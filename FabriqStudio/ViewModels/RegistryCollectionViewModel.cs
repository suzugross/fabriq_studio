using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// レジストリ辞書画面の ViewModel。
/// 左ペイン（フィルタ + 一覧）と右ペイン（詳細フォーム）を 1 つの VM で管理する。
/// IRegistryCollectionService はワークスペース非依存。
/// </summary>
public partial class RegistryCollectionViewModel : ObservableObject
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

    [ObservableProperty] private bool   _isEntrySelected;
    [ObservableProperty] private bool   _isWorkspaceOpen;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>RefreshFilteredEntries 後の選択復元中にフォームの再充填を抑制するフラグ。</summary>
    private bool _suppressFormRefill;

    // ── コンストラクタ ────────────────────────────────────────────────────

    public RegistryCollectionViewModel(
        IRegistryCollectionService service,
        IWorkspaceService          workspace)
    {
        _service   = service;
        _workspace = workspace;

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
        StatusMessage   = "新規エントリを入力後、「保存」を押してください。";
    }

    // ── コマンド: 保存 ────────────────────────────────────────────────────

    [RelayCommand]
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

    [RelayCommand]
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
        var entry  = BuildEntryFromForm();
        var result = await _service.ExportToWorkspaceAsync(entry, _workspace.RootPath);

        StatusMessage = result switch
        {
            { Error: not null } => $"エクスポート失敗: {result.Error}",
            { Skipped: > 0   } => "このエントリは既に reg_config CSV に登録されています。",
            _                  => $"reg_config CSV に追加しました（{entry.Hive}）。",
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
