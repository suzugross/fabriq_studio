using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// kernel/csv/workers.csv の1行を表すモデル
/// </summary>
public partial class WorkerEntry : ObservableObject
{
    [ObservableProperty] private string _iD   = "";
    [ObservableProperty] private string _name = "";
}
