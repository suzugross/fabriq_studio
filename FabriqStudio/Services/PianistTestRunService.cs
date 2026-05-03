using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// <see cref="IPianistTestRunService"/> 実装。
///
/// 設計の根拠は PoC（pianist GUI を <c>powershell.exe -STA</c> + <c>RedirectStandardOutput</c>
/// 下で起動できることを実機で確認）に基づく。LooperService.TestRunAsync と同じ
/// プロセス起動パターンを採用しつつ、pianist 固有の前提（kernel dot-source / passphrase
/// global 注入 / Import-ModuleCsv モック）を上乗せしている。
/// </summary>
public class PianistTestRunService : IPianistTestRunService
{
    /// <summary>ModuleResult JSON の前に置く Sentinel 文字列。stdout から検出して parse する。</summary>
    private const string ResultSentinel = "===PIANIST_TEST_RESULT===";

    private readonly IWorkspaceService _workspace;
    private readonly ICryptoService    _crypto;

    public PianistTestRunService(IWorkspaceService workspace, ICryptoService crypto)
    {
        _workspace = workspace;
        _crypto    = crypto;
    }

    public async Task<PianistTestRunResult> RunAsync(
        string profileName,
        string newPCName,
        CancellationToken ct = default)
    {
        var fabriqRoot = _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

        var commonPs1 = Path.Combine(fabriqRoot, "kernel", "common.ps1");
        if (!File.Exists(commonPs1))
            throw new FileNotFoundException(
                $"カーネル (kernel/common.ps1) が見つかりません: {commonPs1}");

        var pianistPs1 = Path.Combine(fabriqRoot, "modules", "extended", "pianist", "pianist.ps1");
        if (!File.Exists(pianistPs1))
            throw new FileNotFoundException(
                $"pianist.ps1 が見つかりません: {pianistPs1}");

        // ラッパスクリプト構築
        // - PS 内文字列リテラルは single-quote で包み、内部 ' は '' エスケープする
        // - profile 名 / NewPCName / passphrase はユーザー入力相当なので必ずサニタイズ
        var script = BuildWrapperScript(commonPs1, pianistPs1, profileName, newPCName, _crypto.MasterPassphrase);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var psi = new ProcessStartInfo
        {
            FileName               = "powershell.exe",
            Arguments              = $"-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            WorkingDirectory       = fabriqRoot,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutDone = new TaskCompletionSource<bool>();
        var stderrDone = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
            else
                stdoutDone.TrySetResult(true);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
            else
                stderrDone.TrySetResult(true);
        };

        process.Start();
        // stdin を即座に閉じて Read-Host 等の入力待ちを完全抑止
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // GUI 操作待ちなので既定タイムアウトはなし。CancellationToken のみで制御。
        bool cancelled = false;
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
            // Kill 後の WaitForExit は同期 + 短時間タイムアウト
            try { process.WaitForExit(2000); }
            catch { /* best effort */ }
        }

        // ストリーム終端まで待つ（stdout/stderr バッファのフラッシュ）
        await Task.WhenAll(stdoutDone.Task, stderrDone.Task);

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        // ModuleResult JSON を sentinel 経由で抽出
        var (status, message, verified) = TryParseModuleResult(stdoutText);

        // ログ整形: stdout を主、stderr は CLIXML ブロック以外を末尾に追記
        var log = BuildDisplayLog(stdoutText, stderrText, cancelled);

        return new PianistTestRunResult(
            Log:                    log,
            ExitCode:               cancelled ? -1 : process.ExitCode,
            ModuleResultStatus:     status,
            ModuleResultMessage:    message,
            ModuleResultVerified:   verified,
            WasCancelled:           cancelled);
    }

    // ── ラッパスクリプト構築 ──────────────────────────────────────

    /// <summary>
    /// <c>powershell.exe -EncodedCommand</c> に渡すラッパスクリプトを組み立てる。
    /// 内容（順序が意味を持つ）:
    /// 1. UTF-8 出力強制
    /// 2. Confirm-Execution / Wait-KeyPress を **dot-source 前** にスタブ定義
    ///    （common.ps1 ロード中の対話を防止）— LooperService と同じ予防策
    /// 3. <c>kernel/common.ps1</c> dot-source（Show-Info / New-ModuleResult 等の取得）
    /// 4. master passphrase / SELECTED_NEW_PCNAME / FABRIQ_SEGMENT を注入
    /// 5. <c>Import-ModuleCsv</c> を override して合成 1 行を返す
    ///    （profile picker は Items.Count -eq 1 で auto-skip）
    /// 6. <c>Confirm-Execution</c> を再度 stub（dot-source で本体に上書きされたため）
    /// 7. pianist.ps1 を呼び出し、戻り値の ModuleResult を JSON で stdout 末尾に出す
    /// </summary>
    private static string BuildWrapperScript(
        string commonPs1Path,
        string pianistPs1Path,
        string profileName,
        string newPCName,
        string? passphrase)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        sb.AppendLine("$InformationPreference = 'SilentlyContinue'");
        sb.AppendLine();
        sb.AppendLine("# stub before dot-source so common.ps1 cannot prompt while loading");
        sb.AppendLine("function Confirm-Execution { param($Message) return $true }");
        sb.AppendLine("function Wait-KeyPress    { param($Message) return }");
        sb.AppendLine("function Read-Host        { param($Prompt, [switch]$AsSecureString) return '' }");
        sb.AppendLine();
        sb.AppendLine($". {QuotePs(commonPs1Path)}");
        sb.AppendLine();
        sb.AppendLine($"$global:FabriqMasterPassphrase = {QuotePs(passphrase ?? string.Empty)}");
        sb.AppendLine($"$env:SELECTED_NEW_PCNAME       = {QuotePs(newPCName ?? string.Empty)}");
        sb.AppendLine("$env:FABRIQ_SEGMENT             = ''");
        sb.AppendLine();
        sb.AppendLine("# override Import-ModuleCsv with synthetic single-row payload so");
        sb.AppendLine("# pianist's profile picker auto-skips (pianist.ps1:891 Items.Count -eq 1)");
        sb.AppendLine("function Import-ModuleCsv {");
        sb.AppendLine("    return @([PSCustomObject]@{");
        sb.AppendLine("        Enabled     = '1'");
        sb.AppendLine($"        ProfileName = {QuotePs(profileName)}");
        sb.AppendLine("        Group       = 'StudioTest'");
        sb.AppendLine("        Description = 'Studio test run'");
        sb.AppendLine("        Segment     = ''");
        sb.AppendLine("    })");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("# Re-stub after dot-source (common.ps1 may have defined real bodies)");
        sb.AppendLine("function Confirm-Execution { param($Message) return $true }");
        sb.AppendLine("function Wait-KeyPress    { param($Message) return }");
        sb.AppendLine();
        sb.AppendLine($"$result = & {QuotePs(pianistPs1Path)}");
        sb.AppendLine();
        sb.AppendLine("if ($result -and $result._IsModuleResult) {");
        sb.AppendLine($"    Write-Host {QuotePs(ResultSentinel)}");
        sb.AppendLine("    Write-Host (ConvertTo-Json @{");
        sb.AppendLine("        Status   = [string]$result.Status");
        sb.AppendLine("        Message  = [string]$result.Message");
        sb.AppendLine("        Verified = $result.Verified");
        sb.AppendLine("    } -Compress)");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// PowerShell の single-quoted リテラルとしてエスケープ。
    /// PS の '...' 内では ' は '' で表現する（バックスラッシュ事象なし）。
    /// </summary>
    private static string QuotePs(string s)
        => "'" + (s ?? string.Empty).Replace("'", "''") + "'";

    // ── ログ整形 ──────────────────────────────────────────────────

    private static (string? status, string? message, bool? verified) TryParseModuleResult(string stdout)
    {
        var idx = stdout.LastIndexOf(ResultSentinel, StringComparison.Ordinal);
        if (idx < 0) return (null, null, null);

        var afterSentinel = stdout.Substring(idx + ResultSentinel.Length);
        var jsonStart = afterSentinel.IndexOf('{');
        if (jsonStart < 0) return (null, null, null);

        var jsonEnd = afterSentinel.IndexOf('}', jsonStart);
        if (jsonEnd < 0) return (null, null, null);

        var json = afterSentinel.Substring(jsonStart, jsonEnd - jsonStart + 1);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? status   = root.TryGetProperty("Status",   out var s) ? s.GetString() : null;
            string? message  = root.TryGetProperty("Message",  out var m) ? m.GetString() : null;
            bool?   verified = root.TryGetProperty("Verified", out var v) && v.ValueKind != JsonValueKind.Null
                ? v.GetBoolean()
                : null;
            return (status, message, verified);
        }
        catch (JsonException) { return (null, null, null); }
    }

    /// <summary>
    /// 表示用ログを構築。stdout を本体とし、CLIXML 以外の stderr は末尾に追記。
    /// LooperService と同じ filter（<c>#&lt; CLIXML</c> 含むブロックは無視）。
    /// </summary>
    private static string BuildDisplayLog(string stdout, string stderr, bool cancelled)
    {
        var sb = new StringBuilder();
        sb.Append(stdout);

        if (!string.IsNullOrWhiteSpace(stderr) && !stderr.Contains("#< CLIXML"))
        {
            sb.AppendLine();
            sb.AppendLine("--- stderr ---");
            sb.Append(stderr);
        }

        if (cancelled)
        {
            sb.AppendLine();
            sb.AppendLine("--- cancelled by user (process tree killed) ---");
        }

        return sb.ToString();
    }
}
