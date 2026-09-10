using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FabriqStudio.Services.Collect;

public sealed class PowerShellRunner : IPowerShellRunner
{
    private static readonly string PowerShellExe =
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>全スクリプト共通の前置き: エラーで止める・出力は UTF-8・進捗バーを出さない。</summary>
    private const string Prologue =
        "$ErrorActionPreference = 'Stop'\r\n" +
        "$ProgressPreference = 'SilentlyContinue'\r\n" +
        "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\r\n";

    public async Task<PowerShellResult> RunAsync(string script, CancellationToken ct = default)
    {
        var file = WriteTempScript(Prologue + script);
        try
        {
            return await RunProcessAsync(PowerShellExe,
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\"", ct);
        }
        finally
        {
            TryDelete(file);
        }
    }

    public async Task<PowerShellResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* 既に終了 */ }
            throw;
        }
        return new PowerShellResult(p.ExitCode, await stdout, await stderr, Cancelled: false);
    }

    public async Task<PowerShellResult> RunElevatedAsync(string script, CancellationToken ct = default)
    {
        var file    = WriteTempScript(Prologue + script);
        var outFile = file + ".out.txt";
        var psi = new ProcessStartInfo
        {
            FileName        = PowerShellExe,
            // 昇格プロセスは標準出力を親に返せないので、全ストリームをファイルへ。例外は 1 で終える
            Arguments       = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command " +
                              $"\"try {{ & '{file}' *> '{outFile}'; exit 0 }} catch {{ $_ | Out-File -Append -LiteralPath '{outFile}'; exit 1 }}\"",
            UseShellExecute = true,
            Verb            = "runas",
            WindowStyle     = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new PowerShellResult(-1, "", "PowerShell を起動できませんでした", Cancelled: false);
            await p.WaitForExitAsync(ct);
            var output = File.Exists(outFile) ? File.ReadAllText(outFile) : "";
            return new PowerShellResult(p.ExitCode, output, "", Cancelled: false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new PowerShellResult(-1, "", "", Cancelled: true);   // UAC でキャンセル
        }
        finally
        {
            TryDelete(file);
            TryDelete(outFile);
        }
    }

    private static string WriteTempScript(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fabriq_studio_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));   // 5.1 は BOM で UTF-8 と判定する
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 後始末の失敗は無視 */ }
    }
}
