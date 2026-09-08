namespace FabriqStudio.Models.Master;

/// <summary>
/// 「レジストリ追加」章で辞書から選んだ 1 件。回答 JSON の tables["registry_entries"] に 1 行として保存する。
/// <see cref="Title"/> は辞書からエントリが消えたときの表示用、<see cref="Value"/> は行ごとの値（辞書の値を初期値にし、変更できる）。
/// </summary>
public sealed class RegistrySelection
{
    /// <summary>レジストリ辞書のエントリ ID（8 桁 hex）。</summary>
    public string Id    { get; set; } = "";
    public string Title { get; set; } = "";
    public string Value { get; set; } = "";

    public static RegistrySelection FromRow(IReadOnlyDictionary<string, string> row) => new()
    {
        Id    = Get(row, "Id").Trim(),
        Title = Get(row, "Title"),
        Value = Get(row, "Value"),
    };

    public Dictionary<string, string> ToRow() => new(StringComparer.Ordinal)
    {
        ["Id"]    = Id,
        ["Title"] = Title,
        ["Value"] = Value,
    };

    private static string Get(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) ? v ?? "" : "";
}
