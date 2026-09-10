using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Services.Collect;
using FabriqStudio.Services.Master;
using FabriqStudio.Views;
using FabriqStudio.Helpers;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 採取画面: この PC から採取して、現在のプロファイル（基本パラメータの実行プロファイル = 編集先）の
/// データフォルダへ配置する。そのプロファイルでモジュールが未使用なら CSV の取り込みと実行行の追加も行う。
/// 流れは「採取 → 一覧で選ぶ → 配置」。自動配置はしない。
/// </summary>
public sealed partial class CollectViewModel : ObservableObject, IDataSetDependentViewModel
{
    // モジュール名・CSV 名・実行スクリプト（fabriq 本体の module.csv に従う）
    private const string TaskbarModule    = "taskbar_config";      private const string TaskbarCsv    = "taskbar_list.csv";
    private const string StoreModule      = "storeapp_config";     private const string StoreCsv      = "storeapp_list.csv";
    private const string WingetModule     = "winget_install";      private const string WingetCsv     = "app_list.csv";
    private const string DriverModule     = "driver_config";       private const string DriverCsv     = "driver.csv";
    private const string DefaultAppModule = "default_app_config";  private const string DefaultAppCsv = "default_app_list.csv";
    private const string IconModule       = "desktop_icon_config";

    private readonly IWorkspaceService         _workspace;
    private readonly IDataSetContext           _dataSet;
    private readonly ICollectPlacementService  _place;
    private readonly IWingetSearchService      _winget;
    private readonly IStoreAppInventoryService _storeApps;
    private readonly IDriverExportService      _drivers;
    private readonly IDesktopIconLayoutService _icons;
    private readonly IAppAssocService          _appAssoc;

    // ── ダイアログ（テストで差し替え可能。既定は WPF のダイアログ）──
    public Func<string, string?, IReadOnlyList<string>?>             PickFiles    { get; set; } = DefaultPickFiles;
    public Func<PickListRequest, Task<IReadOnlyList<PickListItem>?>> PickFromList { get; set; }
        = req => PickListDialog.ShowAsync(Application.Current?.MainWindow, req);
    public Func<string, string, bool> Confirm { get; set; }
        = (message, title) => MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <summary>採取・配置の対象プロファイル（編集先）。null = 本体が選ばれていて採取できない。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfile))]
    private string? _profileName;

    /// <summary>いずれかの採取を実行中（同時に 2 つは動かさない）。</summary>
    [ObservableProperty] private bool _isBusy;

    public bool HasProfile => ProfileName is not null && _workspace.IsOpen;

    public ObservableCollection<CollectItemViewModel> Items { get; } = [];

    public CollectItemViewModel Taskbar     { get; }
    public CollectItemViewModel StoreApps   { get; }
    public CollectItemViewModel Winget      { get; }
    public CollectItemViewModel Drivers     { get; }
    public CollectItemViewModel DefaultApps { get; }
    public CollectItemViewModel IconLayout  { get; }

    public CollectViewModel(
        IWorkspaceService         workspace,
        IDataSetContext           dataSet,
        ICollectPlacementService  place,
        IWingetSearchService      winget,
        IStoreAppInventoryService storeApps,
        IDriverExportService      drivers,
        IDesktopIconLayoutService icons,
        IAppAssocService          appAssoc)
    {
        _workspace = workspace;
        _dataSet   = dataSet;
        _place     = place;
        _winget    = winget;
        _storeApps = storeApps;
        _drivers   = drivers;
        _icons     = icons;
        _appAssoc  = appAssoc;

        Taskbar     = Make("📌", "タスクバーのピン留め",       "ファイルを選ぶ…",     TaskbarAsync);
        StoreApps   = Make("🛍", "ストアアプリの削除",         "この PC から選ぶ…",   StoreAppsAsync);
        Winget      = Make("📦", "winget でインストール",      "検索して追加…",       WingetAsync);
        Drivers     = Make("🔌", "ドライバ",                   "この PC から採取",     DriversAsync);
        DefaultApps = Make("🔗", "既定のアプリ",               "この PC から採取",     DefaultAppsAsync);
        IconLayout  = Make("🖥", "デスクトップのアイコン配置", "この PC から採取",     IconLayoutAsync);
        foreach (var it in new[] { Taskbar, StoreApps, Winget, Drivers, DefaultApps, IconLayout }) Items.Add(it);

        dataSet.Changed              += (_, _) => Refresh();
        workspace.WorkspaceChanged   += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var name = _workspace.IsOpen ? _dataSet.Current : null;
        if (!string.Equals(name, ProfileName, StringComparison.Ordinal))
        {
            ProfileName = name;
            foreach (var it in Items) it.Clear();   // 別のプロファイルの結果を残さない
        }
        OnPropertyChanged(nameof(HasProfile));
        foreach (var it in Items) it.RefreshCanExecute();
    }

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var it in Items) it.RefreshCanExecute();
    }

    private CollectItemViewModel Make(string icon, string title, string button, Func<string, Task<CollectOutcome?>> body)
    {
        CollectItemViewModel? item = null;
        item = new CollectItemViewModel(icon, title, button, () => RunAsync(item!, body), () => HasProfile && !IsBusy);
        return item;
    }

    /// <summary>1 件の採取を実行する。対象プロファイルは開始時点の名前で固定する（途中で編集先が変わっても混ざらない）。</summary>
    private async Task RunAsync(CollectItemViewModel item, Func<string, Task<CollectOutcome?>> body)
    {
        var profile = ProfileName;
        if (profile is null || IsBusy) return;

        IsBusy      = true;
        item.IsBusy = true;
        item.Clear();
        try
        {
            var outcome = await body(profile);
            if (outcome is null)
            {
                item.Set("キャンセル", null, error: false);
                return;
            }
            item.Set(outcome.Summary, outcome.Detail, error: false);
            if (outcome.ProfileRowAdded)
                WeakReferenceMessenger.Default.Send(new WorkspaceDataUpdatedMessage("ProfileDetail"));
        }
        catch (Exception ex)
        {
            item.Set(ex.Message, null, error: true);
        }
        finally
        {
            item.IsBusy = false;
            IsBusy      = false;
        }
    }

    // ── タスクバー: エクスプローラーで .lnk / .exe を選んで行にする ──────────

    private async Task<CollectOutcome?> TaskbarAsync(string profile)
    {
        var files = PickFiles(FilePicker.LinkFilter, FilePicker.StartMenuPrograms);
        if (files is null || files.Count == 0) return null;

        var rows = files.Select(f => Row(
            ("Enabled", "1"),
            ("Order", ""),
            ("LinkPath", TaskbarLinkPath.ToPortable(f)),
            ("AppId", ""),
            ("Description", Path.GetFileNameWithoutExtension(f)),
            ("Segment", ""))).ToList();

        var r     = await _place.AppendRowsAsync(profile, TaskbarModule, TaskbarCsv, rows, uniqueColumn: "LinkPath", numberColumn: "Order", numberStep: 10);
        var added = await _place.EnsureProfileRowAsync(profile, TaskbarModule, "taskbar_config.ps1");
        return Outcome(r, TaskbarCsv, added);
    }

    // ── ストアアプリ: この PC の一覧から「消すもの」を選ぶ ──────────────────

    private async Task<CollectOutcome?> StoreAppsAsync(string profile)
    {
        var apps = await _storeApps.ListAsync();
        if (apps.Count == 0) return new CollectOutcome("削除できるストアアプリがありません", null, false);

        var existing = await ExistingAsync(profile, StoreModule, StoreCsv, "AppName");
        var items    = apps.Select(a => new PickListItem(a.Name, a.Publisher, a.Version, existing.Contains(a.Name)) { Tag = a }).ToList();
        var picked   = await PickFromList(new PickListRequest("削除するストアアプリ", items));
        if (picked is null || picked.Count == 0) return null;

        var rows = picked.Select(p => Row(
            ("No", ""),
            ("AppName", p.Name),
            ("Enabled", "1"),
            ("Description", p.Name),
            ("Segment", ""))).ToList();

        var r     = await _place.AppendRowsAsync(profile, StoreModule, StoreCsv, rows, uniqueColumn: "AppName", numberColumn: "No", numberStep: 10);
        var added = await _place.EnsureProfileRowAsync(profile, StoreModule, "storeapp_config.ps1");
        return Outcome(r, StoreCsv, added);
    }

    // ── winget: 検索して選ぶ ───────────────────────────────────────────────

    private async Task<CollectOutcome?> WingetAsync(string profile)
    {
        if (!await _winget.IsAvailableAsync())
            throw new InvalidOperationException("この PC に winget がありません");

        var existing = await ExistingAsync(profile, WingetModule, WingetCsv, "AppID");
        var picked   = await PickFromList(new PickListRequest(
            "winget で追加",
            Search: async q => (await _winget.SearchAsync(q))
                .Select(p => new PickListItem(p.Name, p.Id, p.Version, existing.Contains(p.Id)) { Tag = p })
                .ToList()));
        if (picked is null || picked.Count == 0) return null;

        var rows = picked.Select(p => Row(
            ("Enabled", "1"),
            ("AppID", p.Detail),
            ("Options", ""),
            ("Description", p.Name),
            ("Segment", ""))).ToList();

        var r     = await _place.AppendRowsAsync(profile, WingetModule, WingetCsv, rows, uniqueColumn: "AppID");
        var added = await _place.EnsureProfileRowAsync(profile, WingetModule, "winget_install.ps1");
        return Outcome(r, WingetCsv, added);
    }

    // ── ドライバ: dism /export-driver（昇格）→ driver/<モデル名>/ ───────────

    private async Task<CollectOutcome?> DriversAsync(string profile)
    {
        var model = DriverExportService.SafeModelName(_drivers.GetModelName());
        var dest  = _place.AssetPath(profile, DriverModule, $"driver/{model}");
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any()
            && !Confirm($"driver/{model}/ を作り直します。", "ドライバの採取"))
            return null;

        var res = await _drivers.ExportAsync(dest);
        if (res.Cancelled) return null;
        if (!res.Succeeded) throw new InvalidOperationException(res.Message);

        var materialized = await _place.EnsureCsvAsync(profile, DriverModule, DriverCsv);

        // 有効行（model 空 = 対象機で自動判定、または同じモデル名）が無ければ足す
        var table   = await _place.ReadCsvAsync(profile, DriverModule, DriverCsv);
        var covered = table.Rows.Cast<DataRow>().Any(r =>
            Cell(r, "Enabled") == "1" && (Cell(r, "model").Length == 0 || Cell(r, "model").Equals(model, StringComparison.OrdinalIgnoreCase)));
        if (!covered)
            await _place.AppendRowsAsync(profile, DriverModule, DriverCsv,
                [Row(("Enabled", "1"), ("Id", ""), ("model", ""), ("Segment", ""))], numberColumn: "Id", numberStep: 1);

        var added = await _place.EnsureProfileRowAsync(profile, DriverModule, "driver_import_config.ps1");
        return new CollectOutcome($"✓ driver/{model}/ に {res.Count} 件", Detail(null, materialized, added), added);
    }

    // ── 既定のアプリ: Dism エクスポート（昇格）→ xml/AppAssoc.xml ─────────────

    private async Task<CollectOutcome?> DefaultAppsAsync(string profile)
    {
        var temp = await _appAssoc.ExportFromThisPcAsync();
        if (temp is null) return null;

        try
        {
            var dest = _place.AssetPath(profile, DefaultAppModule, "xml/AppAssoc.xml");
            if (File.Exists(dest) && !Confirm("xml/AppAssoc.xml を上書きします。", "既定のアプリの採取")) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(temp, dest, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* 後始末の失敗は無視 */ }
        }

        var materialized = await _place.EnsureCsvAsync(profile, DefaultAppModule, DefaultAppCsv);
        var table   = await _place.ReadCsvAsync(profile, DefaultAppModule, DefaultAppCsv);
        var covered = table.Rows.Cast<DataRow>().Any(r => Cell(r, "XmlFile").Equals("AppAssoc.xml", StringComparison.OrdinalIgnoreCase));
        if (!covered)
            await _place.AppendRowsAsync(profile, DefaultAppModule, DefaultAppCsv,
                [Row(("Enabled", "1"), ("XmlFile", "AppAssoc.xml"), ("Description", "Default App Associations"), ("Segment", ""))]);

        var added = await _place.EnsureProfileRowAsync(profile, DefaultAppModule, "default_app_config.ps1");
        return new CollectOutcome("✓ xml/AppAssoc.xml", Detail(null, materialized, added), added);
    }

    // ── アイコン配置: reg export → backup/DesktopIcons_<日時>.reg ─────────────

    private async Task<CollectOutcome?> IconLayoutAsync(string profile)
    {
        var name = $"DesktopIcons_{DateTime.Now:yyyyMMdd_HHmmss}.reg";
        var dest = _place.AssetPath(profile, IconModule, $"backup/{name}");
        if (!await _icons.ExportAsync(dest))
            throw new InvalidOperationException("アイコン配置を取得できませんでした");

        var added = await _place.EnsureProfileRowAsync(profile, IconModule, "desktop_icon_restore.ps1");
        return new CollectOutcome($"✓ backup/{name}", Detail(null, false, added), added);
    }

    // ── 共通 ─────────────────────────────────────────────────────────────

    private sealed record CollectOutcome(string Summary, string? Detail, bool ProfileRowAdded);

    private static CollectOutcome Outcome(CsvAppendResult r, string csv, bool profileRowAdded)
    {
        var summary = r.Added > 0 ? $"✓ {r.Added} 件追加" : "追加なし（登録済み）";
        var parts   = new List<string> { csv };
        if (r.Skipped > 0) parts.Add($"登録済み {r.Skipped} 件");
        return new CollectOutcome(summary, Detail(parts, r.Materialized, profileRowAdded), profileRowAdded);
    }

    private static string? Detail(List<string>? parts, bool materialized, bool profileRowAdded)
    {
        parts ??= [];
        if (materialized)    parts.Add("本体の CSV を取り込み");
        if (profileRowAdded) parts.Add("プロファイルに追加（順序を確認）");
        return parts.Count == 0 ? null : string.Join(" ・ ", parts);
    }

    private async Task<HashSet<string>> ExistingAsync(string profile, string module, string csv, string column)
    {
        var table = await _place.ReadCsvAsync(profile, module, csv);
        var set   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!table.Columns.Contains(column)) return set;
        foreach (DataRow r in table.Rows)
        {
            var v = Cell(r, column);
            if (v.Length > 0) set.Add(v);
        }
        return set;
    }

    private static string Cell(DataRow r, string column)
        => r.Table.Columns.Contains(column) ? (r[column]?.ToString() ?? "").Trim() : "";

    private static IReadOnlyDictionary<string, string> Row(params (string Column, string Value)[] cells)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (c, v) in cells) d[c] = v;
        return d;
    }

    private static IReadOnlyList<string>? DefaultPickFiles(string filter, string? initialDir)
        => FilePicker.PickMany(filter, initialDir, "タスクバーにピン留めするファイル");
}

/// <summary>採取画面の 1 行（採取の種類）。</summary>
public sealed partial class CollectItemViewModel : ObservableObject
{
    private readonly Func<Task> _run;
    private readonly Func<bool> _canRun;

    public CollectItemViewModel(string icon, string title, string buttonText, Func<Task> run, Func<bool> canRun)
    {
        Icon       = icon;
        Title      = title;
        ButtonText = buttonText;
        _run       = run;
        _canRun    = canRun;
    }

    public string Icon       { get; }
    public string Title      { get; }
    public string ButtonText { get; }

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private bool    _isError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAsync() => _run();

    private bool CanRun() => !IsBusy && _canRun();

    public void RefreshCanExecute() => RunCommand.NotifyCanExecuteChanged();

    public void Clear()
    {
        Status  = null;
        Detail  = null;
        IsError = false;
    }

    public void Set(string status, string? detail, bool error)
    {
        Status  = status;
        Detail  = detail;
        IsError = error;
    }
}
