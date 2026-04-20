using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// 指定フォルダ配下の INF ファイルを走査し、プリンタドライバ（モデル名）一覧を抽出するサービス。
/// ワークスペース非依存（スキャン対象フォルダは任意）。
/// エクスポート機能のみワークスペースパスを呼び出し側から受け取る。
/// </summary>
public interface IPrinterDriverDetectorService
{
    /// <summary>
    /// 指定ディレクトリ配下（再帰）の *.inf をすべてスキャンし、
    /// <see cref="Helpers.InfParser"/> で抽出したドライバ名を <see cref="PrinterDriverInfo"/> として返す。
    /// 該当なしの場合は空リスト。
    /// </summary>
    /// <param name="scanDir">スキャン起点ディレクトリの絶対パス。</param>
    /// <param name="ct">キャンセルトークン。</param>
    Task<IReadOnlyList<PrinterDriverInfo>> ScanAsync(string scanDir, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="scanDir"/> 直下の .exe / .zip を同名フォルダへ展開する。冪等。
    /// <list type="bullet">
    ///   <item><paramref name="sevenZipPath"/> が指定され exe が存在する場合 → 7z.exe を使用</item>
    ///   <item>7z.exe が無く対象が .zip の場合 → <see cref="System.IO.Compression.ZipFile"/> でフォールバック展開</item>
    ///   <item>7z.exe が無く対象が .exe の場合 → 展開不可として Failed にカウント</item>
    /// </list>
    /// </summary>
    Task<ArchiveExtractResult> ExtractArchivesAsync(
        string scanDir, string? sevenZipPath, CancellationToken ct = default);

    /// <summary>
    /// 1 件のドライバ情報を現在のワークスペースの
    /// <c>modules/standard/printer_driver_config/printer_driver_list.csv</c> に追記する。
    /// DriverName が既存行と重複する場合はスキップする（大文字小文字無視）。
    /// CSV が存在しない場合は新規作成する。
    /// </summary>
    Task<DriverExportResult> ExportToWorkspaceAsync(
        PrinterDriverInfo driver, string workspaceRootPath, CancellationToken ct = default);
}
