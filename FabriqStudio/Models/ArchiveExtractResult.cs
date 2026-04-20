namespace FabriqStudio.Models;

/// <summary>
/// <see cref="Services.IPrinterDriverDetectorService.ExtractArchivesAsync"/> の結果。
/// 成功件数・既展開でスキップ件数・失敗件数の集計と、人が読めるメッセージ一覧を返す。
/// </summary>
public sealed class ArchiveExtractResult
{
    /// <summary>新たに展開されたアーカイブ数。</summary>
    public int Extracted { get; init; }

    /// <summary>同名フォルダ既存のためスキップされた数（冪等）。</summary>
    public int Skipped   { get; init; }

    /// <summary>展開失敗した数（7z 未発見の .exe を含む）。</summary>
    public int Failed    { get; init; }

    /// <summary>ステータス欄 / ログに出せる 1 行メッセージの時系列。</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];
}
