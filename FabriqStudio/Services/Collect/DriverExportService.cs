using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FabriqStudio.Services.Collect;

public sealed class DriverExportService : IDriverExportService
{
    private readonly IPowerShellRunner _ps;

    public DriverExportService(IPowerShellRunner ps)
    {
        _ps = ps;
    }

    /// <summary>
    /// fabriq driver_config の Get-SafeModelName と同じ正規化: 空白→_、パス禁止文字を除去、前後の _ と . を落とし、80 文字まで。
    /// 参照機の名前で置いたフォルダを、対象機が自分のモデル名で見つけるために同じ規則にする。
    /// </summary>
    public static string SafeModelName(string raw)
    {
        var s = Regex.Replace(raw ?? "", @"\s", "_");
        s = Regex.Replace(s, @"[\\/:*?""<>|]", "");
        s = s.Trim('_').Trim('.');
        if (s.Length > 80) s = s[..80];
        return string.IsNullOrWhiteSpace(s) ? "Unknown_Model" : s;
    }

    public string GetModelName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var model = key?.GetValue("SystemProductName") as string;
            return string.IsNullOrWhiteSpace(model) ? "" : model.Trim();
        }
        catch
        {
            return "";
        }
    }

    public async Task<DriverCaptureResult> ExportAsync(string destDir, CancellationToken ct = default)
    {
        var resultFile = Path.Combine(Path.GetTempPath(), $"fabriq_studio_driver_{Guid.NewGuid():N}.json");
        var script =
            $"$dest = '{Esc(destDir)}'\r\n" +
            $"$res  = '{Esc(resultFile)}'\r\n" +
            // fabriq と同じ: 既存フォルダを消してから作り直し、dism.exe で採取（Export-WindowsDriver は Server 2022 で失敗するため）
            "if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }\r\n" +
            "New-Item -ItemType Directory -Force -Path $dest | Out-Null\r\n" +
            "$out  = & dism.exe /online /export-driver /destination:\"$dest\" 2>&1\r\n" +
            "$code = $LASTEXITCODE\r\n" +
            "$count = @(Get-ChildItem -LiteralPath $dest -Directory -ErrorAction SilentlyContinue).Count\r\n" +
            "$last  = [string]($out | Where-Object { $_ -and $_.ToString().Trim().Length -gt 0 } | Select-Object -Last 1)\r\n" +
            "@{ ExitCode = $code; Count = $count; Message = $last } | ConvertTo-Json -Compress | Set-Content -LiteralPath $res -Encoding UTF8\r\n" +
            "exit 0\r\n";

        try
        {
            var r = await _ps.RunElevatedAsync(script, ct);
            if (r.Cancelled) return new DriverCaptureResult(false, true, 0, "キャンセルしました");
            if (!File.Exists(resultFile))
                return new DriverCaptureResult(false, false, 0, FirstLine(r.StdOut) ?? "採取に失敗しました");

            using var doc = JsonDocument.Parse(File.ReadAllText(resultFile));
            var root  = doc.RootElement;
            var code  = root.TryGetProperty("ExitCode", out var c) && c.TryGetInt32(out var ci) ? ci : -1;
            var count = root.TryGetProperty("Count", out var n) && n.TryGetInt32(out var ni) ? ni : 0;
            var msg   = root.TryGetProperty("Message", out var m) ? m.GetString() ?? "" : "";
            return code == 0
                ? new DriverCaptureResult(true, false, count, msg)
                : new DriverCaptureResult(false, false, count, $"dism.exe が {code} で終了しました: {msg}");
        }
        finally
        {
            try { if (File.Exists(resultFile)) File.Delete(resultFile); } catch { /* 後始末の失敗は無視 */ }
        }
    }

    private static string Esc(string s) => s.Replace("'", "''");

    private static string? FirstLine(string s)
        => (s ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}
