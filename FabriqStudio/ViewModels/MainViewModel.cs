using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Services;
using FabriqStudio.Views;

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

        // ── ワークスペース変更通知 ─────────────────────────────────────────────
        workspace.WorkspaceChanged += (_, e) =>
        {
            if (e.NewPath is null)
            {
                // Close() 呼び出し時: WelcomeView へ戻る
                IsWorkspaceOpen = false;
                WorkspaceName   = "";
                CurrentPage     = _welcomeVm;
            }
            else
            {
                // Open() 呼び出し時: メイン画面へ遷移
                IsWorkspaceOpen = true;
                WorkspaceName   = GetDisplayName(e.NewPath);
                CurrentPage     = _basicParamsVm;
            }
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
    /// 現在のワークスペースを閉じて WelcomeView へ戻る。
    /// WorkspaceChanged（NewPath = null）が発火し、UI が自動更新される。
    /// </summary>
    [RelayCommand]
    private void CloseWorkspace() => _workspace.Close();

    /// <summary>
    /// テンプレートから新規ワークスペースを作成する。
    /// 1. 作成先フォルダを選択
    /// 2. 新しいフォルダ名を入力
    /// 3. 重複チェック後テンプレートをコピー
    /// 4. 作成したフォルダをワークスペースとして開く
    /// </summary>
    [RelayCommand]
    private async Task CreateNewWorkspaceAsync()
    {
        // 1. 作成先の親フォルダを選択
        var parentDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "新規ワークスペースの作成先フォルダを選択してください"
        };
        if (parentDialog.ShowDialog() != true) return;

        // 2. 新しいフォルダ名を入力
        var folderName = NewWorkspaceDialog.Show(parentDialog.FolderName);
        if (string.IsNullOrWhiteSpace(folderName)) return;

        var targetPath = Path.Combine(parentDialog.FolderName, folderName);

        // 3. 既存チェック（安全対策: 同名フォルダが既にある場合は中断）
        if (Directory.Exists(targetPath))
        {
            MessageBox.Show(
                $"フォルダ「{folderName}」は既に存在します。\n別のフォルダ名を指定してください。",
                "作成エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // 4. テンプレートをコピー
        var error = await _workspace.CreateFromTemplateAsync(targetPath);
        if (error is not null)
        {
            MessageBox.Show(error, "作成エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 5. 作成したワークスペースを開く（WorkspaceChanged が発火してUIが自動更新される）
        try
        {
            _workspace.Open(targetPath);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "ワークスペースエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>パスの末尾フォルダ名だけを取り出す（表示用）。</summary>
    private static string GetDisplayName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return Path.GetFileName(path.TrimEnd('\\', '/')) ?? path;
    }
}
