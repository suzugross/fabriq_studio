using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// kernel/csv/log_destinations.csv の1行を表すモデル。
/// Enabled は fabriq の慣例に合わせて "0"/"1" 文字列のまま保持する。
/// </summary>
public partial class LogDestination : ObservableObject
{
    [ObservableProperty] private string _path        = "";
    [ObservableProperty] private string _type        = "";
    [ObservableProperty] private string _enabled     = "0";
    [ObservableProperty] private string _authUser    = "";
    [ObservableProperty] private string _authPass    = "";
    [ObservableProperty] private string _description = "";
}
