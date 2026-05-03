using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// shortcuts.csv の 1 行を表すモデル。
/// カラム: Label, Type, Path, Args, Note
///
/// pianist.ps1 v1.x は UI から呼んでおらず、ファイル参照のみ。
/// Studio v1 では単純な表 grid で編集できる程度に留める（§6）。
/// </summary>
public partial class PianistShortcut : ObservableObject
{
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _type  = "";
    [ObservableProperty] private string _path  = "";
    [ObservableProperty] private string _args  = "";
    [ObservableProperty] private string _note  = "";
}
