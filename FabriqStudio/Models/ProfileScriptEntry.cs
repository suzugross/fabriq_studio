using CommunityToolkit.Mvvm.ComponentModel;
using CsvHelper.Configuration.Attributes;

namespace FabriqStudio.Models;

/// <summary>
/// profiles/*.csv の1行を表すモデル。
/// カラム: Order, ScriptPath, Enabled, Description
/// </summary>
public partial class ProfileScriptEntry : ObservableObject
{
    [ObservableProperty] private int    _order       ;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemCommand))]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _scriptPath  = "";
    [ObservableProperty] private string _enabled     = "0";
    [ObservableProperty] private string _description = "";

    /// <summary>
    /// __RESTART__ / __AUTOPILOT__ 等のシステム組み込みコマンドかどうか。
    /// View の行スタイル切り替え用。
    /// </summary>
    [Ignore]
    public bool IsSystemCommand =>
        ScriptPath.StartsWith("__", StringComparison.Ordinal) &&
        ScriptPath.EndsWith("__", StringComparison.Ordinal);

    /// <summary>左ペインの ListBox 等で表示するフレンドリー名。CSV には書き出さない。</summary>
    [Ignore]
    public string DisplayName => IsSystemCommand
        ? ScriptPath
        : System.IO.Path.GetFileNameWithoutExtension(ScriptPath);
}
