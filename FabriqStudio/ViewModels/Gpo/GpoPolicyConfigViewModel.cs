using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Services.Gpo;

namespace FabriqStudio.ViewModels.Gpo;

/// <summary>
/// ポリシー 1 件の「状態（未構成／有効／無効）＋適用先＋要素」を編集し、生成される gpo_list 行をその場で計算する。
/// ピッカー ダイアログと GPO 辞書画面の右ペインで共用する。
/// </summary>
public sealed partial class GpoPolicyConfigViewModel : ObservableObject
{
    private bool _loading;

    /// <summary>状態・要素・適用先のいずれかが変わり、生成行を再計算したときに発火。</summary>
    public event Action? Changed;

    [ObservableProperty] private GpoPolicy? _policy;

    public bool HasPolicy => Policy is not null;

    // ── 表示 ─────────────────────────────────────────────────────
    public string  DisplayName          => Policy?.DisplayName ?? "";
    public string  DisplayNameEn        => Policy?.DisplayNameEn ?? "";
    public bool    HasDisplayNameEn     => !string.IsNullOrEmpty(DisplayNameEn) && DisplayNameEn != DisplayName;
    public string  IdText               => Policy?.Id ?? "";
    public string  CategoryPath         => Policy?.CategoryPath ?? "";
    public string  SupportedOn          => Policy?.SupportedOn ?? "";
    public bool    HasSupportedOn       => !string.IsNullOrEmpty(SupportedOn);
    public string  KeyDisplay           => Policy?.KeyDisplay ?? "";
    public string  ExplainText          => Policy?.ExplainText ?? "";
    public string  ScopeLabel           => Policy?.ScopeLabel ?? "";
    public string? FavoriteNote         => Policy?.FavoriteNote;
    public bool    HasFavoriteNote      => !string.IsNullOrEmpty(FavoriteNote);
    public string  PresentationNotes    => Policy is null ? "" : string.Join("\n", Policy.PresentationNotes);
    public bool    HasPresentationNotes => PresentationNotes.Length > 0;
    public bool    CanChooseScope       => Policy?.IsBoth == true;
    public bool    HasElements          => Elements.Count > 0;

    // ── 状態 ─────────────────────────────────────────────────────
    [ObservableProperty] private string _state = GpoStates.Enabled;

    partial void OnStateChanged(string value)
    {
        OnPropertyChanged(nameof(IsEnabledState));
        OnPropertyChanged(nameof(IsDisabledState));
        OnPropertyChanged(nameof(IsNotConfiguredState));
        OnPropertyChanged(nameof(ElementsEnabled));
        foreach (var e in Elements) e.IsEnabled = value == GpoStates.Enabled;
        Recompute();
    }

    public bool ElementsEnabled => State == GpoStates.Enabled;

    public bool IsEnabledState
    {
        get => State == GpoStates.Enabled;
        set { if (value) State = GpoStates.Enabled; }
    }

    public bool IsDisabledState
    {
        get => State == GpoStates.Disabled;
        set { if (value) State = GpoStates.Disabled; }
    }

    public bool IsNotConfiguredState
    {
        get => State == GpoStates.NotConfigured;
        set { if (value) State = GpoStates.NotConfigured; }
    }

    // ── 適用先（class=Both のときだけ選べる） ──────────────────────
    [ObservableProperty] private string _scope = GpoPolicyClass.Machine;

    partial void OnScopeChanged(string value)
    {
        OnPropertyChanged(nameof(IsMachineScope));
        OnPropertyChanged(nameof(IsUserScope));
        Recompute();
    }

    public bool IsMachineScope
    {
        get => Scope != GpoPolicyClass.User;
        set { if (value) Scope = GpoPolicyClass.Machine; }
    }

    public bool IsUserScope
    {
        get => Scope == GpoPolicyClass.User;
        set { if (value) Scope = GpoPolicyClass.User; }
    }

    // ── 要素・生成行 ─────────────────────────────────────────────
    public ObservableCollection<GpoElementViewModel> Elements    { get; } = [];
    public ObservableCollection<GpoRow>              PreviewRows { get; } = [];
    public ObservableCollection<string>              Errors      { get; } = [];
    public ObservableCollection<string>              Warnings    { get; } = [];

    [ObservableProperty] private string _rowSummary = "";

    public bool HasErrors   => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasRows     => PreviewRows.Count > 0;

    /// <summary>
    /// ポリシーを読み込む。<paramref name="selection"/> があればその値、無ければお気に入りの推奨値、それも無ければ ADMX の既定値。
    /// </summary>
    public void Load(GpoPolicy? policy, GpoSelection? selection)
    {
        _loading = true;
        Policy = policy;
        Elements.Clear();

        if (policy is not null)
        {
            var fav = selection is null ? policy.Favorite : null;

            State = GpoStates.Normalize(selection?.State ?? fav?.State ?? GpoStates.Enabled);

            var wantedScope = selection?.Scope ?? fav?.Scope ?? GpoPolicyClass.Machine;
            Scope = policy.Class == GpoPolicyClass.User    ? GpoPolicyClass.User
                  : policy.Class == GpoPolicyClass.Machine ? GpoPolicyClass.Machine
                  : wantedScope == GpoPolicyClass.User     ? GpoPolicyClass.User
                  :                                          GpoPolicyClass.Machine;

            foreach (var e in policy.ElementsForUi)
            {
                var vm = GpoElementViewModel.Create(e, OnElementChanged);
                var v  = selection?.GetElementValue(e);
                if (v is null && fav?.Elements is not null)
                {
                    if (!fav.Elements.TryGetValue(e.Id, out v) && e.ValueName is not null)
                        fav.Elements.TryGetValue(e.ValueName, out v);
                }
                vm.Value     = v ?? e.DefaultValueString();
                vm.IsEnabled = State == GpoStates.Enabled;
                Elements.Add(vm);
            }
        }

        _loading = false;
        Recompute();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>現在の編集内容を回答用の選択に変換する。</summary>
    public GpoSelection ToSelection()
    {
        var sel = new GpoSelection
        {
            PolicyId    = Policy?.Id ?? "",
            DisplayName = Policy?.DisplayName ?? "",
            State       = State,
            Scope       = Scope,
        };
        foreach (var e in Elements) sel.Elements[e.Id] = e.Value;
        return sel;
    }

    private void OnElementChanged() => Recompute();

    private void Recompute()
    {
        if (_loading) return;

        PreviewRows.Clear();
        Errors.Clear();
        Warnings.Clear();

        if (Policy is null)
        {
            RowSummary = "";
        }
        else
        {
            var res = GpoCompiler.Compile(Policy, ToSelection());
            foreach (var r in res.Rows)     PreviewRows.Add(r);
            foreach (var e in res.Errors)   Errors.Add(e);
            foreach (var w in res.Warnings) Warnings.Add(w);
            RowSummary = $"gpo_list.csv に生成される行: {res.Rows.Count}";
        }

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasRows));
        Changed?.Invoke();
    }
}
