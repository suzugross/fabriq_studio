using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Services.Gpo;

namespace FabriqStudio.ViewModels.Gpo;

/// <summary>
/// GPO 辞書の検索・一覧（左ペイン）。ホスト（ピッカー / 辞書画面）が辞書の読み込み変化を
/// <see cref="RefreshCatalogState"/> で伝える（自分ではイベント購読しない = 破棄が簡単）。
/// </summary>
public sealed partial class GpoBrowserViewModel : ObservableObject
{
    public const string ScopeAll     = "すべて";
    public const string ScopeMachine = "コンピューターの構成";
    public const string ScopeUser    = "ユーザーの構成";
    public const string CategoryAll  = "すべて";

    private readonly IGpoCatalogService _service;

    public GpoBrowserViewModel(IGpoCatalogService service)
    {
        _service = service;
        CategoryOptions.Add(CategoryAll);
        RefreshCatalogState();
    }

    // ── フィルタ ─────────────────────────────────────────────────
    [ObservableProperty] private string _searchText = "";
    partial void OnSearchTextChanged(string value) => Refresh();

    public IReadOnlyList<string> ScopeOptions { get; } = [ScopeAll, ScopeMachine, ScopeUser];

    [ObservableProperty] private string _scopeFilter = ScopeAll;
    partial void OnScopeFilterChanged(string value) => Refresh();

    public ObservableCollection<string> CategoryOptions { get; } = [];

    [ObservableProperty] private string _categoryFilter = CategoryAll;
    partial void OnCategoryFilterChanged(string value) => Refresh();

    [ObservableProperty] private bool _favoritesOnly;
    partial void OnFavoritesOnlyChanged(bool value) => Refresh();

    // ── 結果 ─────────────────────────────────────────────────────
    public ObservableCollection<GpoPolicy> Results { get; } = [];

    [ObservableProperty] private string     _resultSummary = "";
    [ObservableProperty] private GpoPolicy? _selectedPolicy;

    // ── 辞書の状態 ───────────────────────────────────────────────
    [ObservableProperty] private bool    _isCatalogReady;
    [ObservableProperty] private bool    _isCatalogLoading;
    [ObservableProperty] private string  _catalogInfo = "";
    [ObservableProperty] private string? _catalogError;

    /// <summary>辞書の読み込み状態を取り込み、カテゴリ一覧と結果を作り直す（UI スレッドで呼ぶ）。</summary>
    public void RefreshCatalogState()
    {
        IsCatalogReady   = _service.IsLoaded;
        IsCatalogLoading = _service.IsLoading;
        CatalogError     = _service.LoadError;
        CatalogInfo      = _service.Catalog?.VersionTag
                           ?? (IsCatalogLoading ? "ADMX を読み込み中..." : "GPO 辞書は未読込です");

        var keep = CategoryFilter;
        CategoryOptions.Clear();
        CategoryOptions.Add(CategoryAll);
        if (_service.Catalog is { } cat)
            foreach (var c in cat.TopCategories) CategoryOptions.Add(c);
        CategoryFilter = CategoryOptions.Contains(keep) ? keep : CategoryAll;

        SelectedPolicy = null;
        Refresh();
    }

    /// <summary>フィルタで結果を作り直す。選択中のポリシーが結果に残っていれば選択を維持する。</summary>
    public void Refresh()
    {
        var keep = SelectedPolicy;
        Results.Clear();

        if (!IsCatalogReady)
        {
            ResultSummary = "";
            SelectedPolicy = null;
            return;
        }

        var scope = ScopeFilter switch
        {
            ScopeMachine => GpoPolicyClass.Machine,
            ScopeUser    => GpoPolicyClass.User,
            _            => "",
        };
        var result = _service.Search(new GpoSearchQuery
        {
            Text          = SearchText,
            Scope         = scope,
            TopCategory   = CategoryFilter == CategoryAll ? null : CategoryFilter,
            FavoritesOnly = FavoritesOnly,
            Limit         = 400,
        });

        foreach (var p in result.Items) Results.Add(p);
        ResultSummary = result.TotalMatches > result.Items.Count
            ? $"{result.TotalMatches} 件中 {result.Items.Count} 件を表示（検索語で絞り込んでください）"
            : $"{result.TotalMatches} 件";

        SelectedPolicy = keep is not null && Results.Contains(keep) ? keep : null;
    }

    /// <summary>ID でポリシーを選択する（結果に無ければ先頭に差し込んで選ぶ）。見つからなければ false。</summary>
    public bool SelectPolicy(string? id)
    {
        var p = _service.FindPolicy(id);
        if (p is null) return false;
        if (!Results.Contains(p)) Results.Insert(0, p);
        SelectedPolicy = p;
        return true;
    }
}
