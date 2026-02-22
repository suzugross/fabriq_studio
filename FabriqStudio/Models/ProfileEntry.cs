namespace FabriqStudio.Models;

/// <summary>
/// profiles/ ディレクトリ直下の .csv ファイル 1件を表すモデル。
/// ファイル名（拡張子なし）をプロファイル名として扱う。
/// </summary>
public class ProfileEntry
{
    public string Name     { get; set; } = "";
    public string FilePath { get; set; } = "";

    // ComboBox の ToString() 表示用
    public override string ToString() => Name;
}
