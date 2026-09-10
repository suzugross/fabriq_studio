using System.Data;

namespace FabriqStudio.Services.Collect;

/// <summary>
/// 採取したものを「現在のプロファイル」のデータフォルダ（profiles/&lt;名&gt;/modules/）へ配置する側。
/// そのプロファイルでモジュールが未使用なら、設定 CSV の取り込みとプロファイルへの実行行の追加を自動で行う。
/// </summary>
public interface ICollectPlacementService
{
    /// <summary>PDF に設定 CSV が無ければ本体から as-is で取り込む。取り込んだら true。</summary>
    Task<bool> EnsureCsvAsync(string profile, string moduleDir, string csvName);

    /// <summary>設定 CSV を読む（PDF にあればそれ、無ければ本体。取り込みはしない = 重複判定用）。</summary>
    Task<DataTable> ReadCsvAsync(string profile, string moduleDir, string csvName);

    /// <summary>
    /// PDF の設定 CSV（無ければ取り込んでから）に行を追加する。
    /// <paramref name="uniqueColumn"/> があれば、その列が既存行と同じ（大文字小文字無視）行は追加しない。
    /// <paramref name="numberColumn"/> があれば、その列に「既存の最大値 + <paramref name="numberStep"/>」を順に振る。
    /// 行の辞書に無い列は空で埋める。
    /// </summary>
    Task<CsvAppendResult> AppendRowsAsync(
        string profile, string moduleDir, string csvName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        string? uniqueColumn = null, string? numberColumn = null, int numberStep = 10);

    /// <summary>資材の書き先（PDF 側の絶対パス）。フォルダは作らない。</summary>
    string AssetPath(string profile, string moduleDir, string relInModule);

    /// <summary>プロファイル CSV にそのスクリプトの行が無ければ末尾（Order+10）に追加する。追加したら true。</summary>
    Task<bool> EnsureProfileRowAsync(string profile, string moduleDir, string scriptFile);
}
