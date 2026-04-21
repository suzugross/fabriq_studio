namespace FabriqStudio.Models;

/// <summary>
/// modules/{kind}/{module}/preset.csv の 1 行を表すモデル。
/// <para>
/// スキーマ: Column, Value, Label（固定3列、UTF-8 BOM）
/// </para>
/// <list type="bullet">
///   <item><c>Column</c>: 対象 CSV の列名（大文字小文字は区別しない）</item>
///   <item><c>Value</c>: セルに書き込まれる実値</item>
///   <item><c>Label</c>: 表示用ラベル。Phase 1 では UI 表示に使わない（将来の拡張用に予約）</item>
/// </list>
/// </summary>
public sealed class ModulePresetEntry
{
    public string Column { get; set; } = "";
    public string Value  { get; set; } = "";
    public string Label  { get; set; } = "";
}
