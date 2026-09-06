using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Gpo;

namespace FabriqStudio.ViewModels.Master;

/// <summary>資材のドロップを受け付ける項目 VM（View のドロップハンドラが呼ぶ）。</summary>
public interface IAssetDropTarget
{
    MasterDropSpec? DropSpec { get; }
    /// <summary>ドロップ設定があり、編集可能な状態か。</summary>
    bool CanDrop { get; }
    string DropHint { get; }
    Task AcceptDropAsync(IReadOnlyList<string> paths);
}

/// <summary>ドロップされた資材をモジュールへ配置する処理（親 VM がサービス呼び出しと上書き確認を担当）。</summary>
public delegate Task<AssetDropResult> AssetImportHandler(MasterDropSpec spec, IReadOnlyList<string> paths);

/// <summary>項目 VM が親から受け取る共通の文脈。</summary>
public sealed class MasterItemContext
{
    public required Action              OnChanged { get; init; }
    public required Func<bool>          CanEdit   { get; init; }
    public          AssetImportHandler? Import    { get; init; }

    /// <summary>action 項目が実行可能か（識別子ごとに親が判断）。</summary>
    public Func<string, bool>? CanRunAction { get; init; }

    /// <summary>action 項目の実行（親が識別子を見て処理し、項目の Status / IsRunning を更新する）。</summary>
    public Func<ActionItemViewModel, Task>? RunAction { get; init; }

    /// <summary>gpo 項目: 辞書ダイアログでポリシーを選ぶ（引数は編集時の現在値、キャンセルで null）。</summary>
    public Func<GpoSelection?, Task<GpoSelection?>>? PickGpo { get; init; }

    /// <summary>gpo 項目: 表示名・生成行数の解決に使う辞書。</summary>
    public IGpoCatalogService? GpoCatalog { get; init; }
}

/// <summary>ボタンで処理を実行する項目（例: ODT のオフライン資材ダウンロード）。値は持たない。</summary>
public sealed partial class ActionItemViewModel : MasterItemViewModel
{
    public ActionItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible)) RunCommand.NotifyCanExecuteChanged();
        };
    }

    public string ActionId => Item.Action ?? "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty] private string? _status;

    public override string CurrentValue => "";
    public override bool IsModified => false;
    public override void ApplyDefault() { Status = null; }
    public override void LoadFrom(MasterAnswers answers) { Status = null; }
    public override void SaveTo(MasterAnswers answers) { }

    private bool CanRun() => !IsRunning && IsVisible && (Context.CanRunAction?.Invoke(ActionId) ?? false);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAsync() => Context.RunAction?.Invoke(this) ?? Task.CompletedTask;

    /// <summary>前提（setup.exe の有無など）が変わったときに親が呼ぶ。</summary>
    public void RefreshCanExecute() => RunCommand.NotifyCanExecuteChanged();
}

/// <summary>
/// マスタ設計画面の質問 1 件の VM 基底。テンプレート項目（<see cref="Item"/>）と現在値を持ち、
/// 値が変わると親 VM の Dirty 検知・プレビュー再計算（<see cref="MasterItemContext.OnChanged"/>）を呼ぶ。
/// </summary>
public abstract partial class MasterItemViewModel : ObservableObject
{
    public MasterItem Item { get; }

    protected readonly MasterItemContext Context;

    protected MasterItemViewModel(MasterItem item, MasterItemContext context)
    {
        Item    = item;
        Context = context;
    }

    public string  Id        => Item.Id;
    public string  Label     => Item.Label;
    public string? Help      => Item.Help;
    public bool    HasHelp   => !string.IsNullOrWhiteSpace(Item.Help);
    public string  Target    => Item.Target ?? "";
    public bool    HasTarget => !string.IsNullOrWhiteSpace(Item.Target);
    public string  Kind      => Item.Kind ?? MasterItemKinds.Module;

    /// <summary>バッジ文言（対応 / 辞書 / 配備 / 手動 / fabriq側）。</summary>
    public string KindLabel => Kind switch
    {
        MasterItemKinds.Dict   => "辞書",
        MasterItemKinds.Deploy => "配備",
        MasterItemKinds.Manual => "手動",
        MasterItemKinds.Fabriq => "fabriq側",
        _                      => "対応",
    };

    /// <summary>visibleWhen の評価結果。親 VM が更新する。</summary>
    [ObservableProperty] private bool _isVisible = true;

    /// <summary>visibleWhen の参照元として使う現在値（bool は "1"/"0"、choice は Value、text は文字列）。</summary>
    public abstract string CurrentValue { get; }

    /// <summary>テンプレートの既定値から変更されているか（章レールの「変更 n 件」に使う）。</summary>
    public abstract bool IsModified { get; }

    /// <summary>テンプレートの既定値に戻す（新規マスタ作成時）。通知は抑制しない。</summary>
    public abstract void ApplyDefault();

    public abstract void LoadFrom(MasterAnswers answers);
    public abstract void SaveTo(MasterAnswers answers);

    protected void NotifyChanged()
    {
        OnPropertyChanged(nameof(CurrentValue));
        OnPropertyChanged(nameof(IsModified));
        Context.OnChanged();
    }
}

/// <summary>説明だけの行。</summary>
public sealed class InfoItemViewModel : MasterItemViewModel
{
    public InfoItemViewModel(MasterItem item, MasterItemContext context) : base(item, context) { }
    public override string CurrentValue => "";
    public override bool IsModified => false;
    public override void ApplyDefault() { }
    public override void LoadFrom(MasterAnswers answers) { }
    public override void SaveTo(MasterAnswers answers) { }
}

public sealed partial class BoolItemViewModel : MasterItemViewModel
{
    [ObservableProperty] private bool _isChecked;

    public BoolItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
        => _isChecked = DefaultChecked;

    private bool DefaultChecked => Item.Default?.Trim() == "1";

    partial void OnIsCheckedChanged(bool value) => NotifyChanged();

    public override string CurrentValue => IsChecked ? "1" : "0";
    public override bool IsModified => IsChecked != DefaultChecked;
    public override void ApplyDefault() => IsChecked = DefaultChecked;
    public override void LoadFrom(MasterAnswers answers)
        => IsChecked = answers.Values.TryGetValue(Id, out var v) ? v.Trim() == "1" : DefaultChecked;
    public override void SaveTo(MasterAnswers answers) => answers.Values[Id] = CurrentValue;
}

public sealed partial class ChoiceItemViewModel : MasterItemViewModel
{
    public IReadOnlyList<MasterChoice> Options { get; }

    [ObservableProperty] private MasterChoice? _selected;

    public ChoiceItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
    {
        Options   = item.Options ?? [];
        _selected = DefaultOption;
    }

    private MasterChoice? DefaultOption => FindOption(Item.Default) ?? Options.FirstOrDefault();

    partial void OnSelectedChanged(MasterChoice? value) => NotifyChanged();

    private MasterChoice? FindOption(string? value)
        => value is null ? null : Options.FirstOrDefault(o => o.Value == value);

    public override string CurrentValue => Selected?.Value ?? "";
    public override bool IsModified => CurrentValue != (DefaultOption?.Value ?? "");
    public override void ApplyDefault() => Selected = DefaultOption;
    public override void LoadFrom(MasterAnswers answers)
    {
        if (answers.Values.TryGetValue(Id, out var v) && FindOption(v) is { } opt) Selected = opt;
        else ApplyDefault();
    }
    public override void SaveTo(MasterAnswers answers) => answers.Values[Id] = CurrentValue;
}

public sealed partial class TextItemViewModel : MasterItemViewModel
{
    [ObservableProperty] private string _text = "";

    public TextItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
        => _text = item.Default ?? "";

    public bool    IsMultiline => Item.Type == MasterItemTypes.Multiline;
    public bool    IsSecret    => Item.Secret;
    public string  Placeholder => Item.Placeholder ?? "";
    public string  Unit        => Item.Unit ?? "";
    public bool    HasUnit     => !string.IsNullOrEmpty(Item.Unit);

    partial void OnTextChanged(string value) => NotifyChanged();

    public override string CurrentValue => Text;
    public override bool IsModified => Text.Trim() != (Item.Default ?? "").Trim();
    public override void ApplyDefault() => Text = Item.Default ?? "";
    public override void LoadFrom(MasterAnswers answers)
        => Text = answers.Values.TryGetValue(Id, out var v) ? v : Item.Default ?? "";
    public override void SaveTo(MasterAnswers answers) => answers.Values[Id] = Text;
}

public sealed partial class NumberItemViewModel : MasterItemViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _text = "";

    public NumberItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
        => _text = item.Default ?? "";

    public string Unit    => Item.Unit ?? "";
    public bool   HasUnit => !string.IsNullOrEmpty(Item.Unit);

    /// <summary>空または整数なら有効。</summary>
    public bool IsValid => string.IsNullOrWhiteSpace(Text) || int.TryParse(Text.Trim(), out _);

    partial void OnTextChanged(string value) => NotifyChanged();

    public override string CurrentValue => Text.Trim();
    public override bool IsModified => Text.Trim() != (Item.Default ?? "").Trim();
    public override void ApplyDefault() => Text = Item.Default ?? "";
    public override void LoadFrom(MasterAnswers answers)
        => Text = answers.Values.TryGetValue(Id, out var v) ? v : Item.Default ?? "";
    public override void SaveTo(MasterAnswers answers) => answers.Values[Id] = Text.Trim();
}

/// <summary>1 ファイルをドロップして配置する項目（例: ODT の setup.exe）。値 = 配置したファイル名。</summary>
public sealed partial class FileItemViewModel : MasterItemViewModel, IAssetDropTarget
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _text = "";

    [ObservableProperty] private string? _dropMessage;

    public FileItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
        => _text = item.Default ?? "";

    public MasterDropSpec? DropSpec => Item.Drop;
    public bool   CanDrop  => DropSpec is not null && Context.Import is not null && Context.CanEdit();
    public string DropHint => DropSpec?.Hint ?? "ここへファイルをドロップ";
    public bool   HasFile  => !string.IsNullOrWhiteSpace(Text);
    public string StatusText => HasFile
        ? $"配置済み: {DropSpec?.Module}/{DropSpec?.SubDir}/{Text}"
        : "未配置";

    partial void OnTextChanged(string value) => NotifyChanged();

    public override string CurrentValue => Text;
    public override bool IsModified => Text.Trim() != (Item.Default ?? "").Trim();
    public override void ApplyDefault() { Text = Item.Default ?? ""; DropMessage = null; }
    public override void LoadFrom(MasterAnswers answers)
    {
        Text = answers.Values.TryGetValue(Id, out var v) ? v : Item.Default ?? "";
        DropMessage = null;
    }
    public override void SaveTo(MasterAnswers answers) => answers.Values[Id] = Text;

    public async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (!CanDrop || DropSpec is null || Context.Import is null || paths.Count == 0) return;

        var result = await Context.Import(DropSpec, [paths[0]]);
        var entry  = result.Entries.FirstOrDefault();
        if (entry is not null) Text = entry.FileName;

        DropMessage = string.Join("  ", result.Errors.Concat(result.Skipped));
        if (string.IsNullOrEmpty(DropMessage)) DropMessage = entry is null ? null : $"✓ {result.TargetRelPath}/{entry.FileName} に配置しました";
    }

    [RelayCommand]
    private void Clear()
    {
        Text = "";
        DropMessage = "参照を外しました（ファイル自体は削除していません）";
    }
}

/// <summary>multi の選択肢 1 件。</summary>
public sealed partial class MultiOptionViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public MultiOptionViewModel(MasterChoice choice, Action onChanged)
    {
        Choice     = choice;
        _onChanged = onChanged;
    }

    public MasterChoice Choice { get; }
    public string Value => Choice.Value;
    public string Label => Choice.Label;

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _onChanged();
}

/// <summary>複数選択（チェックリスト）＋自由追加。</summary>
public sealed partial class MultiItemViewModel : MasterItemViewModel
{
    public ObservableCollection<MultiOptionViewModel> Options     { get; } = [];
    public ObservableCollection<string>               FreeEntries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFreeCommand))]
    private string _freeText = "";

    public bool AllowFree => Item.AllowFree;

    public MultiItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
    {
        foreach (var opt in item.Options ?? [])
            Options.Add(new MultiOptionViewModel(opt, NotifyChanged));
        ApplyDefault();
    }

    /// <summary>選択された値（選択肢 + 自由追加）。</summary>
    public List<string> SelectedValues
        => Options.Where(o => o.IsChecked).Select(o => o.Value).Concat(FreeEntries).ToList();

    public int SelectedCount => SelectedValues.Count;

    private string[] DefaultValues => (Item.Default ?? "")
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string CurrentValue => SelectedCount.ToString();

    public override bool IsModified
        => !SelectedValues.OrderBy(v => v, StringComparer.Ordinal)
               .SequenceEqual(DefaultValues.OrderBy(v => v, StringComparer.Ordinal));

    private bool CanAddFree() => AllowFree && !string.IsNullOrWhiteSpace(FreeText);

    [RelayCommand(CanExecute = nameof(CanAddFree))]
    private void AddFree()
    {
        var v = FreeText.Trim();
        if (v.Length == 0) return;
        if (Options.Any(o => o.Value.Equals(v, StringComparison.OrdinalIgnoreCase)))
        {
            // 既知の選択肢ならチェックに変換
            Options.First(o => o.Value.Equals(v, StringComparison.OrdinalIgnoreCase)).IsChecked = true;
        }
        else if (!FreeEntries.Contains(v, StringComparer.OrdinalIgnoreCase))
        {
            FreeEntries.Add(v);
            NotifyChanged();
        }
        FreeText = "";
    }

    [RelayCommand]
    private void RemoveFree(string? value)
    {
        if (value is null) return;
        if (FreeEntries.Remove(value)) NotifyChanged();
    }

    public override void ApplyDefault()
    {
        var defaults = DefaultValues;
        foreach (var o in Options) o.IsChecked = defaults.Contains(o.Value);
        FreeEntries.Clear();
        NotifyChanged();
    }

    public override void LoadFrom(MasterAnswers answers)
    {
        if (!answers.Multi.TryGetValue(Id, out var selected))
        {
            ApplyDefault();
            return;
        }
        foreach (var o in Options) o.IsChecked = selected.Contains(o.Value);
        FreeEntries.Clear();
        foreach (var v in selected.Where(v => Options.All(o => o.Value != v)))
            FreeEntries.Add(v);
        NotifyChanged();
    }

    public override void SaveTo(MasterAnswers answers) => answers.Multi[Id] = SelectedValues;
}

/// <summary>表形式の入力（DataTable + DataGrid）。列定義はテンプレートから。ドロップ枠があれば資材配置 → 行追加。</summary>
public sealed partial class TableItemViewModel : MasterItemViewModel, IAssetDropTarget
{
    public IReadOnlyList<MasterColumn> Columns { get; }

    [ObservableProperty] private DataTable _table = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteRowCommand))]
    private DataRowView? _selectedRow;

    [ObservableProperty] private string? _dropMessage;

    private readonly string _defaultSignature;

    public TableItemViewModel(MasterItem item, MasterItemContext context) : base(item, context)
    {
        Columns = item.Columns ?? [];
        _defaultSignature = Signature(item.DefaultRows ?? []);
        ApplyDefault();
    }

    // ── ドロップ ─────────────────────────────────────────────────
    public MasterDropSpec? DropSpec => Item.Drop;
    public bool   HasDrop  => DropSpec is not null;
    public bool   CanDrop  => DropSpec is not null && Context.Import is not null && Context.CanEdit();
    public string DropHint => DropSpec?.Hint ?? "ここへファイルをドロップ";

    public async Task AcceptDropAsync(IReadOnlyList<string> paths)
    {
        if (!CanDrop || DropSpec is null || Context.Import is null || paths.Count == 0) return;

        var spec   = DropSpec;
        var result = await Context.Import(spec, paths);
        var lines  = new List<string>();

        foreach (var e in result.Entries)
        {
            if (spec.Kind == MasterDropKinds.PrinterDriver && e.DriverNames.Count > 0)
            {
                foreach (var d in e.DriverNames)
                {
                    var row = Table.NewRow();
                    FillRow(row, null);
                    Set(row, spec.DriverColumn, d);
                    Set(row, spec.NameColumn, d);
                    Set(row, spec.DescriptionColumn, $"{e.FileName}\\");
                    Table.Rows.Add(row);
                }
                lines.Add($"{e.FileName}\\ → {e.Source}");
            }
            else
            {
                var row = Table.NewRow();
                FillRow(row, null);
                Set(row, spec.FileColumn, e.FileName);
                if (spec.Kind == MasterDropKinds.Installer)
                {
                    Set(row, spec.NameColumn, e.AppName);
                    Set(row, spec.TypeColumn, e.Type);
                    Set(row, spec.ArgsColumn, e.SilentArgs);
                    Set(row, spec.DescriptionColumn, e.Version);
                    lines.Add($"{e.FileName} → {e.AppName}（{e.Source}）");
                }
                else if (spec.Kind == MasterDropKinds.PrinterDriver)
                {
                    Set(row, spec.DescriptionColumn, e.FileName);
                    lines.Add($"{e.FileName} → {e.Source}");
                }
                else
                {
                    Set(row, spec.DescriptionColumn, System.IO.Path.GetFileNameWithoutExtension(e.FileName));
                    lines.Add($"{e.FileName} を配置しました");
                }
                Table.Rows.Add(row);
            }
        }

        lines.AddRange(result.Skipped.Select(s => "スキップ: " + s));
        lines.AddRange(result.Errors.Select(s => "エラー: " + s));
        DropMessage = lines.Count == 0 ? null : $"[{result.TargetRelPath}] " + string.Join("  ", lines);
        NotifyChanged();
    }

    private void Set(DataRow row, string? column, string value)
    {
        if (string.IsNullOrEmpty(column) || !Table.Columns.Contains(column)) return;
        if (string.IsNullOrEmpty(value)) return;
        row[column] = value;
    }

    // ── 列 ───────────────────────────────────────────────────────
    /// <summary>列ラベル（DataGrid の列ヘッダー差し替え用）。</summary>
    public string HeaderFor(string columnName)
        => Columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))?.Label ?? columnName;

    public IReadOnlyList<string>? OptionsFor(string columnName)
        => Columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))?.Options;

    public int RowCount => Table.Rows.Cast<DataRow>().Count(r => r.RowState != DataRowState.Deleted);

    public override string CurrentValue => RowCount.ToString();

    public override bool IsModified => Signature(CurrentRows()) != _defaultSignature;

    private DataTable NewTable()
    {
        var t = new DataTable();
        foreach (var c in Columns) t.Columns.Add(c.Name);
        return t;
    }

    private void Attach(DataTable t)
    {
        t.RowChanged    += (_, _) => NotifyChanged();
        t.RowDeleted    += (_, _) => NotifyChanged();
        t.ColumnChanged += (_, _) => NotifyChanged();
    }

    private void FillRow(DataRow row, IReadOnlyDictionary<string, string>? values)
    {
        foreach (var c in Columns)
        {
            var v = c.Default ?? "";
            if (values is not null)
                foreach (var (k, val) in values)
                    if (k.Equals(c.Name, StringComparison.OrdinalIgnoreCase)) { v = val ?? ""; break; }
            row[c.Name] = v;
        }
    }

    private List<Dictionary<string, string>> CurrentRows()
    {
        var list = new List<Dictionary<string, string>>();
        foreach (DataRow row in Table.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in Columns) dict[c.Name] = row[c.Name]?.ToString() ?? "";
            // 全列空の行は無視
            if (dict.Values.All(string.IsNullOrWhiteSpace)) continue;
            list.Add(dict);
        }
        return list;
    }

    /// <summary>行集合の比較用文字列（列順はテンプレート順、既定値で欠けを補う）。</summary>
    private string Signature(IEnumerable<Dictionary<string, string>> rows)
    {
        var parts = new List<string>();
        foreach (var r in rows)
        {
            var cells = Columns.Select(c =>
            {
                foreach (var (k, v) in r)
                    if (k.Equals(c.Name, StringComparison.OrdinalIgnoreCase)) return (v ?? "").Trim();
                return (c.Default ?? "").Trim();
            });
            var line = string.Join("", cells);
            if (line.Length == 0) continue;   // 全列空の行は無視
            parts.Add(line);
        }
        return string.Join("", parts);
    }

    public override void ApplyDefault()
    {
        var t = NewTable();
        foreach (var r in Item.DefaultRows ?? [])
        {
            var row = t.NewRow();
            FillRow(row, r);
            t.Rows.Add(row);
        }
        t.AcceptChanges();
        Attach(t);
        Table = t;
        SelectedRow = null;
        DropMessage = null;
        NotifyChanged();
    }

    public override void LoadFrom(MasterAnswers answers)
    {
        if (!answers.Tables.TryGetValue(Id, out var rows))
        {
            ApplyDefault();
            return;
        }
        var t = NewTable();
        foreach (var r in rows)
        {
            var row = t.NewRow();
            FillRow(row, r);
            t.Rows.Add(row);
        }
        t.AcceptChanges();
        Attach(t);
        Table = t;
        SelectedRow = null;
        DropMessage = null;
        NotifyChanged();
    }

    public override void SaveTo(MasterAnswers answers) => answers.Tables[Id] = CurrentRows();

    [RelayCommand]
    private void AddRow()
    {
        var row = Table.NewRow();
        FillRow(row, null);
        Table.Rows.Add(row);
    }

    private bool CanDeleteRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteRow))]
    private void DeleteRow()
    {
        SelectedRow?.Row.Delete();
        SelectedRow = null;
    }
}
