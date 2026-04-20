using System.IO;
using FabriqStudio.Helpers;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class PrinterDriverDetectorService : IPrinterDriverDetectorService
{
    public Task<IReadOnlyList<PrinterDriverInfo>> ScanAsync(string scanDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scanDir))
            throw new ArgumentException("スキャン先が指定されていません。", nameof(scanDir));

        if (!Directory.Exists(scanDir))
            throw new DirectoryNotFoundException($"フォルダが存在しません: {scanDir}");

        var root = Path.GetFullPath(scanDir).TrimEnd('\\', '/');

        return Task.Run<IReadOnlyList<PrinterDriverInfo>>(() =>
        {
            var results = new List<PrinterDriverInfo>();

            // 再帰で *.inf を全取得。EnumerateFiles で遅延列挙し、キャンセルに反応可能にする。
            foreach (var infPath in Directory.EnumerateFiles(root, "*.inf", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var models = InfParser.ExtractModelNames(infPath);
                if (models.Count == 0) continue;

                var infFileName = Path.GetFileName(infPath);
                var folderName  = ResolveTopFolderName(root, infPath);

                foreach (var model in models)
                {
                    results.Add(new PrinterDriverInfo
                    {
                        DriverName   = model,
                        InfFileName  = infFileName,
                        InfFilePath  = infPath,
                        FolderName   = folderName,
                        Architecture = InfParser.Architecture,
                    });
                }
            }

            // DriverName の昇順で安定ソートして返す
            return results.OrderBy(r => r.DriverName, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);
    }

    /// <summary>
    /// INF のフルパスから、スキャン起点直下のトップフォルダ名を取り出す。
    /// 例: root=<c>...\INF</c>, inf=<c>...\INF\EPSON PX-S505 Series\sub\x.inf</c> → <c>"EPSON PX-S505 Series"</c>。
    /// INF がスキャン起点の直下にある場合は空文字。
    /// </summary>
    private static string ResolveTopFolderName(string root, string infPath)
    {
        var rel = Path.GetRelativePath(root, infPath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length >= 2 ? parts[0] : "";
    }
}
