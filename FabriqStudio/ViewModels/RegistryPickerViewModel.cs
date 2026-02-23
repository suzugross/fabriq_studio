using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FabriqStudio.Models;

namespace FabriqStudio.ViewModels;

/// <summary>
/// レジストリ辞書からエントリを選択するピッカーダイアログの ViewModel。
/// DI 非依存 — ダイアログ側でデータを直接渡して構築する。
/// </summary>
public partial class RegistryPickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<RegistryTemplateEntry> _allEntries;
    private readonly string? _targetHive;

    // ── フィルタ ────────────────────────────────────────────────────────
    [ObservableProperty] private string _searchText     = "";
    [ObservableProperty] private string _categoryFilter = "すべて";
    [ObservableProperty] private string _hiveFilter     = "すべて";

    // ── 一覧 ────────────────────────────────────────────────────────────
    public ObservableCollection<RegistryTemplateEntry> FilteredEntries { get; } = new();
    public ObservableCollection<string> CategoryOptions { get; } = new();
    public IReadOnlyList<string> HiveOptions { get; } = ["すべて", "HKLM", "HKCU"];

    // ── Hive セレクタの有効/無効 ─────────────────────────────────────────
    /// <summary>targetHive 未指定（自由選択可能）の場合 true。指定済みなら false。</summary>
    public bool IsHiveSelectorEnabled => _targetHive is null;

    // ── 選択 ────────────────────────────────────────────────────────────
    [ObservableProperty] private RegistryTemplateEntry? _selectedEntry;
    [ObservableProperty] private bool _hasSelection;

    /// <param name="entries">辞書エントリ一覧</param>
    /// <param name="targetHive">
    /// Hive を固定する場合は "HKLM" or "HKCU"。null ならフリー選択。
    /// </param>
    public RegistryPickerViewModel(
        IReadOnlyList<RegistryTemplateEntry> entries,
        string? targetHive = null)
    {
        _allEntries = entries;
        _targetHive = targetHive;

        if (_targetHive is not null)
            _hiveFilter = _targetHive;  // フィールド直書きで初期値を上書き

        RefreshCategoryOptions();
        RefreshFilteredEntries();
    }

    partial void OnSearchTextChanged(string value)    => RefreshFilteredEntries();
    partial void OnCategoryFilterChanged(string value) => RefreshFilteredEntries();

    partial void OnHiveFilterChanged(string value)
    {
        // 安全弁: targetHive が指定されている場合は強制復元
        if (_targetHive is not null && value != _targetHive)
        {
            HiveFilter = _targetHive;
            return;
        }
        RefreshFilteredEntries();
    }

    partial void OnSelectedEntryChanged(RegistryTemplateEntry? value)
        => HasSelection = value is not null;

    private void RefreshCategoryOptions()
    {
        CategoryOptions.Clear();
        CategoryOptions.Add("すべて");
        foreach (var cat in _allEntries
                     .Select(e => e.Category)
                     .Where(c => !string.IsNullOrEmpty(c))
                     .Distinct()
                     .OrderBy(x => x))
            CategoryOptions.Add(cat);
    }

    private void RefreshFilteredEntries()
    {
        FilteredEntries.Clear();
        var query = _allEntries.AsEnumerable();

        if (CategoryFilter != "すべて")
            query = query.Where(e => e.Category == CategoryFilter);
        if (HiveFilter != "すべて")
            query = query.Where(e => e.Hive == HiveFilter);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.Title.ToLowerInvariant().Contains(q) ||
                e.Tags.ToLowerInvariant().Contains(q) ||
                e.KeyName.ToLowerInvariant().Contains(q));
        }

        foreach (var e in query)
            FilteredEntries.Add(e);
    }
}
