using CsvHelper.Configuration.Attributes;

namespace FabriqStudio.Models;

/// <summary>
/// modules/standard/{dir}/module.csv または modules/extended/{dir}/module.csv の 1 行を表すモデル。
/// カラム: MenuName, Category, Script, Order, Enabled
/// ディレクトリから付与: ModuleDir（フォルダ名）, Kind（"standard" / "extended"）
/// </summary>
public class ModuleMasterEntry
{
    // ── module.csv カラム ────────────────────────────────────────
    public string MenuName    { get; set; } = "";
    public string Category    { get; set; } = "";
    public string Script      { get; set; } = "";
    public int    Order       { get; set; }
    public string Enabled     { get; set; } = "0";

    // ── ディレクトリスキャン時に付与するメタデータ ───────────────
    // CsvHelper のヘッダーマッピング対象外にする
    /// <summary>モジュールフォルダ名（例: adobe_reader）</summary>
    [Ignore] public string ModuleDir   { get; set; } = "";

    /// <summary>"standard" または "extended"</summary>
    [Ignore] public string Kind        { get; set; } = "";
}
