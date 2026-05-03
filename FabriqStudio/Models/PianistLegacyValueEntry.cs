namespace FabriqStudio.Models;

/// <summary>
/// 旧 long format values.csv（pianist 1.0.0）の 1 行。
/// 列: Key, Value, Encrypted, Note
///
/// 移行時は <see cref="Key"/> が wide 列名、<see cref="Value"/> が `*` 行のセル値、
/// <see cref="Encrypted"/>="1" のときは値の頭に "ENC:" prefix を付与（§5.5）。
/// </summary>
public class PianistLegacyValueEntry
{
    public string Key       { get; set; } = "";
    public string Value     { get; set; } = "";
    public string Encrypted { get; set; } = "";
    public string Note      { get; set; } = "";
}
