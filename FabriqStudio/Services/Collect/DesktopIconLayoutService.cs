using System.IO;

namespace FabriqStudio.Services.Collect;

public sealed class DesktopIconLayoutService : IDesktopIconLayoutService
{
    /// <summary>fabriq desktop_icon_config と同じキー。Studio は対話ユーザーで動くので HKCU をそのまま書き出せる。</summary>
    public const string RegistryKey = @"HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop";

    private readonly IPowerShellRunner _ps;

    public DesktopIconLayoutService(IPowerShellRunner ps)
    {
        _ps = ps;
    }

    public async Task<bool> ExportAsync(string destFile, CancellationToken ct = default)
    {
        // 失敗時に空フォルダを残さないよう、一時ファイルに書いてから置く
        var temp = Path.Combine(Path.GetTempPath(), $"fabriq_studio_icons_{Guid.NewGuid():N}.reg");
        try
        {
            var r = await _ps.RunProcessAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"),
                $"export \"{RegistryKey}\" \"{temp}\" /y", ct);
            if (r.ExitCode != 0 || !File.Exists(temp)) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(temp, destFile, overwrite: true);
            return true;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 後始末の失敗は無視 */ }
        }
    }
}
