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
/// 左ペイン「利用可能モジュール」の表示用 VM エントリ。
/// 通常モジュール（<see cref="Module"/>）または特殊コマンド（<see cref="Special"/>）のいずれかを保持する
/// 判別ユニオン相当で、単一コレクションでフィルター・選択・追加を扱えるようにする。
/// </summary>
public sealed class AvailableItem
{
    public string             DisplayName { get; }
    public string             KindLabel   { get; }
    public string             Category    { get; }
    public string             SubText     { get; }
    public ModuleMasterEntry? Module      { get; }
    public SpecialCommandDef? Special     { get; }

    public bool IsSpecial => Special is not null;

    private AvailableItem(
        string displayName, string kindLabel, string category, string subText,
        ModuleMasterEntry? module, SpecialCommandDef? special)
    {
        DisplayName = displayName;
        KindLabel   = kindLabel;
        Category    = category;
        SubText     = subText;
        Module      = module;
        Special     = special;
    }

    public static AvailableItem FromModule(ModuleMasterEntry m)
        => new(m.MenuName, m.Kind, m.Category, m.ModuleDir, m, null);

    public static AvailableItem FromSpecial(SpecialCommandDef cmd)
        => new(cmd.Command, "特殊", "特殊コマンド", cmd.Description, null, cmd);
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

    // ─── 左ペイン: 利用可能モジュール（通常モジュール + 特殊コマンド） ────────
    [ObservableProperty] private ObservableCollection<AvailableItem> _availableModules = [];
    [ObservableProperty] private ObservableCollection<AvailableItem> _filteredModules  = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddModuleCommand))]
    private AvailableItem? _selectedAvailable;

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
        new("__AUTOPILOT__",  "AutoPilot (Description=WaitSec=N)"),
        new("__ASYNC__",      "Async runspace (since kernel 2.1.0)"),
        new("__RESTART__",    "Restart"),
        new("__REEXPLORER__", "Restart Explorer"),
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
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
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

            // 左ペイン: 特殊コマンドを先頭に、続いて全モジュール種別を表示
            var items = new List<AvailableItem>();
            items.AddRange(SpecialCommands.Select(AvailableItem.FromSpecial));
            items.AddRange(allModulesTask.Result.Select(AvailableItem.FromModule));
            AvailableModules = new ObservableCollection<AvailableItem>(items);
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
            FilteredModules = new ObservableCollection<AvailableItem>(AvailableModules);
        }
        else
        {
            var f = FilterText.Trim();
            FilteredModules = new ObservableCollection<AvailableItem>(
                AvailableModules.Where(i =>
                    i.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    i.KindLabel.Contains(f,   StringComparison.OrdinalIgnoreCase) ||
                    i.Category.Contains(f,    StringComparison.OrdinalIgnoreCase) ||
                    i.SubText.Contains(f,     StringComparison.OrdinalIgnoreCase)));
        }
    }

    // ─── モジュール追加（左 → 右）────────────────────────────────
    private bool CanAddModule() => SelectedAvailable is not null && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanAddModule))]
    private void AddModule()
    {
        if (SelectedAvailable is null) return;

        var nextOrder = Modules.Count > 0 ? Modules.Max(m => m.Order) + 10 : 10;

        string scriptPath;
        string description;
        if (SelectedAvailable.Special is { } sp)
        {
            // 特殊コマンド: ScriptPath にマーカーそのものを、Description にマスター説明を転記
            scriptPath  = sp.Command;
            description = sp.Description;
        }
        else if (SelectedAvailable.Module is { } m)
        {
            // 通常モジュール: ScriptPath は "kind/moduleDir/script" 形式で構築
            scriptPath  = $"{m.Kind}/{m.ModuleDir}/{m.Script}";
            description = m.MenuName;
        }
        else
        {
            return;
        }

        Modules.Add(new ProfileScriptEntry
        {
            Order       = nextOrder,
            ScriptPath  = scriptPath,
            Enabled     = "1",
            Description = description,
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

    // ─── ドラッグ&ドロップによる並べ替え ─────────────────────────
    /// <summary>
    /// D&amp;D の Drop 確定時に <see cref="Helpers.DataGridRowDragDropBehavior"/> から呼ばれる。
    /// ObservableCollection.Move 経由で <c>CollectionChanged</c> が発火し、
    /// 既存の Dirty 検知ハンドラが自動で <c>IsDirty=true</c> にセットする。
    /// </summary>
    [RelayCommand]
    private void MoveRow(RowMoveRequest? req)
    {
        if (req is null || IsLocked) return;
        if (req.SourceIndex < 0 || req.SourceIndex >= Modules.Count) return;
        if (req.TargetIndex < 0 || req.TargetIndex >= Modules.Count) return;
        if (req.SourceIndex == req.TargetIndex) return;

        Modules.Move(req.SourceIndex, req.TargetIndex);
        // ↑/↓ ボタンの CanExecute を再評価（選択行の位置が変わったため）
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

    // ─── インポート ─────────────────────────────────────────────
    private bool CanImportProfile() => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanImportProfile))]
    private async Task ImportProfileAsync()
    {
        // profiles フォルダを初期ディレクトリに設定
        var initialDir = Profile?.FilePath is not null
            ? System.IO.Path.GetDirectoryName(Profile.FilePath)
            : null;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title            = "インポートするプロファイルを選択",
            Filter           = "CSV ファイル (*.csv)|*.csv",
            InitialDirectory = initialDir ?? ""
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var tempEntry = new ProfileEntry
            {
                Name     = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
                FilePath = dlg.FileName
            };

            var imported = await _profileService.GetProfileModulesAsync(tempEntry);

            foreach (var src in imported)
            {
                Modules.Add(new ProfileScriptEntry
                {
                    Order       = src.Order,
                    ScriptPath  = src.ScriptPath,
                    Enabled     = src.Enabled,
                    Description = src.Description,
                    Segment     = src.Segment,
                    Note        = src.Note
                });
            }

            IsDirty = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"インポートエラー: {ex.Message}",
                "インポート",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

        var module = AvailableModules
            .Select(i => i.Module)
            .FirstOrDefault(m => m is not null
                && string.Equals(m.ModuleDir, moduleDir, StringComparison.OrdinalIgnoreCase));

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
