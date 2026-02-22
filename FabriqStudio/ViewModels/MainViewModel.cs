using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FabriqStudio.ViewModels;

/// <summary>
/// ナビゲーション管理 — CurrentPage を切り替えることで右ペインの表示を制御する
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly BasicParamsViewModel _basicParamsVm;
    private readonly ModuleEditViewModel  _moduleEditVm;
    private readonly HostListViewModel    _hostListVm;

    [ObservableProperty]
    private object _currentPage;

    public MainViewModel(
        BasicParamsViewModel basicParamsVm,
        ModuleEditViewModel  moduleEditVm,
        HostListViewModel    hostListVm)
    {
        _basicParamsVm = basicParamsVm;
        _moduleEditVm  = moduleEditVm;
        _hostListVm    = hostListVm;

        // 起動時のデフォルトページ
        _currentPage = _basicParamsVm;
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        CurrentPage = page switch
        {
            "BasicParams" => (object)_basicParamsVm,
            "ModuleEdit"  => _moduleEditVm,
            "HostList"    => _hostListVm,
            _             => CurrentPage
        };
    }
}
