using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Gpo;

namespace FabriqStudio.ViewModels.Master;

/// <summary>
/// マスタ設計の「ローカル グループポリシー」項目: GPO 辞書から選んだポリシーの一覧。
/// 追加／編集はピッカー ダイアログ（親が <see cref="MasterItemContext.PickGpo"/> で開く）、値は tables[id] に保存する。
/// </summary>
public sealed partial class GpoItemViewModel : MasterItemViewModel
{
    public ObservableCollection<GpoSelectionViewModel> Policies { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private GpoSelectionViewModel? _selectedPolicy;

    [ObservableProperty] private string _summary = "";

    public GpoItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
        => UpdateSummary();

    private IGpoCatalogService? Catalog => Context.GpoCatalog;

    /// <summary>辞書が使える状態で、親がピッカーを提供しているか。</summary>
    public bool CanPick => Context.PickGpo is not null && Catalog is { IsLoaded: true };

    /// <summary>辞書が使えないときの案内（使えるときは空）。</summary>
    public string CatalogState => Catalog switch
    {
        null                 => "GPO 辞書が利用できません",
        { IsLoaded: true }   => "",
        { IsLoading: true }  => "GPO 辞書（ADMX）を読み込み中...",
        var c                => $"GPO 辞書を読み込めません: {c.LoadError}",
    };

    public bool HasPolicies => Policies.Count > 0;

    // ── コマンド ──────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task AddAsync()
    {
        if (Context.PickGpo is null) return;
        var sel = await Context.PickGpo(null);
        if (sel is null) return;

        // 同じポリシーが既にあれば置き換える（重複行を作らない）
        var existing = Policies.FirstOrDefault(p => p.Selection.PolicyId.Equals(sel.PolicyId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Replace(sel, Catalog);
            SelectedPolicy = existing;
        }
        else
        {
            var vm = new GpoSelectionViewModel(sel);
            vm.Refresh(Catalog);
            Policies.Add(vm);
            SelectedPolicy = vm;
        }
        AfterChange();
    }

    private bool CanEditSelected() => SelectedPolicy is not null && CanPick;

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task EditAsync()
    {
        if (Context.PickGpo is null || SelectedPolicy is null) return;
        var target = SelectedPolicy;
        var sel = await Context.PickGpo(target.Selection.Clone());
        if (sel is null) return;

        // 別のポリシーに変えた場合、同じものが他にあればそちらを消す
        var dup = Policies.FirstOrDefault(p => p != target && p.Selection.PolicyId.Equals(sel.PolicyId, StringComparison.OrdinalIgnoreCase));
        if (dup is not null) Policies.Remove(dup);

        target.Replace(sel, Catalog);
        SelectedPolicy = target;
        AfterChange();
    }

    private bool CanRemove() => SelectedPolicy is not null;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedPolicy is null) return;
        Policies.Remove(SelectedPolicy);
        SelectedPolicy = null;
        AfterChange();
    }

    private void AfterChange()
    {
        UpdateSummary();
        OnPropertyChanged(nameof(HasPolicies));
        NotifyChanged();
    }

    /// <summary>辞書の読み込み完了・再読込で親が呼ぶ（表示名・行数・欠落表示を更新）。</summary>
    public void RefreshFromCatalog()
    {
        foreach (var p in Policies) p.Refresh(Catalog);
        UpdateSummary();
        OnPropertyChanged(nameof(CanPick));
        OnPropertyChanged(nameof(CatalogState));
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSummary()
    {
        if (Policies.Count == 0)
        {
            Summary = "0 件";
            return;
        }
        var rows    = Policies.Sum(p => p.RowCount);
        var missing = Policies.Count(p => p.IsMissing);
        Summary = $"{Policies.Count} 件（{rows} 行）"
                  + (missing > 0 ? $" / 辞書に無いもの {missing} 件" : "");
    }

    // ── MasterItemViewModel ──────────────────────────────────────

    public override string CurrentValue => Policies.Count.ToString();
    public override bool   IsModified   => Policies.Count > 0;

    public override void ApplyDefault()
    {
        Policies.Clear();
        SelectedPolicy = null;
        AfterChange();
    }

    public override void LoadFrom(MasterAnswers answers)
    {
        Policies.Clear();
        SelectedPolicy = null;
        foreach (var row in answers.GetTable(Id))
        {
            var sel = GpoSelection.FromRow(row);
            if (sel.PolicyId.Length == 0) continue;
            var vm = new GpoSelectionViewModel(sel);
            vm.Refresh(Catalog);
            Policies.Add(vm);
        }
        AfterChange();
    }

    public override void SaveTo(MasterAnswers answers)
        => answers.Tables[Id] = Policies.Select(p => p.Selection.ToRow()).ToList();
}

/// <summary>一覧の 1 行（選択したポリシー）。辞書を引いて表示名・分類・生成行数を出す。</summary>
public sealed partial class GpoSelectionViewModel : ObservableObject
{
    public GpoSelection Selection { get; private set; }

    public GpoSelectionViewModel(GpoSelection selection)
    {
        Selection    = selection;
        _displayName = selection.DisplayName.Length > 0 ? selection.DisplayName : selection.PolicyId;
        _stateLabel  = GpoStates.Label(selection.State);
        _scopeLabel  = GpoPolicyClass.Label(selection.Scope);
    }

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _categoryPath = "";
    [ObservableProperty] private string _stateLabel;
    [ObservableProperty] private string _scopeLabel;
    [ObservableProperty] private string _elementSummary = "";
    [ObservableProperty] private string _rowCountText = "";
    [ObservableProperty] private bool   _isMissing;
    [ObservableProperty] private bool   _hasErrors;

    public int RowCount { get; private set; }

    public string PolicyId => Selection.PolicyId;

    public void Replace(GpoSelection selection, IGpoCatalogService? catalog)
    {
        Selection = selection;
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(PolicyId));
        Refresh(catalog);
    }

    public void Refresh(IGpoCatalogService? catalog)
    {
        var policy = catalog?.FindPolicy(Selection.PolicyId);
        StateLabel = GpoStates.Label(Selection.State);

        if (policy is null)
        {
            IsMissing      = catalog?.IsLoaded == true;
            DisplayName    = Selection.DisplayName.Length > 0 ? Selection.DisplayName : Selection.PolicyId;
            CategoryPath   = Selection.PolicyId;
            ScopeLabel     = GpoPolicyClass.Label(Selection.Scope);
            ElementSummary = "";
            RowCount       = 0;
            HasErrors      = false;
            RowCountText   = IsMissing ? "辞書に無し" : "";
            return;
        }

        IsMissing    = false;
        DisplayName  = policy.DisplayName;
        CategoryPath = policy.CategoryPath;
        ScopeLabel   = GpoPolicyClass.Label(policy.IsBoth ? Selection.Scope : policy.Class);

        var res = GpoCompiler.Compile(policy, Selection);
        RowCount     = res.Rows.Count;
        HasErrors    = res.HasErrors;
        RowCountText = $"{res.Rows.Count} 行" + (res.HasErrors ? " ⛔" : res.Warnings.Count > 0 ? " ⚠" : "");
        ElementSummary = GpoStates.Normalize(Selection.State) == GpoStates.Enabled ? BuildElementSummary(policy) : "";
    }

    private string BuildElementSummary(GpoPolicy policy)
    {
        var parts = new List<string>();
        foreach (var e in policy.ElementsForUi)
        {
            var v = Selection.GetElementValue(e) ?? e.DefaultValueString();
            switch (e.Type)
            {
                case GpoElementType.Boolean:
                    if (v.Trim() == "1") parts.Add($"☑ {e.DisplayLabel}");
                    break;
                case GpoElementType.Enum:
                    var item = e.Items.FirstOrDefault(i => i.Value.ToString() == v.Trim());
                    if (item is not null) parts.Add($"{e.DisplayLabel}: {item.DisplayName}");
                    break;
                case GpoElementType.List:
                case GpoElementType.MultiText:
                    var n = v.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                    if (n > 0) parts.Add($"{e.DisplayLabel}: {n} 件");
                    break;
                default:
                    if (v.Trim().Length > 0) parts.Add($"{e.DisplayLabel}: {v.Trim()}");
                    break;
            }
        }
        var s = string.Join(" / ", parts);
        return s.Length > 160 ? s[..160] + "…" : s;
    }
}
