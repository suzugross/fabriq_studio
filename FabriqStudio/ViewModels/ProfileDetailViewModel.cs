using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Views;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 特殊コマンドのマスター定義。
/// Command: プロファイル CSV に書き込む ScriptPath 値
/// Description: 概要説明（プロファイル CSV の Description カラムに自動セット）
/// </summary>
public record SpecialCommandDef(string Command, string Description)
{
    // ComboBox の DisplayMemberPath="Command" を使うため ToString は不要だが
    // デバッグ時の可読性のため残す。
    public override string ToString() => $"{Command}  ({Description})";
}

/// <summary>
/// プロファイル編集画面
///   - 左ペイン: 利用可能モジュール一覧（フィルター付き ListBox）
///   - 右ペイン: プロファイル構成 DataGrid（追加/削除/上下移動/特殊コマンド）
///
/// ロック機構:
///   IsLocked=true（初期値）→ DataGrid 読み取り専用、全操作ボタン無効
///   IsLocked=false          → 編集可能
///
/// 保存:
///   CanExecute = IsDirty &amp;&amp; !IsLocked
///   保存時に表示順を Order (10, 20, ...) で振り直してから CSV に書き込む
///
/// Dirty 検知バイパス:
///   _isInitializing=true の間は CollectionChanged / PropertyChanged
///   による IsDirty の自動セットを抑制する。
///   LoadAsync の全処理と SaveAsync の保存呼び出しをこのフラグでガードし、
///   完了後に必ず IsDirty = false にリセットする。
/// </summary>
public partial class ProfileDetailViewModel : ObservableObject
{
    private readonly IModuleService  _moduleService;
    private readonly IProfileService _profileService;

    /// <summary>ロード中／保存中は true にして Dirty 検知をバイパスする。</summary>
    private bool _isInitializing;

    // ─── 対象プロファイル ─────────────────────────────────────────
    [ObservableProperty] private ProfileEntry? _profile;

    // ─── 左ペイン: 利用可能モジュール ────────────────────────────
    [ObservableProperty] private ObservableCollection<ModuleMasterEntry> _availableModules = [];
    [ObservableProperty] private ObservableCollection<ModuleMasterEntry> _filteredModules  = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddModuleCommand))]
    private ModuleMasterEntry? _selectedAvailable;

    [ObservableProperty] private string _filterText = "";

    // ─── 右ペイン: プロファイル構成 ──────────────────────────────
    [ObservableProperty] private ObservableCollection<ProfileScriptEntry> _modules = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveModuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private ProfileScriptEntry? _selectedModule;

    // ─── 特殊コマンド ─────────────────────────────────────────────
    /// <summary>ドロップダウンに表示する特殊コマンドのマスター定義（fabriq 仕様準拠）</summary>
    public IReadOnlyList<SpecialCommandDef> SpecialCommands { get; } =
    [
        new("__AUTOPILOT__",  "WaitSec=3"),
        new("__RESTART__",    "Restart"),
        new("__REEXPLORER__", "Restart Explorer"),
        new("__STOPLOG__",    "Stop Transcript"),
        new("__STARTLOG__",   "Start Transcript"),
        new("__SHUTDOWN__",   "Shutdown"),
        new("__PAUSE__",      "Pause (Enter wait)"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSpecialCommandCommand))]
    private SpecialCommandDef? _selectedSpecialCommand;

    // ─── ロック ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddModuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveModuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSpecialCommandCommand))]
    private bool _isLocked = true;

    // ─── 状態 ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private string? _errorMessage;

    public ProfileDetailViewModel(IModuleService moduleService, IProfileService profileService)
    {
        _moduleService  = moduleService;
        _profileService = profileService;
    }

    /// <summary>BasicParams 画面から遷移してきたときに呼び出す。</summary>
    public void Load(ProfileEntry profile)
    {
        Profile    = profile;
        IsLocked   = true;
        SaveStatus = null;
        SaveError  = null;
        _ = LoadAsync(profile);
    }

    private async Task LoadAsync(ProfileEntry profile)
    {
        // ─── 初期化開始: Dirty 検知をバイパス ────────────────────
        _isInitializing = true;
        IsLoading       = true;
        ErrorMessage    = null;

        // 旧コレクションの Clear() が Dirty を発火しないようフラグを立てた後に実行
        Modules.Clear();
        AvailableModules.Clear();
        FilteredModules.Clear();
        FilterText = "";

        try
        {
            // 左右ペインのデータを並列ロード
            var allModulesTask  = _moduleService.GetAllModulesAsync();
            var profileModsTask = _profileService.GetProfileModulesAsync(profile);
            await Task.WhenAll(allModulesTask, profileModsTask);

            // 右ペイン: プロファイル構成
            var entries = new ObservableCollection<ProfileScriptEntry>(profileModsTask.Result);
            SubscribeDirty(entries);
            Modules = entries;

            // 左ペイン: 利用可能モジュール（全種別）
            AvailableModules = new ObservableCollection<ModuleMasterEntry>(allModulesTask.Result);
            ApplyFilter();

            // ロード完了後に必ずクリーン状態にリセット
            IsDirty = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsLoading       = false;
            _isInitializing = false;  // ─── 初期化終了: Dirty 検知を再開
        }
    }

    // ─── Dirty 検知 ──────────────────────────────────────────────
    private void SubscribeDirty(ObservableCollection<ProfileScriptEntry> collection)
    {
        foreach (var item in collection)
            item.PropertyChanged += OnModuleItemChanged;

        collection.CollectionChanged += (_, e) =>
        {
            // 初期化中（ロード・保存の Order 書き換え）は無視する
            if (!_isInitializing) IsDirty = true;

            if (e.NewItems is not null)
                foreach (ProfileScriptEntry m in e.NewItems)
                    m.PropertyChanged += OnModuleItemChanged;
        };
    }

    private void OnModuleItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 初期化中（ロード・保存の Order 書き換え）は無視する
        if (!_isInitializing) IsDirty = true;
    }

    // ─── フィルター ───────────────────────────────────────────────
    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            FilteredModules = new ObservableCollection<ModuleMasterEntry>(AvailableModules);
        }
        else
        {
            var f = FilterText.Trim();
            FilteredModules = new ObservableCollection<ModuleMasterEntry>(
                AvailableModules.Where(m =>
                    m.MenuName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    m.Category.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    m.ModuleDir.Contains(f, StringComparison.OrdinalIgnoreCase)));
        }
    }

    // ─── モジュール追加（左 → 右）────────────────────────────────
    private bool CanAddModule() => SelectedAvailable is not null && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanAddModule))]
    private void AddModule()
    {
        if (SelectedAvailable is null) return;

        var nextOrder  = Modules.Count > 0 ? Modules.Max(m => m.Order) + 10 : 10;
        // ScriptPath は "kind/moduleDir/script" 形式で構築
        var scriptPath = $"{SelectedAvailable.Kind}/{SelectedAvailable.ModuleDir}/{SelectedAvailable.Script}";

        Modules.Add(new ProfileScriptEntry
        {
            Order       = nextOrder,
            ScriptPath  = scriptPath,
            Enabled     = "1",
            Description = SelectedAvailable.MenuName
        });
    }

    // ─── モジュール削除（右から削除）──────────────────────────────
    private bool CanRemoveModule() => SelectedModule is not null && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanRemoveModule))]
    private void RemoveModule()
    {
        if (SelectedModule is null) return;
        Modules.Remove(SelectedModule);
        SelectedModule = null;
    }

    // ─── 上に移動 ────────────────────────────────────────────────
    private bool CanMoveUp()
        => SelectedModule is not null && Modules.IndexOf(SelectedModule) > 0 && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (SelectedModule is null) return;
        var idx = Modules.IndexOf(SelectedModule);
        if (idx <= 0) return;
        Modules.Move(idx, idx - 1);
        IsDirty = true;
        // 移動後は SelectedModule が変わらないため CanExecute を手動再評価
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ─── 下に移動 ────────────────────────────────────────────────
    private bool CanMoveDown()
        => SelectedModule is not null && Modules.IndexOf(SelectedModule) < Modules.Count - 1 && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (SelectedModule is null) return;
        var idx = Modules.IndexOf(SelectedModule);
        if (idx >= Modules.Count - 1) return;
        Modules.Move(idx, idx + 1);
        IsDirty = true;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ─── 特殊コマンド追加 ─────────────────────────────────────────
    private bool CanAddSpecialCommand() => SelectedSpecialCommand is not null && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanAddSpecialCommand))]
    private void AddSpecialCommand()
    {
        if (SelectedSpecialCommand is null) return;
        var nextOrder = Modules.Count > 0 ? Modules.Max(m => m.Order) + 10 : 10;
        Modules.Add(new ProfileScriptEntry
        {
            Order       = nextOrder,
            ScriptPath  = SelectedSpecialCommand.Command,
            Enabled     = "1",
            Description = SelectedSpecialCommand.Description
        });
    }

    // ─── 保存 ────────────────────────────────────────────────────
    private bool CanSave() => IsDirty && !IsLocked && Profile is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (Profile is null) return;
        SaveError  = null;
        SaveStatus = null;
        try
        {
            // SaveProfileModulesAsync 内で ProfileScriptEntry.Order を書き換えるため、
            // その PropertyChanged が Dirty を再トリガーしないようフラグを立てる。
            _isInitializing = true;
            await _profileService.SaveProfileModulesAsync(Profile, Modules);
            IsDirty    = false;
            SaveStatus = "✓ 保存しました";
            WeakReferenceMessenger.Default.Send(new WorkspaceDataUpdatedMessage("ProfileDetail"));
        }
        catch (Exception ex)
        {
            SaveError = $"保存エラー: {ex.Message}";
        }
        finally
        {
            _isInitializing = false;
        }
    }

    // ─── モジュール設定を開く ────────────────────────────────────
    [RelayCommand]
    private void OpenModuleSettings(ProfileScriptEntry? entry)
    {
        if (entry is null || entry.IsSystemCommand) return;

        var moduleDir = ExtractModuleDirectory(entry.ScriptPath);
        if (moduleDir is null) return;

        var module = AvailableModules.FirstOrDefault(m =>
            string.Equals(m.ModuleDir, moduleDir, StringComparison.OrdinalIgnoreCase));

        if (module is null)
        {
            MessageBox.Show(
                $"モジュール '{moduleDir}' が見つかりません。\n" +
                "プロファイルの情報が古いか、モジュールが削除された可能性があります。",
                "モジュール設定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ModuleSettingsDialog.Show(module, Application.Current.MainWindow);
    }

    /// <summary>
    /// ScriptPath ("kind/moduleDir/script.ps1" 等) からモジュールディレクトリ名を抽出する。
    /// フォーマット混在（"/" / "\" / "modules\" プレフィクス付き）に対応。
    /// </summary>
    private static string? ExtractModuleDirectory(string scriptPath)
    {
        var parts = scriptPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : null;
    }

    // ─── 戻る ────────────────────────────────────────────────────
    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("BasicParams"));
}
