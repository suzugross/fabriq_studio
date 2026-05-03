namespace FabriqStudio.Models;

/// <summary>
/// modules/extended/pianist/profiles/&lt;name&gt;/ ディレクトリ 1 件を表す Pianist Profile のエントリ。
/// 通常の <see cref="ProfileEntry"/>（profiles/*.csv）とは別物 — Pianist は
/// 1 Profile = 1 フォルダ で複数ファイル（pianist.json / procedure.csv / values.csv /
/// shortcuts.csv / instructions/*.txt）を内包する。
/// </summary>
public class PianistProfileEntry
{
    /// <summary>フォルダ名 = プロファイル ID（半角英数 + アンダースコア）。</summary>
    public string Name { get; set; } = "";

    /// <summary>プロファイルフォルダの絶対パス。</summary>
    public string FolderPath { get; set; } = "";

    public override string ToString() => Name;
}
