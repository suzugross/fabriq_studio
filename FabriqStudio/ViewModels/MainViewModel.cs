using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;

namespace FabriqStudio.ViewModels;

/// <summary>
/// ナビゲーション管理 — CurrentPage を切り替えることで右ペインの表示を制御する。
/// WeakReferenceMessenger でサブページ間の遷移メッセージを受け取る。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly BasicParamsViewModel   _basicParamsVm;
    private readonly ModuleEditViewModel    _moduleEditVm;
    private readonly HostListViewModel      _hostListVm;
    private readonly HostDetailViewModel    _hostDetailVm;
    private readonly ModuleDetailViewModel  _moduleDetailVm;
    private readonly ProfileDetailViewModel _profileDetailVm;

    [ObservableProperty]
    private object _currentPage;

    public MainViewModel(
        BasicParamsViewModel   basicParamsVm,
        ModuleEditViewModel    moduleEditVm,
        HostListViewModel      hostListVm,
        HostDetailViewModel    hostDetailVm,
        ModuleDetailViewModel  moduleDetailVm,
        ProfileDetailViewModel profileDetailVm)
    {
        _basicParamsVm   = basicParamsVm;
        _moduleEditVm    = moduleEditVm;
        _hostListVm      = hostListVm;
        _hostDetailVm    = hostDetailVm;
        _moduleDetailVm  = moduleDetailVm;
        _profileDetailVm = profileDetailVm;

        _currentPage = _basicParamsVm;

        // ── 詳細画面への遷移 ──────────────────────────────────────
        WeakReferenceMessenger.Default.Register<ShowHostDetailMessage>(this, (_, msg) =>
        {
            _hostDetailVm.Load(msg.Value);
            CurrentPage = _hostDetailVm;
        });

        WeakReferenceMessenger.Default.Register<ShowModuleDetailMessage>(this, (_, msg) =>
        {
            _moduleDetailVm.Load(msg.Value);
            CurrentPage = _moduleDetailVm;
        });

        WeakReferenceMessenger.Default.Register<ShowProfileDetailMessage>(this, (_, msg) =>
        {
            _profileDetailVm.Load(msg.Value);
            CurrentPage = _profileDetailVm;
        });

        // ── 一覧画面への戻り ─────────────────────────────────────
        WeakReferenceMessenger.Default.Register<NavigateBackMessage>(this, (_, msg) =>
        {
            CurrentPage = msg.Value switch
            {
                "HostList"    => (object)_hostListVm,
                "ModuleEdit"  => _moduleEditVm,
                "BasicParams" => _basicParamsVm,
                _             => _basicParamsVm
            };
        });
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
