using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Helpers;

namespace FabriqStudio.ViewModels;

/// <summary>
/// レジストリ辞書画面の ViewModel。
/// 左ペイン（フィルタ + 一覧）と右ペイン（詳細フォーム）を 1 つの VM で管理する。
/// IRegistryCollectionService はワークスペース非依存。
/// </summary>
public partial class RegistryCollectionViewModel : ObservableObject, IDataSetDependentViewModel
{
    private readonly IRegistryCollectionService _service;
    private readonly IWorkspaceService          _workspace;

    // ── フィルタ ──────────────────────────────────────────────────────────

    [ObservableProperty] private string _categoryFilter = "すべて";
    [ObservableProperty] private string _hiveFilter     = "すべて";
    [ObservableProperty] private string _searchText     = "";

    // ── 一覧 ──────────────────────────────────────────────────────────────

    public ObservableCollection<RegistryTemplateEntry> FilteredEntries  { get; } = new();
    public ObservableCollection<string>                CategoryOptions  { get; } = new();

    public IReadOnlyList<string> HiveOptions     { get; } = ["すべて", "HKLM", "HKCU"];
    public IReadOnlyList<string> HiveFormOptions { get; } = ["HKLM", "HKCU"];
    public IReadOnlyList<string> TypeOptions     { get; } =
        ["REG_DWORD", "REG_SZ", "REG_BINARY", "REG_MULTI_SZ", "REG_EXPAND_SZ"];

    [ObservableProperty] private RegistryTemplateEntry? _selectedEntry;

    // ── 詳細フォーム ─────────────────────────────────────────────────────

    [ObservableProperty] private string _editId          = "";
    [ObservableProperty] private string _editCategory    = "";
    [ObservableProperty] private string _editTitle       = "";
    [ObservableProperty] private string _editHive        = "HKLM";
    [ObservableProperty] private string _editKeyPath     = "";
    [ObservableProperty] private string _editKeyName     = "";
    [ObservableProperty] private string _editType        = "REG_DWORD";
    [ObservableProperty] private string _editValue       = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editTags        = "";

    // ── 状態 ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteEntryCommand))]
    private bool _isLocked = true;

    [ObservableProperty] private bool   _isEntrySelected;
    [ObservableProperty] private bool   _isWorkspaceOpen;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>RefreshFilteredEntries 後の選択復元中にフォームの再充填を抑制するフラグ。</summary>
    private bool _suppressFormRefill;

    // ── コンストラクタ ────────────────────────────────────────────────────

    private readonly IModuleDataResolver _resolver;
    private readonly IDataSetContext     _dataSet;
    private readonly IProfileDataService _profileData;

    /// <summary>書き先（編集先データセット）の表示。</summary>
    public string DataSetLabel    => DataSetText.WriteTarget(_dataSet.Current);
    public bool   IsProfileTarget => _dataSet.Current is not null;

    public RegistryCollectionViewModel(
        IRegistryCollectionService service,
        IWorkspaceService          workspace,
        IModuleDataResolver        resolver,
        IDataSetContext            dataSet,
        IProfileDataService        profileData)
    {
        _service     = service;
        _workspace   = workspace;
        _resolver    = resolver;
        _dataSet     = dataSet;
        _profileData = profileData;
        dataSet.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(DataSetLabel));
            OnPropertyChanged(nameof(IsProfileTarget));
        };

        IsWorkspaceOpen = workspace.IsOpen;
        workspace.WorkspaceChanged += (_, e) => IsWorkspaceOpen = e.NewPath is not null;

        RefreshCategoryOptions();
        RefreshFilteredEntries();
    }

    // ── フィルタ変更ハンドラ ─────────────────────────────────────────────

    partial void OnCategoryFilterChanged(string value) => RefreshFilteredEntries();
    partial void OnHiveFilterChanged(string value)     => RefreshFilteredEntries();
    partial void OnSearchTextChanged(string value)     => RefreshFilteredEntries();

    // ── 選択変更ハンドラ ─────────────────────────────────────────────────

    partial void OnSelectedEntryChanged(RegistryTemplateEntry? value)
    {
        if (_suppressFormRefill) return;

        IsEntrySelected = value is not null;
        IsLocked        = true;
        StatusMessage   = "";

        if (value is null) { ClearEditForm(); return; }

        SetFormFromEntry(value);
    }

    // ── コマンド: 新規追加 ────────────────────────────────────────────────

    [RelayCommand]
    private void AddEntry()
    {
        _suppressFormRefill = true;
        SelectedEntry       = null;
        _suppressFormRefill = false;

        ClearEditForm();
        EditId   = Guid.NewGuid().ToString("N")[..8];
        EditHive = "HKLM";
        EditType = "REG_DWORD";

        IsEntrySelected = true; // フォームを有効化（一覧の選択なし）
        IsLocked        = false; // 新規エントリは即編集可能
        StatusMessage   = "新規エントリを入力後、「保存」を押してください。";
    }

    // ── コマンド: 保存 ────────────────────────────────────────────────────

    private bool CanSaveEntry() => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSaveEntry))]
    private async Task SaveEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        { StatusMessage = "タイトルを入力してください。"; return; }

        if (string.IsNullOrWhiteSpace(EditKeyPath))
        { StatusMessage = "KeyPath を入力してください。"; return; }

        if (string.IsNullOrWhiteSpace(EditKeyName))
        { StatusMessage = "KeyName を入力してください。"; return; }

        var entry = BuildEntryFromForm();
        var isNew = !_service.Entries.Any(e => e.Id == EditId);

        if (isNew)
            await _service.AddAsync(entry);
        else
            await _service.UpdateAsync(entry);

        RefreshCategoryOptions();
        RefreshFilteredEntries();

        // RefreshFilteredEntries で SelectedEntry が null になるため、
        // 選択と表示を手動で復元する。
        var savedEntry = FilteredEntries.FirstOrDefault(e => e.Id == entry.Id);
        _suppressFormRefill = true;
        SelectedEntry       = savedEntry;
        IsEntrySelected     = true;
        _suppressFormRefill = false;

        SetFormFromEntry(entry); // RefreshFilteredEntries でクリアされたフォームを復元

        StatusMessage = isNew ? "エントリを追加しました。" : "エントリを保存しました。";
    }

    // ── コマンド: 削除 ────────────────────────────────────────────────────

    private bool CanDeleteEntry() => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanDeleteEntry))]
    private async Task DeleteEntryAsync()
    {
        if (!IsEntrySelected || string.IsNullOrEmpty(EditId)) return;

        var title  = string.IsNullOrEmpty(EditTitle) ? EditId : EditTitle;
        var result = MessageBox.Show(
            $"「{title}」を削除しますか？\nこの操作は元に戻せません。",
            "エントリの削除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK) return;

        await _service.RemoveAsync(EditId);

        IsEntrySelected = false;
        ClearEditForm();
        RefreshCategoryOptions();
        RefreshFilteredEntries();
        StatusMessage = "エントリを削除しました。";
    }

    // ── コマンド: ワークスペースへエクスポート ────────────────────────────

    [RelayCommand]
    private async Task ExportToWorkspaceAsync()
    {
        if (!IsEntrySelected || _workspace.RootPath is null) return;

        // 未保存の内容でもエクスポートできるよう、フォームの現在値を使用する
        var entry   = BuildEntryFromForm();
        var isHkcu  = entry.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase);
        var module  = isHkcu ? "reg_hkcu_config" : "reg_hklm_config";
        var csvName = isHkcu ? "reg_hkcu_list.csv" : "reg_hklm_list.csv";

        var rel = _resolver.ModuleRelPath(module, csvName);
        if (rel is null)
        {
            StatusMessage = $"エクスポート失敗: モジュール {module} がワークスペースにありません。";
            return;
        }

        // 編集先データセット（PDF）が選ばれていれば、reg_* をモジュール単位で取り込んでから PDF 側に追記する
        var dataSet = _dataSet.Current;
        if (dataSet is not null)
            await _profileData.MaterializeCsvAsync(dataSet, module, csvName);
        var target = _resolver.ResolveWrite(rel, dataSet);

        var result = await _service.ExportToCsvAsync(entry, target.AbsPath);

        StatusMessage = result switch
        {
            { Error: not null } => $"エクスポート失敗: {result.Error}",
            { Skipped: > 0   } => $"このエントリは既に {target.RelPath} に登録されています。",
            _                  => $"{target.RelPath} に追加しました（{entry.Hive}）。",
        };
    }

    // ── ヘルパー: フォーム ────────────────────────────────────────────────

    private RegistryTemplateEntry BuildEntryFromForm() => new()
    {
        Id          = EditId,
        Category    = EditCategory.Trim(),
        Title       = EditTitle.Trim(),
        Hive        = EditHive,
        KeyPath     = EditKeyPath.Trim(),
        KeyName     = EditKeyName.Trim(),
        Type        = EditType,
        Value       = EditValue.Trim(),
        Description = EditDescription,
        Tags        = EditTags.Trim(),
    };

    private void SetFormFromEntry(RegistryTemplateEntry e)
    {
        EditId          = e.Id;
        EditCategory    = e.Category;
        EditTitle       = e.Title;
        EditHive        = e.Hive;
        EditKeyPath     = e.KeyPath;
        EditKeyName     = e.KeyName;
        EditType        = e.Type;
        EditValue       = e.Value;
        EditDescription = e.Description;
        EditTags        = e.Tags;
    }

    private void ClearEditForm()
    {
        EditId = EditCategory = EditTitle = EditKeyPath =
        EditKeyName = EditValue = EditDescription = EditTags = "";
        EditHive = "HKLM";
        EditType = "REG_DWORD";
    }

    // ── ヘルパー: 一覧・カテゴリ ─────────────────────────────────────────

    private void RefreshCategoryOptions()
    {
        var current = CategoryFilter;
        CategoryOptions.Clear();
        CategoryOptions.Add("すべて");
        foreach (var cat in _service.Entries
                     .Select(e => e.Category)
                     .Where(c => !string.IsNullOrEmpty(c))
                     .Distinct()
                     .OrderBy(x => x))
            CategoryOptions.Add(cat);

        // 現在の選択が無効になった場合のみリセット
        if (!CategoryOptions.Contains(current))
            CategoryFilter = "すべて";
    }

    private void RefreshFilteredEntries()
    {
        FilteredEntries.Clear();
        var query = _service.Entries.AsEnumerable();

        if (CategoryFilter != "すべて")
            query = query.Where(e => e.Category == CategoryFilter);

        if (HiveFilter != "すべて")
            query = query.Where(e => e.Hive == HiveFilter);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.Title.ToLowerInvariant().Contains(q)   ||
                e.Tags.ToLowerInvariant().Contains(q)    ||
                e.KeyName.ToLowerInvariant().Contains(q));
        }

        foreach (var e in query)
            FilteredEntries.Add(e);
    }
}
