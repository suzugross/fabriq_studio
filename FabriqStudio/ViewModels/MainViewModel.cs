using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Helpers;
using FabriqStudio.Messages;
using FabriqStudio.Services;
using System.Collections.ObjectModel;
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
    private readonly AppConfigViewModel            _appConfigVm;
    private readonly ProfileDetailViewModel        _profileDetailVm;
    private readonly LooperEditorViewModel            _looperEditorVm;
    private readonly WelcomeViewModel                 _welcomeVm;
    private readonly RegistryCollectionViewModel      _registryCollectionVm;
    private readonly PrinterDriverDetectorViewModel   _printerDriverDetectorVm;
    private readonly PianistProfileEditorViewModel    _pianistEditorVm;
    private readonly MasterParamViewModel             _masterParamVm;
    private readonly GpoCollectionViewModel           _gpoCollectionVm;
    private readonly CollectViewModel                 _collectVm;
    private readonly IWorkspaceService                _workspace;
    private readonly ICryptoService                   _crypto;
    private readonly IPassphraseService               _passphrase;
    private readonly IDataSetContext                  _dataSet;
    private readonly IProfileService                  _profiles;

    [ObservableProperty]
    private object _currentPage;

    /// <summary>ワークスペースが開かれているか。ナビゲーションボタンの IsEnabled にバインドする。</summary>
    [ObservableProperty] private bool   _isWorkspaceOpen;

    /// <summary>現在のワークスペースのフォルダ名（表示用）。</summary>
    [ObservableProperty] private string _workspaceName = "";

    /// <summary>パスフレーズ設定状態の表示テキスト。</summary>
    [ObservableProperty] private string _passphraseStatus = "未設定";

    /// <summary>
    /// サイドバーの Advanced グループ（モジュール本体の直接編集・Script Looper）を開いているか。
    /// 日常のキッティング作業では使わない画面なので既定は畳む。
    /// </summary>
    [ObservableProperty] private bool _isAdvancedExpanded;

    // ─── 編集先データセット（PDF）─────────────────────────────────
    // プロファイルに紐づかない画面（GPO 辞書 / レジストリ辞書の書き出し、プリンタ検出、Pianist）の書き先。
    // 「（本体）」= modules/ 配下、プロファイル名 = profiles/<名>/modules/ 配下。
    // 基本パラメータの「実行プロファイル」と連動する（状態は IDataSetContext が唯一持ち、このセレクタはその鏡）。
    // 切り替えは IDataSetContext.Changed で依存画面（IDataSetDependentViewModel）だけが再読込する。
    // ワークスペース全体は再読込しないので、基本パラメータ等の編集中の内容は失われない。

    /// <summary>「本体」を表す選択肢。</summary>
    public const string BodyChoice = "（本体）";

    public ObservableCollection<string> DataSetChoices { get; } = [BodyChoice];

    [ObservableProperty] private string? _selectedDataSetChoice = BodyChoice;

    private bool _suppressDataSetChange;

    public MainViewModel(
        BasicParamsViewModel              basicParamsVm,
        ModuleEditViewModel               moduleEditVm,
        HostListViewModel                 hostListVm,
        HostDetailViewModel               hostDetailVm,
        ModuleDetailViewModel             moduleDetailVm,
        AppConfigViewModel                appConfigVm,
        ProfileDetailViewModel            profileDetailVm,
        LooperEditorViewModel             looperEditorVm,
        WelcomeViewModel                  welcomeVm,
        RegistryCollectionViewModel       registryCollectionVm,
        PrinterDriverDetectorViewModel    printerDriverDetectorVm,
        PianistProfileEditorViewModel     pianistEditorVm,
        MasterParamViewModel              masterParamVm,
        GpoCollectionViewModel            gpoCollectionVm,
        CollectViewModel                  collectVm,
        IWorkspaceService                 workspace,
        ICryptoService                    crypto,
        IPassphraseService                passphrase,
        IDataSetContext                   dataSet,
        IProfileService                   profiles)
    {
        _dataSet                 = dataSet;
        _profiles                = profiles;
        _masterParamVm           = masterParamVm;
        _gpoCollectionVm         = gpoCollectionVm;
        _collectVm               = collectVm;
        _basicParamsVm           = basicParamsVm;
        _moduleEditVm            = moduleEditVm;
        _hostListVm              = hostListVm;
        _hostDetailVm            = hostDetailVm;
        _moduleDetailVm          = moduleDetailVm;
        _appConfigVm             = appConfigVm;
        _profileDetailVm         = profileDetailVm;
        _looperEditorVm          = looperEditorVm;
        _welcomeVm               = welcomeVm;
        _registryCollectionVm    = registryCollectionVm;
        _printerDriverDetectorVm = printerDriverDetectorVm;
        _pianistEditorVm         = pianistEditorVm;
        _workspace               = workspace;
        _crypto                  = crypto;
        _passphrase              = passphrase;

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
                ClearSessionPassphrase();   // パスフレーズはワークスペースに属するので持ち越さない
            }
            else if (e.NewPath == e.OldPath)
            {
                // Reload() 呼び出し時: CurrentPage はそのまま維持
                // （各 VM が WorkspaceChanged を受けてデータを自動再ロードする）
            }
            else
            {
                // Open() 呼び出し時: メイン画面（BasicParams）へ遷移
                IsWorkspaceOpen = true;
                WorkspaceName   = GetDisplayName(e.NewPath);
                CurrentPage     = _basicParamsVm;
                QueuePassphrasePrompt();    // 開いた直後に確認（Reload では出さない）
            }
        };

        // ── 編集先データセット: 候補（profiles/*.csv）の更新。別ワークスペースでは本体に戻す（DataSetContext も戻る）──
        workspace.WorkspaceChanged += (_, e) =>
        {
            if (e.NewPath != e.OldPath) SetDataSetChoiceSilently(BodyChoice);
            _ = RefreshDataSetChoicesAsync();
        };
        WeakReferenceMessenger.Default.Register<WorkspaceDataUpdatedMessage>(this, (_, _) => _ = RefreshDataSetChoicesAsync());
        dataSet.Changed += (_, _) => MirrorDataSetChoice();   // 基本パラメータの「実行プロファイル」からの連動もここで映る
        _ = RefreshDataSetChoicesAsync();

        // ── 詳細画面への遷移 ──────────────────────────────────────────────
        WeakReferenceMessenger.Default.Register<ShowHostDetailMessage>(this, (_, msg) =>
        {
            if (!ConfirmDiscardIfDirty()) return;
            _hostDetailVm.Load(msg.Value);
            CurrentPage = _hostDetailVm;
        });

        WeakReferenceMessenger.Default.Register<ShowModuleDetailMessage>(this, (_, msg) =>
        {
            if (!ConfirmDiscardIfDirty()) return;
            var dir = msg.Value.ModuleDir ?? "";
            var dirName = Path.GetFileName(dir.TrimEnd('\\', '/'));
            if (dirName.Equals("app_config", StringComparison.OrdinalIgnoreCase))
            {
                _appConfigVm.Load(msg.Value);
                CurrentPage = _appConfigVm;
            }
            else
            {
                _moduleDetailVm.Load(msg.Value);
                CurrentPage = _moduleDetailVm;
            }
        });

        WeakReferenceMessenger.Default.Register<ShowProfileDetailMessage>(this, (_, msg) =>
        {
            if (!ConfirmDiscardIfDirty()) return;
            _profileDetailVm.Load(msg.Value);
            CurrentPage = _profileDetailVm;
        });

        // ── 一覧画面への戻り ─────────────────────────────────────────────
        WeakReferenceMessenger.Default.Register<NavigateBackMessage>(this, (_, msg) =>
        {
            // モーダルダイアログ表示中（ModuleSettingsDialog 等）は MainWindow のナビゲーションを行わない。
            // ダイアログ側が同じ NavigateBackMessage を独自にハンドルしてダイアログを閉じる責務を持つ。
            if (Application.Current.Windows.Count > 1) return;

            if (!ConfirmDiscardIfDirty()) return;
            CurrentPage = msg.Value switch
            {
                "HostList"    => (object)_hostListVm,
                "ModuleEdit"  => _moduleEditVm,
                "BasicParams" => _basicParamsVm,
                _             => _basicParamsVm
            };
        });
    }

    /// <summary>
    /// 現在のページに未保存の変更があるか確認し、ある場合はユーザーに破棄確認を求める。
    /// 画面遷移／ワークスペース切替／アプリ終了など、編集が失われ得るすべての操作の前に呼び出す。
    /// 文言と DiscardChanges 呼び出しの責務は <see cref="DirtyConfirmHelper"/> に集約。
    /// </summary>
    /// <returns>遷移してよい場合 true、ユーザーがキャンセルした場合 false。</returns>
    public bool ConfirmDiscardIfDirty()
        => DirtyConfirmHelper.ConfirmDiscard(CurrentPage as IDirtyAwareViewModel);

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedExpanded = !IsAdvancedExpanded;

    [RelayCommand]
    private void Navigate(string? page)
    {
        if (!ConfirmDiscardIfDirty()) return;
        CurrentPage = page switch
        {
            "BasicParams"            => (object)_basicParamsVm,
            "ModuleEdit"             => _moduleEditVm,
            "HostList"               => _hostListVm,
            "LooperEditor"           => _looperEditorVm,
            "RegistryCollection"     => _registryCollectionVm,
            "PrinterDriverDetector"  => _printerDriverDetectorVm,
            "PianistProfile"         => _pianistEditorVm,
            "MasterParam"            => _masterParamVm,
            "GpoCollection"          => _gpoCollectionVm,
            "Collect"                => _collectVm,
            _                        => CurrentPage
        };
    }

    // ─── 編集先データセット ─────────────────────────────────────

    private async Task RefreshDataSetChoicesAsync()
    {
        if (!_workspace.IsOpen)
        {
            ResetChoices([]);
            return;
        }
        try
        {
            var names = (await _profiles.GetProfilesAsync())
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ResetChoices(names);
        }
        catch
        {
            // 一覧が取れなくても「本体」は選べる
        }
    }

    private void ResetChoices(IReadOnlyList<string> names)
    {
        var keep = _dataSet.Current;
        _suppressDataSetChange = true;
        try
        {
            DataSetChoices.Clear();
            DataSetChoices.Add(BodyChoice);
            foreach (var n in names) DataSetChoices.Add(n);
            if (keep is not null && !DataSetChoices.Contains(keep)) DataSetChoices.Add(keep);   // PDF だけあるプロファイル等
            SelectedDataSetChoice = keep ?? BodyChoice;
        }
        finally
        {
            _suppressDataSetChange = false;
        }
    }

    private void SetDataSetChoiceSilently(string choice)
    {
        _suppressDataSetChange = true;
        try { SelectedDataSetChoice = choice; }
        finally { _suppressDataSetChange = false; }
    }

    partial void OnSelectedDataSetChoiceChanged(string? oldValue, string? newValue)
    {
        if (_suppressDataSetChange) return;
        var target = string.IsNullOrEmpty(newValue) || newValue == BodyChoice ? null : newValue;
        if (target == _dataSet.Current) return;

        // 切替で再読込されるのは編集先に依存する画面（IDataSetDependentViewModel）だけ。
        // その画面を表示中で未保存があれば先に確認し、他の画面（基本パラメータ等）の編集はそのまま残す
        if (CurrentPage is IDataSetDependentViewModel && !ConfirmDiscardIfDirty())
        {
            SetDataSetChoiceSilently(oldValue ?? BodyChoice);
            return;
        }
        _dataSet.Set(target);   // Changed を受けて依存画面が自分で再読込し、セレクタは MirrorDataSetChoice で追従する
    }

    /// <summary>
    /// 編集先コンテキストの変化をセレクタへ映す（基本パラメータの「実行プロファイル」からの連動、
    /// 別ワークスペースでの本体戻し）。候補に無い名前（PDF だけのデータセット等）は候補に足してから選ぶ。
    /// </summary>
    private void MirrorDataSetChoice()
    {
        var choice = _dataSet.Current ?? BodyChoice;
        if (!DataSetChoices.Contains(choice)) DataSetChoices.Add(choice);
        SetDataSetChoiceSilently(choice);
    }

    // ─── パスフレーズ ─────────────────────────────────────────
    // セッションのパスフレーズはワークスペースに属する（IPassphraseService の規約）。
    // 起動時とワークスペースを開いた直後に一度だけ確認する。強制はせず、キャンセルすれば
    // パスフレーズなしで編集を続行できる（＝合致判定なし）。忘却を防ぐための確認である。

    /// <summary>起動時プロンプトは 1 回だけ（Loaded が複数回発火しても二重に出さない）。</summary>
    private bool _startupPromptDone;

    /// <summary>プロンプト表示中フラグ（モーダル中の再入防止）。</summary>
    private bool _passphrasePromptOpen;

    /// <summary>
    /// MainWindow の Loaded から呼ぶ。永続化復元で開かれたワークスペースは
    /// WorkspaceChanged が発火しないため、この経路で確認プロンプトを出す。
    /// </summary>
    public void PromptPassphraseOnStartup()
    {
        if (_startupPromptDone) return;
        _startupPromptDone = true;
        QueuePassphrasePrompt();
    }

    /// <summary>
    /// 確認プロンプトを次の Dispatcher サイクルへ回す。
    /// WorkspaceChanged ハンドラの途中でモーダルを出すと、後続ハンドラ（各 VM の再読込）が
    /// ダイアログを閉じるまで走らないため、必ず遅延させる。
    /// </summary>
    private void QueuePassphrasePrompt()
        => Application.Current?.Dispatcher.BeginInvoke(
               new Action(PromptPassphraseForWorkspace), DispatcherPriority.Background);

    /// <summary>
    /// 現在のワークスペースについてパスフレーズを確認する。
    /// 設定済み（検証トークンあり）→ 照合プロンプト。不一致なら再入力を促す。
    /// 未設定 → 設定プロンプト。どちらもキャンセル可（パスフレーズなしで続行）。
    /// </summary>
    private void PromptPassphraseForWorkspace()
    {
        if (_passphrasePromptOpen || !_workspace.IsOpen) return;

        _passphrasePromptOpen = true;
        try
        {
            if (_passphrase.IsConfiguredInWorkspace)
            {
                // 既にこのワークスペースのパスフレーズを保持しているなら聞かない
                if (_crypto.MasterPassphrase is { } current && _passphrase.Verify(current)) return;

                ClearSessionPassphrase();   // 別ワークスペースのパスフレーズは持ち越さない

                while (true)
                {
                    var input = PassphraseDialog.Show(PassphraseDialogMode.WorkspaceVerify);
                    if (string.IsNullOrEmpty(input)) return;   // キャンセル / 空 = スキップ
                    if (_passphrase.Verify(input)) { ApplyPassphrase(input); return; }

                    MessageBox.Show(
                        "パスフレーズが正しくありません。\nもう一度入力するか、キャンセルしてください。",
                        "パスフレーズの確認",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            ClearSessionPassphrase();

            var created = PassphraseDialog.Show(PassphraseDialogMode.WorkspaceSetup);
            if (string.IsNullOrEmpty(created)) return;   // キャンセル / 空 = スキップ
            ApplyPassphrase(created);
        }
        finally
        {
            _passphrasePromptOpen = false;
        }
    }

    /// <summary>左ペイン下部「🔑 パスフレーズ」からの手動設定。</summary>
    [RelayCommand]
    private void SetPassphrase()
    {
        var result = PassphraseDialog.Show(PassphraseDialogMode.Manual, _crypto.HasPassphrase);
        if (result is null) return;   // キャンセル

        if (string.IsNullOrEmpty(result))
        {
            ClearSessionPassphrase();   // セッションのみ解除（検証トークンは残す）
            return;
        }

        ApplyPassphrase(result);
    }

    /// <summary>パスフレーズを適用し、失敗（不一致・トークン書き出し不能）はそのまま表示する。</summary>
    private void ApplyPassphrase(string passphrase)
    {
        var error = _passphrase.Apply(passphrase);
        if (error is not null)
            MessageBox.Show(error, "パスフレーズ", MessageBoxButton.OK, MessageBoxImage.Warning);

        RefreshPassphraseStatus();
    }

    /// <summary>セッションのパスフレーズを解除する（検証トークンには触らない）。</summary>
    private void ClearSessionPassphrase()
    {
        _passphrase.ClearSession();
        RefreshPassphraseStatus();
    }

    private void RefreshPassphraseStatus()
        => PassphraseStatus = _crypto.HasPassphrase ? "設定済み" : "未設定";

    /// <summary>
    /// 現在のワークスペースを閉じて WelcomeView へ戻る。
    /// WorkspaceChanged（NewPath = null）が発火し、UI が自動更新される。
    /// </summary>
    [RelayCommand]
    private void CloseWorkspace()
    {
        if (!ConfirmDiscardIfDirty()) return;
        _workspace.Close();
    }

    /// <summary>
    /// 現在のワークスペースのデータを最新の情報に更新する。
    /// 確認ダイアログで OK を選んだ場合のみ Reload() を呼び出す。
    /// </summary>
    [RelayCommand]
    private void ReloadWorkspace()
    {
        var result = MessageBox.Show(
            "最新の情報に更新しますか？\n未保存の編集内容はすべて破棄されます。",
            "最新の情報に更新",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.OK)
            _workspace.Reload();
    }

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
        // 0. 現在のページに未保存の変更があれば破棄確認
        if (!ConfirmDiscardIfDirty()) return;

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
