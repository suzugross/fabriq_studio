namespace FabriqStudio.Models;

/// <summary>
/// profiles/*.csv の1行を表すモデル。
/// カラム: Order, ScriptPath, Enabled, Description
/// </summary>
public class ProfileScriptEntry
{
    public int    Order       { get; set; }
    public string ScriptPath  { get; set; } = "";
    public string Enabled     { get; set; } = "0";
    public string Description { get; set; } = "";

    /// <summary>
    /// __RESTART__ / __AUTOPILOT__ 等のシステム組み込みコマンドかどうか。
    /// View の行スタイル切り替え用。
    /// </summary>
    public bool IsSystemCommand =>
        ScriptPath.StartsWith("__", StringComparison.Ordinal) &&
        ScriptPath.EndsWith("__", StringComparison.Ordinal);
}
