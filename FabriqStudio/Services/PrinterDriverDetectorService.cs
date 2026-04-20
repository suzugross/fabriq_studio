using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using FabriqStudio.Helpers;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class PrinterDriverDetectorService : IPrinterDriverDetectorService
{
    /// <summary>ワークスペース内のエクスポート先 CSV 相対パス。</summary>
    private const string DriverListRelPath =
        @"modules\standard\printer_driver_config\printer_driver_list.csv";

    // ═══════════════════════════════════════════════════════════════════════
    // Scan
    // ═══════════════════════════════════════════════════════════════════════

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

            return results.OrderBy(r => r.DriverName, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ExtractArchives (Phase 2)
    // ═══════════════════════════════════════════════════════════════════════

    public Task<ArchiveExtractResult> ExtractArchivesAsync(
        string scanDir, string? sevenZipPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scanDir))
            throw new ArgumentException("スキャン先が指定されていません。", nameof(scanDir));

        if (!Directory.Exists(scanDir))
            throw new DirectoryNotFoundException($"フォルダが存在しません: {scanDir}");

        var has7z = !string.IsNullOrEmpty(sevenZipPath) && File.Exists(sevenZipPath);

        return Task.Run<ArchiveExtractResult>(() =>
        {
            var archives = Directory.EnumerateFiles(scanDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int extracted = 0, skipped = 0, failed = 0;
            var messages = new List<string>();

            foreach (var archive in archives)
            {
                ct.ThrowIfCancellationRequested();

                var arcName   = Path.GetFileName(archive);
                var baseName  = Path.GetFileNameWithoutExtension(archive);
                var targetDir = Path.Combine(scanDir, baseName);
                var isZip     = archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

                // 冪等性: 同名フォルダがあれば再展開しない
                if (Directory.Exists(targetDir))
                {
                    skipped++;
                    messages.Add($"既展開: {arcName}");
                    continue;
                }

                if (has7z)
                {
                    if (Extract7z(sevenZipPath!, archive, targetDir, out var msg))
                    {
                        extracted++;
                        messages.Add($"展開: {arcName}");
                    }
                    else
                    {
                        failed++;
                        messages.Add($"失敗: {arcName}{(msg is null ? "" : " - " + msg)}");
                        TryCleanupPartialDir(targetDir);
                    }
                }
                else if (isZip)
                {
                    // 7z なしのフォールバック: .zip のみ System.IO.Compression で展開
                    try
                    {
                        ZipFile.ExtractToDirectory(archive, targetDir);
                        extracted++;
                        messages.Add($"展開 (組込ZIP): {arcName}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        messages.Add($"失敗: {arcName} - {ex.Message}");
                        TryCleanupPartialDir(targetDir);
                    }
                }
                else
                {
                    // 7z なし + .exe → 展開不可
                    failed++;
                    messages.Add($"不可: {arcName} (7z.exe 未配置のため .exe は展開できません)");
                }
            }

            return new ArchiveExtractResult
            {
                Extracted = extracted,
                Skipped   = skipped,
                Failed    = failed,
                Messages  = messages,
            };
        }, ct);
    }

    /// <summary>
    /// 7z.exe で <paramref name="archive"/> を <paramref name="targetDir"/> へ展開する。
    /// fabriq の PS 実装と同じ引数（<c>-bso0 -bsp0</c> で出力抑制、<c>-y</c> で確認なし）。
    /// </summary>
    private static bool Extract7z(string sevenZipExe, string archive, string targetDir, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = sevenZipExe,
                Arguments              = $"x \"{archive}\" \"-o{targetDir}\" -y -bso0 -bsp0",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("7z.exe を起動できませんでした。");

            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                errorMessage = $"exit={p.ExitCode}";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static void TryCleanupPartialDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best effort */ }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ExportToWorkspace (Phase 2)
    // ═══════════════════════════════════════════════════════════════════════

    public Task<DriverExportResult> ExportToWorkspaceAsync(
        PrinterDriverInfo driver, string workspaceRootPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (string.IsNullOrEmpty(workspaceRootPath))
            throw new ArgumentException("ワークスペースパスが指定されていません。", nameof(workspaceRootPath));

        var csvPath = Path.Combine(workspaceRootPath, DriverListRelPath);

        return Task.Run(() =>
        {
            try
            {
                var rows = LoadDriverListRows(csvPath);

                // DriverName の重複チェック（大文字小文字無視）
                if (rows.Any(r => string.Equals(r.DriverName, driver.DriverName,
                                                StringComparison.OrdinalIgnoreCase)))
                {
                    return new DriverExportResult { Skipped = 1 };
                }

                rows.Add(new DriverListRow
                {
                    Enabled     = "1",
                    TargetHost  = "",
                    DriverName  = driver.DriverName,
                    Description = $"Detected from {driver.InfFileName}",
                });

                Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

                // BOM 付き UTF-8 で全件書き戻す（PowerShell 5.1 の Import-Csv 互換）
                using var writer = new StreamWriter(csvPath, append: false,
                    encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                csv.WriteRecords(rows);

                return new DriverExportResult { Added = 1 };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new DriverExportResult { Error = $"アクセスが拒否されました: {ex.Message}" };
            }
            catch (IOException ex)
            {
                return new DriverExportResult { Error = $"ファイル操作エラー: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new DriverExportResult { Error = $"予期しないエラー: {ex.Message}" };
            }
        }, ct);
    }

    /// <summary>
    /// <c>printer_driver_list.csv</c> を読み込む。存在しない / 読み込み失敗時は空リスト。
    /// </summary>
    private static List<DriverListRow> LoadDriverListRows(string csvPath)
    {
        if (!File.Exists(csvPath)) return [];

        try
        {
            using var reader = new StreamReader(csvPath, detectEncodingFromByteOrderMarks: true);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
            return csv.GetRecords<DriverListRow>().ToList();
        }
        catch
        {
            return [];
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

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

    /// <summary>
    /// <c>printer_driver_list.csv</c> の 1 行を表す内部モデル。
    /// CsvHelper の [Name] 属性でカラム名を明示する。
    /// </summary>
    private sealed class DriverListRow
    {
        [Name("Enabled")]     public string Enabled     { get; set; } = "1";
        [Name("TargetHost")]  public string TargetHost  { get; set; } = "";
        [Name("DriverName")]  public string DriverName  { get; set; } = "";
        [Name("Description")] public string Description { get; set; } = "";
    }
}
