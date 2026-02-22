using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// ナビゲーション管理 — CurrentPage を切り替えることで右ペインの表示を制御する。
/// WeakReferenceMessenger でサブページ間の遷移メッセージを受け取る。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly BasicParamsViewModel          _basicParamsVm;
    private readonly ModuleEditViewModel           _moduleEditVm;
    private readonly HostListViewModel             _hostListVm;
    private readonly HostDetailViewModel           _hostDetailVm;
    private readonly ModuleDetailViewModel         _moduleDetailVm;
    private readonly ProfileDetailViewModel        _profileDetailVm;
    private readonly AutokeyRecipeEditorViewModel  _autokeyEditorVm;
    private readonly WelcomeViewModel              _welcomeVm;
    private readonly IWorkspaceService             _workspace;

    [ObservableProperty]
    private object _currentPage;

    /// <summary>ワークスペースが開かれているか。ナビゲーションボタンの IsEnabled にバインドする。</summary>
    [ObservableProperty] private bool   _isWorkspaceOpen;

    /// <summary>現在のワークスペースのフォルダ名（表示用）。</summary>
    [ObservableProperty] private string _workspaceName = "";

    public MainViewModel(
        BasicParamsViewModel          basicParamsVm,
        ModuleEditViewModel           moduleEditVm,
        HostListViewModel             hostListVm,
        HostDetailViewModel           hostDetailVm,
        ModuleDetailViewModel         moduleDetailVm,
        ProfileDetailViewModel        profileDetailVm,
        AutokeyRecipeEditorViewModel  autokeyEditorVm,
        WelcomeViewModel              welcomeVm,
        IWorkspaceService             workspace)
    {
        _basicParamsVm   = basicParamsVm;
        _moduleEditVm    = moduleEditVm;
        _hostListVm      = hostListVm;
        _hostDetailVm    = hostDetailVm;
        _moduleDetailVm  = moduleDetailVm;
        _profileDetailVm = profileDetailVm;
        _autokeyEditorVm = autokeyEditorVm;
        _welcomeVm       = welcomeVm;
        _workspace       = workspace;

        // ── 初期表示: ワークスペースが開いていればメイン画面、未設定なら WelcomeView ──
        IsWorkspaceOpen = workspace.IsOpen;
        WorkspaceName   = GetDisplayName(workspace.RootPath);
        _currentPage    = workspace.IsOpen ? (object)_basicParamsVm : _welcomeVm;

        // ── ワークスペース変更通知: 常にメイン画面（BasicParams）へ遷移 ────────
        workspace.WorkspaceChanged += (_, e) =>
        {
            IsWorkspaceOpen = true;
            WorkspaceName   = GetDisplayName(e.NewPath);
            CurrentPage     = _basicParamsVm;
        };

        // ── 詳細画面への遷移 ──────────────────────────────────────────────
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

        // ── 一覧画面への戻り ─────────────────────────────────────────────
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
            "BasicParams"   => (object)_basicParamsVm,
            "ModuleEdit"    => _moduleEditVm,
            "HostList"      => _hostListVm,
            "AutokeyEditor" => _autokeyEditorVm,
            _               => CurrentPage
        };
    }

    /// <summary>
    /// ワークスペースを変更する。フォルダ選択ダイアログを開き、
    /// 選択されたパスで IWorkspaceService.Open() を呼び出す。
    /// バリデーション失敗時はメッセージボックスで通知する。
    /// </summary>
    [RelayCommand]
    private void ChangeWorkspace()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "fabriq ルートフォルダを選択してください"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _workspace.Open(dialog.FolderName);
            // 成功時は WorkspaceChanged イベントが発火し、CurrentPage が自動更新される
        }
        catch (ArgumentException ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "ワークスペースエラー",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>パスの末尾フォルダ名だけを取り出す（表示用）。</summary>
    private static string GetDisplayName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return Path.GetFileName(path.TrimEnd('\\', '/')) ?? path;
    }
}
