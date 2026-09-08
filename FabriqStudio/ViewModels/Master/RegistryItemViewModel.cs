using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Models.Master;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels.Master;

/// <summary>
/// マスタ設計の「レジストリ追加」項目: レジストリ辞書から選んだ設定の一覧。
/// 追加はピッカー ダイアログ（親が <see cref="MasterItemContext.PickRegistry"/> で開く）、値は行ごとに編集でき、
/// tables[id] に <see cref="RegistrySelection"/> の行として保存する。
/// </summary>
public sealed partial class RegistryItemViewModel : MasterItemViewModel
{
    public ObservableCollection<RegistrySelectionViewModel> Entries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private RegistrySelectionViewModel? _selectedEntry;

    [ObservableProperty] private string _summary = "";

    public RegistryItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
        => UpdateSummary();

    private IRegistryCollectionService? Dictionary => Context.RegistryDictionary;

    /// <summary>辞書が使える状態で、親がピッカーを提供しているか。</summary>
    public bool CanPick => Context.PickRegistry is not null && Dictionary is not null;

    public bool HasEntries => Entries.Count > 0;

    // ── コマンド ──────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task AddAsync()
    {
        if (Context.PickRegistry is null) return;
        var entry = await Context.PickRegistry();
        if (entry is null) return;
        AddEntry(entry);
    }

    /// <summary>辞書エントリを 1 件追加する（同じ ID が既にあればそれを選択するだけ）。ピッカー以外（テスト等）からも使う。</summary>
    public RegistrySelectionViewModel AddEntry(RegistryTemplateEntry entry)
    {
        var existing = Entries.FirstOrDefault(e => e.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedEntry = existing;
            return existing;
        }

        var vm = Attach(new RegistrySelectionViewModel(new RegistrySelection { Id = entry.Id, Title = entry.Title, Value = entry.Value }));
        Entries.Add(vm);
        SelectedEntry = vm;
        AfterChange();
        return vm;
    }

    private bool CanRemove() => SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedEntry is null) return;
        Detach(SelectedEntry);
        Entries.Remove(SelectedEntry);
        SelectedEntry = null;
        AfterChange();
    }

    private RegistrySelectionViewModel Attach(RegistrySelectionViewModel vm)
    {
        vm.Refresh(Dictionary);
        vm.PropertyChanged += OnEntryPropertyChanged;
        return vm;
    }

    private void Detach(RegistrySelectionViewModel vm) => vm.PropertyChanged -= OnEntryPropertyChanged;

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegistrySelectionViewModel.Value)) NotifyChanged();
    }

    private void AfterChange()
    {
        UpdateSummary();
        OnPropertyChanged(nameof(HasEntries));
        NotifyChanged();
    }

    /// <summary>辞書の再読込などで親が呼ぶ（表示名・キー・欠落表示を更新）。</summary>
    public void RefreshFromDictionary()
    {
        foreach (var e in Entries) e.Refresh(Dictionary);
        UpdateSummary();
        OnPropertyChanged(nameof(CanPick));
        AddCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSummary()
    {
        if (Entries.Count == 0)
        {
            Summary = "0 件";
            return;
        }
        var hklm    = Entries.Count(e => e.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase));
        var hkcu    = Entries.Count(e => e.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase));
        var missing = Entries.Count(e => e.IsMissing);
        var changed = Entries.Count(e => e.IsValueChanged);
        Summary = $"{Entries.Count} 件（HKLM {hklm} / HKCU {hkcu}）"
                  + (changed > 0 ? $" / 辞書と違う値 {changed} 件" : "")
                  + (missing > 0 ? $" / 辞書に無いもの {missing} 件" : "");
    }

    // ── MasterItemViewModel ──────────────────────────────────────

    public override string CurrentValue => Entries.Count.ToString();
    public override bool   IsModified   => Entries.Count > 0;

    public override void ApplyDefault()
    {
        foreach (var e in Entries) Detach(e);
        Entries.Clear();
        SelectedEntry = null;
        AfterChange();
    }

    public override void LoadFrom(MasterAnswers answers)
    {
        foreach (var e in Entries) Detach(e);
        Entries.Clear();
        SelectedEntry = null;
        foreach (var row in answers.GetTable(Id))
        {
            var sel = RegistrySelection.FromRow(row);
            if (sel.Id.Length == 0) continue;
            if (Entries.Any(e => e.Id.Equals(sel.Id, StringComparison.OrdinalIgnoreCase))) continue;
            Entries.Add(Attach(new RegistrySelectionViewModel(sel)));
        }
        AfterChange();
    }

    public override void SaveTo(MasterAnswers answers)
        => answers.Tables[Id] = Entries.Select(e => e.Selection.ToRow()).ToList();
}

/// <summary>一覧の 1 行（選択した辞書エントリ）。辞書を引いてキー・型・辞書値を出し、値は行で編集できる。</summary>
public sealed partial class RegistrySelectionViewModel : ObservableObject
{
    public RegistrySelection Selection { get; }

    public RegistrySelectionViewModel(RegistrySelection selection)
    {
        Selection = selection;
        _title    = selection.Title.Length > 0 ? selection.Title : selection.Id;
        _value    = selection.Value;
    }

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _hive    = "";
    [ObservableProperty] private string _keyPath = "";
    [ObservableProperty] private string _keyName = "";
    [ObservableProperty] private string _type    = "";
    [ObservableProperty] private bool   _isMissing;

    /// <summary>辞書に登録されている値（↺ で戻す先）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValueChanged))]
    [NotifyPropertyChangedFor(nameof(ValueHint))]
    private string _dictValue = "";

    /// <summary>この行で書く値。辞書の値を初期値にし、行ごとに変更できる。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValueChanged))]
    [NotifyPropertyChangedFor(nameof(ValueHint))]
    private string _value;

    public string Id => Selection.Id;

    /// <summary>KeyPath\KeyName（表示用。HKEY_ 接頭辞は落とす）。</summary>
    public string KeyText => IsMissing ? $"ID {Id}" : $"{StripHive(KeyPath)}\\{KeyName}";

    public bool   IsValueChanged => !Value.Equals(DictValue, StringComparison.Ordinal);
    public string ValueHint      => IsValueChanged ? $"辞書の値: {DictValue}" : "辞書の値のまま";

    partial void OnValueChanged(string value) => Selection.Value = value;

    [RelayCommand]
    private void ResetValue() => Value = DictValue;

    public void Refresh(IRegistryCollectionService? dictionary)
    {
        var entry = dictionary?.Entries.FirstOrDefault(e => e.Id.Equals(Selection.Id, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            IsMissing = dictionary is not null;
            Title     = Selection.Title.Length > 0 ? Selection.Title : Selection.Id;
            Hive      = "";
            KeyPath   = "";
            KeyName   = "";
            Type      = "";
            DictValue = Value;
            OnPropertyChanged(nameof(KeyText));
            return;
        }

        IsMissing = false;
        Title     = entry.Title;
        Hive      = entry.Hive;
        KeyPath   = entry.KeyPath;
        KeyName   = entry.KeyName;
        Type      = entry.Type;
        DictValue = entry.Value;
        Selection.Title = entry.Title;
        OnPropertyChanged(nameof(KeyText));
    }

    private static string StripHive(string keyPath)
    {
        var k = keyPath.Trim().Trim('\\');
        foreach (var prefix in new[] { "HKEY_LOCAL_MACHINE\\", "HKEY_CURRENT_USER\\", "HKLM\\", "HKCU\\" })
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return k[prefix.Length..];
        return k;
    }
}
