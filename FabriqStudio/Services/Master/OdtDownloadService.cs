using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace FabriqStudio.Services.Master;

public sealed class OdtDownloadService : IOdtDownloadService
{
    public const string DownloadXmlName = "download_configuration.xml";

    /// <summary>
    /// 生成済み configuration.xml に SourcePath（製品フォルダの絶対パス）を付けたダウンロード用 XML を作る。
    /// ODT は SourcePath の直下に Office\ を作るため、fabriq の odt_install が期待する配置になる。
    /// </summary>
    public static string BuildDownloadXml(string configurationXml, string sourcePath)
    {
        var doc = XDocument.Parse(configurationXml);
        var add = doc.Root?.Element("Add")
                  ?? throw new InvalidDataException("configuration.xml に <Add> 要素がありません。");
        add.SetAttributeValue("SourcePath", sourcePath.TrimEnd('\\'));
        return doc.ToString() + Environment.NewLine;
    }

    public async Task<OdtDownloadResult> DownloadAsync(
        string setupExePath, string productFolder, string configurationXml,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (!File.Exists(setupExePath))
            return new OdtDownloadResult { Success = false, ExitCode = -1, Message = $"setup.exe がありません: {setupExePath}" };

        string xmlPath;
        try
        {
            Directory.CreateDirectory(productFolder);
            xmlPath = Path.Combine(productFolder, DownloadXmlName);
            await File.WriteAllTextAsync(xmlPath, BuildDownloadXml(configurationXml, productFolder), ct);
        }
        catch (Exception ex)
        {
            return new OdtDownloadResult { Success = false, ExitCode = -1, Message = $"ダウンロード用 XML を書けません: {ex.Message}" };
        }

        var psi = new ProcessStartInfo
        {
            FileName         = setupExePath,
            Arguments        = $"/download \"{xmlPath}\"",
            WorkingDirectory = productFolder,
            UseShellExecute  = false,
            CreateNoWindow   = false,
        };

        Process? proc;
        try
        {
            proc = Process.Start(psi);
            if (proc is null)
                return new OdtDownloadResult { Success = false, ExitCode = -1, Message = "setup.exe を起動できませんでした。", DownloadXmlPath = xmlPath };
        }
        catch (Exception ex)
        {
            return new OdtDownloadResult { Success = false, ExitCode = -1, Message = $"setup.exe を起動できませんでした: {ex.Message}", DownloadXmlPath = xmlPath };
        }

        using (proc)
        {
            var started = DateTime.Now;
            progress?.Report("setup.exe /download を起動しました（ODT のウィンドウが開きます）");

            try
            {
                // 2 秒ごとに経過と Office\ の書き込み量を報告しながら終了を待つ
                while (!proc.HasExited)
                {
                    await Task.Delay(2000, ct);
                    var elapsed = DateTime.Now - started;
                    progress?.Report($"ダウンロード中... 経過 {elapsed:mm\\:ss}  Office\\ {FormatSize(FolderSize(Path.Combine(productFolder, "Office")))}");
                }
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* 既に終了 */ }
                return new OdtDownloadResult { Success = false, ExitCode = -2, Message = "中止しました。", DownloadXmlPath = xmlPath };
            }

            var version = FindDataVersion(productFolder);
            var ok      = proc.ExitCode == 0 && version is not null;
            var elapsedTotal = DateTime.Now - started;

            return new OdtDownloadResult
            {
                Success         = ok,
                ExitCode        = proc.ExitCode,
                DataVersion     = version,
                DownloadXmlPath = xmlPath,
                Message = ok
                    ? $"✓ 完了: Office\\Data\\{version}（{FormatSize(FolderSize(Path.Combine(productFolder, "Office")))}、{elapsedTotal:mm\\:ss}）"
                    : proc.ExitCode == 0
                        ? "setup.exe は終了しましたが Office\\Data が見つかりません。ODT のウィンドウの表示を確認してください。"
                        : $"setup.exe が終了コード {proc.ExitCode} で終了しました。インターネット接続と XML の製品指定を確認してください。",
            };
        }
    }

    private static string? FindDataVersion(string productFolder)
    {
        var data = Path.Combine(productFolder, "Office", "Data");
        if (!Directory.Exists(data)) return null;
        return Directory.GetDirectories(data)
            .Select(Path.GetFileName)
            .Where(n => n is not null && n.StartsWith("16.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .LastOrDefault();
    }

    private static long FolderSize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _           => $"{bytes / 1024.0:0} KB",
    };
}
