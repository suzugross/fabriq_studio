namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// 回答の一部を fabriq の CSV 行・プロファイル行・レジストリ行・手動作業に変換する単位。
/// 実装は <see cref="MasterContext"/> 経由でのみ計画に書き込む。
/// </summary>
public interface IMasterEmitter
{
    /// <summary>表示・診断用の名前。</summary>
    string Name { get; }

    void Emit(MasterContext ctx);
}

/// <summary>Emitter 共通の小さなヘルパ。</summary>
internal static class EmitterHelpers
{
    /// <summary>列名 → 値 の行を作る（大文字小文字を無視するキー比較）。</summary>
    public static Dictionary<string, string> Row(params (string Column, string Value)[] cells)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (c, v) in cells) row[c] = v ?? "";
        return row;
    }

    /// <summary>テーブル行のセル値（列名の大文字小文字を無視、無ければ空）。</summary>
    public static string Cell(this IReadOnlyDictionary<string, string> row, string column)
    {
        foreach (var (k, v) in row)
            if (k.Equals(column, StringComparison.OrdinalIgnoreCase)) return v ?? "";
        return "";
    }

    public static string Cell(this Dictionary<string, string> row, string column)
        => ((IReadOnlyDictionary<string, string>)row).Cell(column);

    /// <summary>
    /// 副セグメント用に名前を英数字だけに詰める（例: "Google Chrome" → "GoogleChrome"）。
    /// 空になる場合は空文字を返す。長さは <paramref name="maxLength"/> まで。
    /// </summary>
    public static string ToSegmentToken(string? name, int maxLength = 24)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var chars = name.Where(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')).Take(maxLength);
        return new string(chars.ToArray());
    }
}
