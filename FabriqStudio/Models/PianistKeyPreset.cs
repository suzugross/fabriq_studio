namespace FabriqStudio.Models;

/// <summary>
/// Pianist の Key Action 用 SendKeys プリセットの 1 件。
///
/// CSV / Pianist 実行時に渡されるのは <see cref="Code"/>（SendKeys 表記）だけで、
/// <see cref="Description"/> は ComboBox ドロップダウンの 2 列目に表示する解説用。
/// ComboBox 側で <c>TextSearch.TextPath="Code"</c> を指定することで、選択時に
/// テキストボックスへ書き戻されるのは Code のみとなる。
/// </summary>
public class PianistKeyPreset
{
    /// <summary>SendKeys 表記（CSV にそのまま書く文字列）。</summary>
    public string Code        { get; init; } = "";

    /// <summary>ドロップダウン表示用の日本語解説。</summary>
    public string Description { get; init; } = "";

    public override string ToString() => Code;
}
