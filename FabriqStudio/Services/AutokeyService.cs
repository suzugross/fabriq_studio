using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class AutokeyService : IAutokeyService
{
    private readonly IWorkspaceService _workspace;

    /// <summary>テンプレートベースパス（ワークスペーステンプレート内の fabriq）。カーネル解決用。</summary>
    private static readonly string TemplateFabriqPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "template", "template_fabriq", "fabriq");

    /// <summary>ツール専用テンプレートベースパス（autokey_template 等を格納）。</summary>
    private static readonly string StudioTemplatesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "template", "template_fabriq");

    public AutokeyService(IWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    private string GetRoot() =>
        _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    // ── Load ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RecipeRow>> LoadRecipeAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
            return (IReadOnlyList<RecipeRow>)csv.GetRecords<RecipeRow>().ToList();
        });
    }

    // ── Save (絶対パス指定) ──────────────────────────────────────────────────

    public async Task SaveRecipeAsync(string filePath, IEnumerable<RecipeRow> rows)
    {
        var list = rows.ToList();
        for (int i = 0; i < list.Count; i++)
            list[i].Step = i + 1;

        await Task.Run(() =>
        {
            using var writer = new StreamWriter(filePath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteRecords(list);
        });
    }

    // ── Export ───────────────────────────────────────────────────────────────

    public async Task<string> ExportModuleAsync(
        string                moduleName,
        IEnumerable<RecipeRow> rows,
        bool                  overwrite = false)
    {
        // 1. バリデーション
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("モジュール名を入力してください。");

        var invalidChars = Path.GetInvalidFileNameChars();
        if (moduleName.Any(c => invalidChars.Contains(c)))
            throw new ArgumentException("モジュール名に使用できない文字が含まれています。");

        var sanitized = moduleName.Trim();

        // 2. パス解決
        var fabriqRoot  = GetRoot();
        var destDir     = Path.Combine(fabriqRoot, "modules", "extended", sanitized);

        // 3. 既存チェック
        if (Directory.Exists(destDir))
        {
            if (!overwrite)
                throw new InvalidOperationException($"モジュール「{sanitized}」は既に存在します。");

            await Task.Run(() => Directory.Delete(destDir, recursive: true));
        }

        await Task.Run(() => Directory.CreateDirectory(destDir));

        // 4. autokey_config.ps1 テンプレートをコピー（リネーム）
        var templateScript = ResolveAutokeyTemplatePath();
        var destScriptName = $"{sanitized}_autokey_config.ps1";
        var destScript     = Path.Combine(destDir, destScriptName);
        await Task.Run(() => File.Copy(templateScript, destScript, overwrite: true));

        // 5. module.csv を生成
        var moduleCsvPath = Path.Combine(destDir, "module.csv");
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(moduleCsvPath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("MenuName,Category,Script,Order,Enabled");
            writer.WriteLine($"{sanitized},Automation,{destScriptName},50,0");
        });

        // 6. recipe.csv を保存
        var recipePath = Path.Combine(destDir, "recipe.csv");
        await SaveRecipeAsync(recipePath, rows);

        return destDir;
    }

    // ── Test Run ─────────────────────────────────────────────────────────────

    public async Task<string> TestRunAsync(IEnumerable<RecipeRow> rows)
    {
        // 1. 一時ディレクトリ作成
        var tempDir = Path.Combine(Path.GetTempPath(), $"fabriq_autokey_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 2. recipe.csv を一時ディレクトリに保存
            var recipePath = Path.Combine(tempDir, "recipe.csv");
            await SaveRecipeAsync(recipePath, rows);

            // 3. autokey_config.ps1 テンプレートを一時ディレクトリにコピー
            //    $PSScriptRoot が tempDir に解決され、同ディレクトリの recipe.csv を読み込む
            var templateScript = ResolveAutokeyTemplatePath();
            var tempScript     = Path.Combine(tempDir, "autokey_config.ps1");
            File.Copy(templateScript, tempScript, overwrite: true);

            // 4. カーネル common.ps1 のパスを Dual Resolution で解決
            var kernelPath = ResolveKernelCommonPath();

            // 5. PowerShell コマンド構築
            //    実行順序:
            //    (a) UTF-8 エンコーディング強制
            //    (b) AutoPilotMode + Read-Host モック（dot-source 前に配置 → 読み込み中の対話を防止）
            //    (c) common.ps1 をドットソースで読み込み（カーネル関数群を取得）
            //    (d) Confirm-Execution / Wait-KeyPress をモック上書き（dot-source 後に再定義）
            //    (e) autokey_config.ps1 を実行
            var command =
                "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                "$ProgressPreference = 'SilentlyContinue'; " +
                "$InformationPreference = 'SilentlyContinue'; " +
                "$global:AutoPilotMode = $true; " +
                "function Read-Host { return 'Y' }; " +
                $". '{kernelPath}'; " +
                "function Confirm-Execution { param([string]$Message) return $true }; " +
                "function Wait-KeyPress { param([string]$Message) return }; " +
                $"& '{tempScript}'";

            // 6. PowerShell プロセス起動
            //    -EncodedCommand: Base64(UTF-16LE) でコマンドを渡し、引用符のエスケープ問題を完全回避
            //    -NonInteractive: 対話プロンプトを完全抑止
            //    RedirectStandardInput: stdin を閉じて万一の入力待ちを防止
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                RedirectStandardInput  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            // TaskCompletionSource でストリーム終端（Data == null）を検知
            // → プロセス終了後のバッファフラッシュを確実に待機しデッドロックを回避
            var stdoutDone = new TaskCompletionSource<bool>();
            var stderrDone = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stdout.AppendLine(e.Data);
                else
                    stdoutDone.TrySetResult(true);   // null = ストリーム終端
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stderr.AppendLine(e.Data);
                else
                    stderrDone.TrySetResult(true);   // null = ストリーム終端
            };

            process.Start();

            // stdin を即座に閉じる（PowerShell の Read-Host 等による入力待ちを防止）
            process.StandardInput.Close();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 7. タイムアウト 5 分（WaitForExitAsync で UI スレッドをブロックしない）
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("テスト実行がタイムアウトしました（5分）。");
            }

            // 8. ストリーム読み取り完了を待機（プロセス終了後のバッファフラッシュ）
            await Task.WhenAll(stdoutDone.Task, stderrDone.Task);

            // 9. ログ結合（stdout + CLIXML フィルタ済み stderr）
            var log = stdout.ToString();
            var stderrText = stderr.ToString();

            // CLIXML ブロック（Write-Progress/Write-Information の XML シリアライズ）を除去
            if (!string.IsNullOrWhiteSpace(stderrText) && !stderrText.Contains("#< CLIXML"))
                log += "\n--- stderr ---\n" + stderrText;

            return log;
        }
        finally
        {
            // 10. 一時ディレクトリ削除（クリーンアップ失敗は無視）
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ── パス解決ヘルパー ─────────────────────────────────────────────────────

    /// <summary>
    /// カーネル common.ps1 のパスを解決する（Dual Kernel Resolution）。
    /// 1. ワークスペース内 kernel/common.ps1
    /// 2. フォールバック: アプリ同梱テンプレート
    /// </summary>
    private string ResolveKernelCommonPath()
    {
        if (_workspace.RootPath is not null)
        {
            var workspacePath = Path.Combine(_workspace.RootPath, "kernel", "common.ps1");
            if (File.Exists(workspacePath))
                return workspacePath;
        }

        var templatePath = Path.Combine(TemplateFabriqPath, "kernel", "common.ps1");
        if (File.Exists(templatePath))
            return templatePath;

        throw new FileNotFoundException(
            "カーネル (kernel/common.ps1) が見つかりません。\n" +
            "ワークスペースまたはアプリテンプレートにカーネルが存在することを確認してください。");
    }

    /// <summary>
    /// autokey_config.ps1 テンプレートのパスを解決する。
    /// 常にアプリ同梱の StudioTemplatesPath/autokey_template/ を参照する。
    /// </summary>
    private static string ResolveAutokeyTemplatePath()
    {
        var path = Path.Combine(StudioTemplatesPath, "autokey_template", "autokey_config.ps1");
        if (File.Exists(path))
            return path;

        throw new FileNotFoundException(
            "Autokey テンプレート (autokey_config.ps1) が見つかりません。\n" +
            "アプリ同梱テンプレートに autokey_template/autokey_config.ps1 が存在することを確認してください。");
    }
}
