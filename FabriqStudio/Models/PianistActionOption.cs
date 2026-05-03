namespace FabriqStudio.Models;

/// <summary>
/// pianist の Action 列の選択肢（§4.2）。
///
/// CSV には <see cref="Code"/>（英名）を書き、UI には <see cref="Label"/>（日本語）を表示する。
/// Studio の Step グリッドでは <c>DataGridComboBoxColumn</c> の SelectedValuePath=Code /
/// DisplayMemberPath=Label にバインドして利用する。
/// </summary>
public class PianistActionOption
{
    public string Code  { get; init; } = "";
    public string Label { get; init; } = "";

    public override string ToString() => $"{Label} ({Code})";
}
