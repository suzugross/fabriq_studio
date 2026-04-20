using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// 指定フォルダ配下の INF ファイルを走査し、プリンタドライバ（モデル名）一覧を抽出するサービス。
/// ワークスペース非依存。Phase 1 では EXE/ZIP の自動展開は行わず、既に展開済みのフォルダのみを対象とする。
/// </summary>
public interface IPrinterDriverDetectorService
{
    /// <summary>
    /// 指定ディレクトリ配下（再帰）の *.inf をすべてスキャンし、
    /// <see cref="Helpers.InfParser"/> で抽出したドライバ名を <see cref="PrinterDriverInfo"/> として返す。
    /// 該当なしの場合は空リスト。
    /// </summary>
    /// <param name="scanDir">スキャン起点ディレクトリの絶対パス。</param>
    /// <param name="ct">キャンセルトークン（大規模フォルダ対策）。</param>
    Task<IReadOnlyList<PrinterDriverInfo>> ScanAsync(string scanDir, CancellationToken ct = default);
}
