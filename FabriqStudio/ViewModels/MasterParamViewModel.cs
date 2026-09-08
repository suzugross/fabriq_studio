using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Helpers;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Models.Master;
using FabriqStudio.Services;
using FabriqStudio.Services.Gpo;
using FabriqStudio.Services.Master;
using FabriqStudio.Services.Master.Emitters;
using FabriqStudio.ViewModels.Master;
using FabriqStudio.Views;

namespace FabriqStudio.ViewModels;

/// <summary>
/// マスタ設計画面。
///   - テンプレート（JSON）から章・質問の VM を組み立てる
///   - 回答は profiles/&lt;マスタ名&gt;.master.json に保存（Save）
///   - 入力のたびに計画（MasterPlan）を再計算して右ペインにプレビュー
///   - 「プレビュー／生成」でモーダルダイアログを開き、そこで初めてディスクに書く
///
/// ロック / Dirty / 破棄確認は既存画面（IsLocked, IDirtyAwareViewModel）と同じ規約。
/// Dirty は「回答 JSON のスナップショット比較」で判定する（項目追加時に個別フラグを増やさない）。
/// </summary>
public partial class MasterParamViewModel : ObservableObject, IDirtyAwareViewModel
{
    // ─── IDirtyAwareViewModel ───────────────────────────────────────
    public bool HasUnsavedChanges => IsDirty;
    public string DirtyDescription => string.IsNullOrWhiteSpace(MasterName)
        ? "マスタ設計"
        : $"マスタ設計: {MasterName}";

    /// <summary>最後に読み込んだ／保存した回答に戻す（ディスクは触らない）。</summary>
    public void DiscardChanges()
    {
        ApplyAnswers(_current);
        TakeSnapshot();
    }

    private readonly IMasterTemplateService         _templateService;
    private readonly IMasterAnswersService          _answersService;
    private readonly IMasterProfileGeneratorService _generator;
    private readonly IMasterAssetService            _assetService;
    private readonly IOdtDownloadService            _odtDownload;
    private readonly IWorkspaceService              _workspace;
    private readonly IGpoCatalogService             _gpoCatalog;
    private readonly IAppAssocService               _appAssoc;
    private readonly IRegistryCollectionService     _registry;
    private readonly IMasterSheetService            _sheets;

    /// <summary>ODT のダウンロード実行中（同時に 1 つだけ）。</summary>
    [ObservableProperty] private bool _isOdtDownloading;

    /// <summary>項目 VM に渡す共通文脈（変更通知 / 編集可否 / 資材ドロップの配置処理）。</summary>
    private readonly MasterItemContext _itemContext;

    private MasterTemplate?          _template;
    private MasterWorkspaceSnapshot? _snapshot;

    /// <summary>現在編集中の回答（メタデータ: CreatedAt / LastGenerated 等の入れ物）。</summary>
    private MasterAnswers _current = new();

    /// <summary>Dirty 判定用: 最後に Load / Save した時点の回答 JSON。</summary>
    private string _snapshotJson = "";

    /// <summary>ロード中・リセット中は変更通知を無視する。</summary>
    private bool _suppressChanges;

    private readonly DispatcherTimer _previewTimer;

    // ─── 状態 ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportParameterSheetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportChecklistCommand))]
    private bool    _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLocked = true;

    public bool IsEditable => !IsLocked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    // ─── 章・質問 ─────────────────────────────────────────────────
    public ObservableCollection<MasterSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionPositionText))]
    [NotifyCanExecuteChangedFor(nameof(PrevSectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextSectionCommand))]
    private MasterSectionViewModel? _selectedSection;

    /// <summary>「章 3 / 13」のような位置表示。</summary>
    public string SectionPositionText
        => SelectedSection is null ? "" : $"章 {SelectedSection.Index} / {Sections.Count}";

    private bool CanPrevSection() => SelectedSection is not null && Sections.IndexOf(SelectedSection) > 0;

    [RelayCommand(CanExecute = nameof(CanPrevSection))]
    private void PrevSection()
    {
        var i = SelectedSection is null ? -1 : Sections.IndexOf(SelectedSection);
        if (i > 0) SelectedSection = Sections[i - 1];
    }

    private bool CanNextSection() => SelectedSection is not null && Sections.IndexOf(SelectedSection) < Sections.Count - 1;

    [RelayCommand(CanExecute = nameof(CanNextSection))]
    private void NextSection()
    {
        var i = SelectedSection is null ? -1 : Sections.IndexOf(SelectedSection);
        if (i >= 0 && i < Sections.Count - 1) SelectedSection = Sections[i + 1];
    }

    private readonly Dictionary<string, MasterItemViewModel> _itemsById = new(StringComparer.Ordinal);

    // ─── マスタ選択 ───────────────────────────────────────────────
    public ObservableCollection<string> ExistingMasters { get; } = [];

    [ObservableProperty] private string? _selectedExistingMaster;

    // ─── ヘッダー（案件メタ） ───────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMasterNameValid))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportParameterSheetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportChecklistCommand))]
    private string _masterName = "";

    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _versionText = "1";
    [ObservableProperty] private string _worker      = "";
    [ObservableProperty] private string _notes       = "";

    public bool IsMasterNameValid => MasterAnswers.IsValidMasterName(MasterName);

    // ─── プレビュー ───────────────────────────────────────────────
    public ObservableCollection<ProfileScriptEntry> PreviewRows        { get; } = [];
    public ObservableCollection<ProfileScriptEntry> PreviewSysprepRows { get; } = [];
    public ObservableCollection<PlanFileSummary>    PreviewFiles    { get; } = [];
    public ObservableCollection<PlanMessage>        PreviewMessages { get; } = [];
    public ObservableCollection<string>             ManualTasks     { get; } = [];

    [ObservableProperty] private string  _previewSummary = "";
    [ObservableProperty] private bool    _hasSysprepPreview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBadge))]
    private bool    _hasPlanErrors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBadge))]
    private int     _warningCount;

    [ObservableProperty] private string? _lastGeneratedText;

    /// <summary>右ペイン（生成プレビュー）を開いているか。既定は閉じる（画面の状態で、回答には含めない）。</summary>
    [ObservableProperty] private bool _isPreviewVisible;

    /// <summary>プレビューを閉じているときにハンドルへ出す要点（エラー / 警告の件数。無ければ空）。</summary>
    public string PreviewBadge => HasPlanErrors ? "⛔ エラー" : WarningCount > 0 ? $"⚠ {WarningCount}" : "";

    [RelayCommand]
    private void TogglePreview() => IsPreviewVisible = !IsPreviewVisible;

    public MasterParamViewModel(
        IMasterTemplateService         templateService,
        IMasterAnswersService          answersService,
        IMasterProfileGeneratorService generator,
        IMasterAssetService            assetService,
        IOdtDownloadService            odtDownload,
        IWorkspaceService              workspace,
        IGpoCatalogService             gpoCatalog,
        IAppAssocService               appAssoc,
        IRegistryCollectionService     registry,
        IMasterSheetService            sheets)
    {
        _templateService = templateService;
        _answersService  = answersService;
        _generator       = generator;
        _assetService    = assetService;
        _odtDownload     = odtDownload;
        _workspace       = workspace;
        _gpoCatalog      = gpoCatalog;
        _appAssoc        = appAssoc;
        _registry        = registry;
        _sheets          = sheets;
        _itemContext     = new MasterItemContext
        {
            OnChanged    = OnItemChanged,
            CanEdit      = () => IsEditable,
            Import       = ImportAssetsAsync,
            CanRunAction = CanRunAction,
            RunAction    = RunActionAsync,
            PickGpo      = PickGpoAsync,
            GpoCatalog   = gpoCatalog,
            PickRegistry       = PickRegistryAsync,
            RegistryDictionary = registry,
        };
        gpoCatalog.CatalogChanged += (_, _) => RunOnUi(OnGpoCatalogChanged);

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RebuildPreview();
        };

        workspace.WorkspaceChanged += (_, e) =>
        {
            if (e.NewPath is null) { Clear(); return; }
            _ = LoadAllAsync();
        };
        if (workspace.IsOpen)
            _ = LoadAllAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    //  ロード
    // ═══════════════════════════════════════════════════════════════

    private void Clear()
    {
        _suppressChanges = true;
        Sections.Clear();
        _itemsById.Clear();
        ExistingMasters.Clear();
        SelectedExistingMaster = null;
        _template = null;
        _snapshot = null;
        _current  = new MasterAnswers();
        MasterName = ""; ProjectName = ""; VersionText = "1"; Worker = ""; Notes = "";
        ClearPreview();
        IsDirty = false;
        _suppressChanges = false;
    }

    private async Task LoadAllAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            _template = await _templateService.LoadAsync();
            _snapshot = await _generator.LoadSnapshotAsync();
            await _gpoCatalog.EnsureLoadedAsync();   // 失敗しても LoadError に入るだけ（GPO 章は警告になる）

            BuildSections(_template);
            await RefreshExistingMastersAsync();
            NewMasterCore();
            RefreshActionStates();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshExistingMastersAsync()
    {
        var names = await _answersService.ListMasterNamesAsync();
        var keep  = SelectedExistingMaster;
        _suppressChanges = true;
        ExistingMasters.Clear();
        foreach (var n in names) ExistingMasters.Add(n);
        SelectedExistingMaster = keep is not null && names.Contains(keep) ? keep : null;
        _suppressChanges = false;
    }

    private void BuildSections(MasterTemplate template)
    {
        _suppressChanges = true;
        Sections.Clear();
        _itemsById.Clear();

        foreach (var section in template.Sections)
        {
            var items = new List<MasterItemViewModel>();
            foreach (var item in section.Items)
            {
                var vm = CreateItemVm(item);
                items.Add(vm);
                _itemsById[item.Id] = vm;
            }
            Sections.Add(new MasterSectionViewModel(section, items, Sections.Count + 1));
        }

        // visibleWhen の配線: 参照元の値が変わったら再評価
        foreach (var vm in _itemsById.Values)
        {
            if (vm.Item.VisibleWhen is null) continue;
            if (!_itemsById.TryGetValue(vm.Item.VisibleWhen.Item, out var source)) continue;
            var target = vm;
            source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MasterItemViewModel.CurrentValue) or nameof(MasterItemViewModel.IsVisible))
                    EvaluateVisibility(target);
            };
            EvaluateVisibility(vm);
        }

        SelectedSection = Sections.FirstOrDefault();
        _suppressChanges = false;
    }

    private MasterItemViewModel CreateItemVm(MasterItem item) => item.Type switch
    {
        MasterItemTypes.Bool      => new BoolItemViewModel(item, _itemContext),
        MasterItemTypes.Choice    => new ChoiceItemViewModel(item, _itemContext),
        MasterItemTypes.Number    => new NumberItemViewModel(item, _itemContext),
        MasterItemTypes.Multi     => new MultiItemViewModel(item, _itemContext),
        MasterItemTypes.Table     => new TableItemViewModel(item, _itemContext),
        MasterItemTypes.Info      => new InfoItemViewModel(item, _itemContext),
        MasterItemTypes.File      => new FileItemViewModel(item, _itemContext),
        MasterItemTypes.Action    => new ActionItemViewModel(item, _itemContext),
        MasterItemTypes.Gpo       => new GpoItemViewModel(item, _itemContext),
        MasterItemTypes.Registry  => new RegistryItemViewModel(item, _itemContext),
        MasterItemTypes.Multiline => new TextItemViewModel(item, _itemContext),
        _                         => new TextItemViewModel(item, _itemContext),
    };

    // ═══════════════════════════════════════════════════════════════
    //  action 項目（ODT のオフライン資材ダウンロード）
    // ═══════════════════════════════════════════════════════════════

    private const string OdtDownloadAction   = "odtDownload";
    private const string AppAssocEditAction  = "appassocEdit";

    private void RefreshActionStates()
    {
        foreach (var a in _itemsById.Values.OfType<ActionItemViewModel>())
            a.RefreshCanExecute();
    }

    partial void OnIsOdtDownloadingChanged(bool value) => RefreshActionStates();
    partial void OnIsLockedChanged(bool value)         => RefreshActionStates();

    private bool CanRunAction(string actionId)
    {
        if (_snapshot is null) return false;
        switch (actionId)
        {
            case OdtDownloadAction:
                // setup.exe が配置済みのときだけ実行できる
                return !IsOdtDownloading && _snapshot.GetModule("odt_config")?.HasFile("assets", "setup.exe") == true;
            case AppAssocEditAction:
                // ワークスペースの XML を書き換えるので編集モードのときだけ
                return IsEditable && _snapshot.HasModule("default_app_config");
            default:
                return false;
        }
    }

    private Task RunActionAsync(ActionItemViewModel item) => item.ActionId switch
    {
        OdtDownloadAction  => RunOdtDownloadAsync(item),
        AppAssocEditAction => RunAppAssocEditAsync(item),
        _                  => Task.CompletedTask,
    };

    /// <summary>
    /// 既定のアプリの関連付け XML（default_app_config/xml/AppAssoc.xml）を編集ダイアログで作成・編集する。
    /// 保存されたら file 項目（sp_appassoc）にファイル名を入れ、スナップショットを取り直してプレビューに反映する。
    /// </summary>
    private async Task RunAppAssocEditAsync(ActionItemViewModel item)
    {
        if (_snapshot is null || _workspace.RootPath is null) return;
        var module = _snapshot.GetModule("default_app_config");
        if (module is null) { item.Status = "モジュール default_app_config がワークスペースにありません。"; return; }

        await _appAssoc.EnsureLoadedAsync();
        var path  = Path.Combine(module.AbsPath, "xml", "AppAssoc.xml");
        var rel   = Path.GetRelativePath(_workspace.RootPath, path).Replace('/', '\\');
        var saved = AppAssocEditorDialog.Show(Application.Current?.MainWindow, _appAssoc, path, rel);
        if (!saved) return;

        if (_itemsById.TryGetValue("sp_appassoc", out var fileItem) && fileItem is FileItemViewModel f && string.IsNullOrWhiteSpace(f.Text))
            f.Text = "AppAssoc.xml";
        item.Status   = $"✓ {rel} に保存しました";
        StatusMessage = item.Status;

        try
        {
            _snapshot = await _generator.LoadSnapshotAsync();
            SchedulePreview();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"再読込エラー: {ex.Message}";
        }
        RefreshActionStates();
    }

    /// <summary>
    /// 現在の回答から ODT の configuration.xml を組み立て（既製 XML なら assets/custom のものを使い）、
    /// setup.exe /download を子プロセスで実行して製品フォルダ直下に Office\ を取得する。
    /// </summary>
    private async Task RunOdtDownloadAsync(ActionItemViewModel item)
    {
        if (_template is null || _snapshot is null || _workspace.RootPath is null) return;

        var module = _snapshot.GetModule("odt_config");
        if (module is null) { item.Status = "モジュール odt_config がワークスペースにありません。"; return; }

        var setupExe = Path.Combine(module.AbsPath, "assets", "setup.exe");
        if (!File.Exists(setupExe)) { item.Status = "odt_config/assets/setup.exe がありません。先に setup.exe をドロップしてください。"; return; }

        // 生成計画から XML を取る（既製 XML の場合は計画に無いので assets/custom を読む）
        string xml, folder;
        var plan = _generator.BuildPlan(_template, BuildAnswers(), _snapshot);
        var odt  = plan.TextFiles.FirstOrDefault(t =>
            t.RelPath.Contains("odt_config", StringComparison.OrdinalIgnoreCase) &&
            t.RelPath.EndsWith("configuration.xml", StringComparison.OrdinalIgnoreCase));
        if (odt is not null)
        {
            xml    = odt.Content;
            folder = Path.GetDirectoryName(odt.AbsPath)!;
        }
        else
        {
            var custom = Path.Combine(module.AbsPath, "assets", "custom", "configuration.xml");
            if (!File.Exists(custom))
            {
                item.Status = "製品を選択するか、既製の configuration.xml をドロップしてください。";
                return;
            }
            xml    = await File.ReadAllTextAsync(custom);
            folder = Path.GetDirectoryName(custom)!;
        }

        var rel = Path.GetRelativePath(_workspace.RootPath, folder).Replace('/', '\\');
        var confirm = MessageBox.Show(
            $"{rel}\\Office\\ に Office のオフライン資材をダウンロードします。\n\n" +
            "・この PC がインターネットに接続している必要があります\n" +
            "・数 GB を書き込み、数分〜十数分かかります\n" +
            "・ODT の小さなウィンドウが開きます（閉じると中止になります）\n\n続行しますか？",
            "オフライン資材のダウンロード",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        IsOdtDownloading = true;
        item.IsRunning   = true;
        ErrorMessage     = null;
        try
        {
            var progress = new Progress<string>(s => item.Status = s);
            var result   = await _odtDownload.DownloadAsync(setupExe, folder, xml, progress, CancellationToken.None);
            item.Status  = result.Message;
            if (result.Success)
                StatusMessage = $"✓ Office のオフライン資材を取得しました（{rel}\\Office\\Data\\{result.DataVersion}）";
            else
                ErrorMessage = result.Message;
        }
        catch (Exception ex)
        {
            item.Status  = $"エラー: {ex.Message}";
            ErrorMessage = item.Status;
        }
        finally
        {
            item.IsRunning   = false;
            IsOdtDownloading = false;
        }

        // Office\ の有無をプレビューの警告に反映
        try
        {
            _snapshot = await _generator.LoadSnapshotAsync();
            SchedulePreview();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"再読込エラー: {ex.Message}";
        }
        RefreshActionStates();
    }

    // ═══════════════════════════════════════════════════════════════
    //  gpo 項目（GPO 辞書からの選択）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>辞書ダイアログでポリシーを選ぶ（<paramref name="existing"/> は編集時の現在値）。キャンセルで null。</summary>
    private Task<GpoSelection?> PickGpoAsync(GpoSelection? existing)
    {
        if (!_gpoCatalog.IsLoaded)
        {
            MessageBox.Show(
                _gpoCatalog.IsLoading
                    ? "GPO 辞書（ADMX）を読み込み中です。少し待ってからもう一度押してください。"
                    : $"GPO 辞書（ADMX）を読み込めていません。\n{_gpoCatalog.LoadError}\n\n「GPO 辞書」画面で ADMX フォルダーを確認してください。",
                "GPO 辞書", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.FromResult<GpoSelection?>(null);
        }
        var result = GpoPickerDialog.Show(Application.Current?.MainWindow, _gpoCatalog, existing);
        return Task.FromResult(result);
    }

    // ═══════════════════════════════════════════════════════════════
    //  registry 項目（レジストリ辞書からの選択）
    // ═══════════════════════════════════════════════════════════════

    private Task<RegistryTemplateEntry?> PickRegistryAsync()
    {
        if (_registry.Entries.Count == 0)
        {
            MessageBox.Show(
                "レジストリ辞書が空です。「レジストリ辞書」画面でエントリを登録してから追加してください。",
                "レジストリ辞書", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.FromResult<RegistryTemplateEntry?>(null);
        }
        var entry = RegistryPickerWindow.Show(_registry.Entries, Application.Current?.MainWindow);
        return Task.FromResult(entry);
    }

    /// <summary>辞書の読み込み完了／再読込で、GPO 項目の表示（表示名・行数）とプレビューを更新する。</summary>
    private void OnGpoCatalogChanged()
    {
        foreach (var g in _itemsById.Values.OfType<GpoItemViewModel>()) g.RefreshFromCatalog();
        if (_template is not null && _snapshot is not null) SchedulePreview();
    }

    private static void RunOnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    /// <summary>
    /// ドロップされた資材をモジュールのフォルダへ配置する（項目 VM から呼ばれる）。
    /// 同名ファイルの上書きはダイアログで確認する。結果の要約をヘッダーのステータスに出す。
    /// </summary>
    private async Task<AssetDropResult> ImportAssetsAsync(MasterDropSpec spec, IReadOnlyList<string> paths)
    {
        ErrorMessage = null;
        var result = await _assetService.ImportAsync(spec, paths, name =>
            MessageBox.Show(
                $"「{name}」は既に存在します。\n上書きしますか？",
                "上書き確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);

        if (result.Errors.Count > 0)
            ErrorMessage = string.Join(" / ", result.Errors);
        else if (result.Entries.Count > 0)
            StatusMessage = $"✓ {result.Entries.Count} 件を {result.TargetRelPath}/ に配置しました";

        // 配置した資材をプレビューの存在チェックに反映させるため、スナップショットを取り直す
        if (result.Entries.Count > 0)
        {
            try
            {
                _snapshot = await _generator.LoadSnapshotAsync();
                SchedulePreview();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"再読込エラー: {ex.Message}";
            }
            RefreshActionStates();   // setup.exe が置かれたらダウンロードボタンが有効になる
        }

        return result;
    }

    private void EvaluateVisibility(MasterItemViewModel vm)
    {
        var cond = vm.Item.VisibleWhen;
        if (cond is null) { vm.IsVisible = true; return; }
        if (!_itemsById.TryGetValue(cond.Item, out var source)) { vm.IsVisible = true; return; }
        vm.IsVisible = source.IsVisible && cond.Values.Contains(source.CurrentValue);
    }

    // ═══════════════════════════════════════════════════════════════
    //  変更検知・プレビュー
    // ═══════════════════════════════════════════════════════════════

    private void OnItemChanged()
    {
        if (_suppressChanges) return;
        UpdateDirty();
        SchedulePreview();
    }

    partial void OnMasterNameChanged(string value)  => OnHeaderChanged();
    partial void OnProjectNameChanged(string value) => OnHeaderChanged();
    partial void OnVersionTextChanged(string value) => OnHeaderChanged();
    partial void OnWorkerChanged(string value)      => OnHeaderChanged();
    partial void OnNotesChanged(string value)       => OnHeaderChanged();

    private void OnHeaderChanged()
    {
        if (_suppressChanges) return;
        UpdateDirty();
        SchedulePreview();
    }

    private void UpdateDirty() => IsDirty = SerializeAnswers(BuildAnswers()) != _snapshotJson;

    private void TakeSnapshot()
    {
        _snapshotJson = SerializeAnswers(BuildAnswers());
        IsDirty = false;
    }

    private static readonly JsonSerializerOptions SnapshotJson = new() { WriteIndented = false };

    /// <summary>Dirty 比較用（更新日時など揺れるメタは除いて比較する）。</summary>
    private static string SerializeAnswers(MasterAnswers a)
    {
        var copy = new MasterAnswers
        {
            MasterName  = a.MasterName,
            ProjectName = a.ProjectName,
            Version     = a.Version,
            Worker      = a.Worker,
            Notes       = a.Notes,
            Values      = a.Values,
            Multi       = a.Multi,
            Tables      = a.Tables,
        };
        return JsonSerializer.Serialize(copy, SnapshotJson);
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void ClearPreview()
    {
        PreviewRows.Clear();
        PreviewSysprepRows.Clear();
        PreviewFiles.Clear();
        PreviewMessages.Clear();
        ManualTasks.Clear();
        PreviewSummary    = "";
        HasSysprepPreview = false;
        HasPlanErrors     = false;
        WarningCount      = 0;
    }

    /// <summary>現在の回答から計画を計算して右ペインへ反映する（ディスクは触らない）。</summary>
    private void RebuildPreview()
    {
        if (_template is null || _snapshot is null) return;

        MasterPlan plan;
        try
        {
            plan = _generator.BuildPlan(_template, BuildAnswers(), _snapshot);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"プレビュー計算エラー: {ex.Message}";
            return;
        }

        ClearPreview();

        var master = plan.Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Master);
        if (master is not null)
            foreach (var r in master.Rows) PreviewRows.Add(r);

        var sysprep = plan.Profiles.FirstOrDefault(p => p.IsSysprep);
        if (sysprep is not null)
        {
            foreach (var r in sysprep.Rows) PreviewSysprepRows.Add(r);
            HasSysprepPreview = true;
        }

        foreach (var f in plan.FileSummaries) PreviewFiles.Add(f);
        foreach (var m in plan.Messages.OrderByDescending(m => m.Severity)) PreviewMessages.Add(m);
        foreach (var t in plan.ManualTasks) ManualTasks.Add(t);

        var moduleRows = master?.Rows.Count(r => !r.IsSystemCommand) ?? 0;
        var regRows    = plan.RegistryOps.Sum(r => r.Rows.Count);
        var csvRows    = plan.CsvOps.Sum(c => c.Rows.Count);
        HasPlanErrors  = plan.HasErrors;
        WarningCount   = plan.Messages.Count(m => m.Severity == PlanSeverity.Warning);
        PreviewSummary = $"モジュール行 {moduleRows} / レジストリ {regRows} 件 / CSV 行 {csvRows} / 手動 {plan.ManualTasks.Count}";
    }

    // ═══════════════════════════════════════════════════════════════
    //  回答 ⇄ VM
    // ═══════════════════════════════════════════════════════════════

    private MasterAnswers BuildAnswers()
    {
        var a = new MasterAnswers
        {
            SchemaVersion   = _current.SchemaVersion,
            TemplateVersion = _template?.Version ?? 1,
            MasterName      = MasterName.Trim(),
            ProjectName     = ProjectName,
            Version         = VersionText,
            Worker          = Worker,
            Notes           = Notes,
            CreatedAt       = _current.CreatedAt,
            UpdatedAt       = _current.UpdatedAt,
            LastGenerated   = _current.LastGenerated,
            LastFiles       = _current.LastFiles,
        };
        foreach (var vm in _itemsById.Values) vm.SaveTo(a);
        return a;
    }

    private void ApplyAnswers(MasterAnswers a)
    {
        _suppressChanges = true;
        MasterName  = a.MasterName;
        ProjectName = a.ProjectName;
        VersionText = string.IsNullOrEmpty(a.Version) ? "1" : a.Version;
        Worker      = a.Worker;
        Notes       = a.Notes;
        foreach (var vm in _itemsById.Values) vm.LoadFrom(a);
        foreach (var vm in _itemsById.Values) EvaluateVisibility(vm);
        LastGeneratedText = string.IsNullOrEmpty(a.LastGenerated) ? null : $"最終生成: {a.LastGenerated}";
        _suppressChanges = false;
        SchedulePreview();
    }

    private void NewMasterCore()
    {
        _current = new MasterAnswers();
        _suppressChanges = true;
        MasterName = ""; ProjectName = ""; VersionText = "1"; Worker = ""; Notes = "";
        foreach (var vm in _itemsById.Values) vm.ApplyDefault();
        foreach (var vm in _itemsById.Values) EvaluateVisibility(vm);
        LastGeneratedText = null;
        SelectedExistingMaster = null;
        _suppressChanges = false;
        TakeSnapshot();
        StatusMessage = null;
        SchedulePreview();
    }

    // ═══════════════════════════════════════════════════════════════
    //  コマンド
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private void NewMaster()
    {
        if (!DirtyConfirmHelper.ConfirmDiscard(this)) return;
        NewMasterCore();
        IsLocked = false;
    }

    partial void OnSelectedExistingMasterChanged(string? value)
    {
        if (_suppressChanges || value is null) return;
        if (value == _current.MasterName) return;

        if (!DirtyConfirmHelper.ConfirmDiscard(this))
        {
            // ComboBox のハイライトがクリック先に張り付くのを避けるため、選択の巻き戻しは Dispatcher 経由で行う
            var previous = string.IsNullOrEmpty(_current.MasterName) ? null : _current.MasterName;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _suppressChanges = true;
                SelectedExistingMaster = previous;
                _suppressChanges = false;
            }), DispatcherPriority.Background);
            return;
        }

        _ = LoadMasterAsync(value);
    }

    private async Task LoadMasterAsync(string name)
    {
        ErrorMessage = null;
        try
        {
            var answers = await _answersService.LoadAsync(name);
            if (answers is null)
            {
                ErrorMessage = $"回答ファイルが見つかりません: {name}";
                return;
            }
            _current = answers;
            ApplyAnswers(answers);
            TakeSnapshot();
            StatusMessage = $"「{name}」を読み込みました";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    private bool CanSave() => IsDirty && !IsLocked && IsMasterNameValid;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await SaveAnswersAsync();
    }

    /// <summary>回答を JSON に保存し、スナップショットを更新する。成功時 true。</summary>
    private async Task<bool> SaveAnswersAsync()
    {
        ErrorMessage = null;
        try
        {
            var answers = BuildAnswers();
            await _answersService.SaveAsync(answers);
            _current = answers;
            TakeSnapshot();
            await RefreshExistingMastersAsync();
            _suppressChanges = true;
            SelectedExistingMaster = answers.MasterName;
            _suppressChanges = false;
            StatusMessage = $"✓ 回答を保存しました（{answers.MasterName}.master.json）";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存エラー: {ex.Message}";
            return false;
        }
    }

    private bool CanPreview() => IsMasterNameValid && !IsLoading;

    // ═══════════════════════════════════════════════════════════════
    //  帳票の出力（パラメータシート = Excel、チェックリスト = HTML）
    // ═══════════════════════════════════════════════════════════════

    private bool CanExportSheet() => IsMasterNameValid && !IsLoading && _template is not null && _snapshot is not null;

    [RelayCommand(CanExecute = nameof(CanExportSheet))]
    private Task ExportParameterSheetAsync()
        => ExportSheetAsync("パラメータシート", "Excel ブック (*.xlsx)|*.xlsx", ".xlsx", (doc, path) =>
        {
            _sheets.SaveParameterSheetXlsx(doc, path);
            return Task.CompletedTask;
        });

    [RelayCommand(CanExecute = nameof(CanExportSheet))]
    private Task ExportChecklistAsync()
        => ExportSheetAsync("チェックリスト", "HTML ファイル (*.html)|*.html", ".html",
            (doc, path) => File.WriteAllTextAsync(path, _sheets.ToChecklistHtml(doc), new UTF8Encoding(false)));

    /// <summary>現在の回答（未保存の編集を含む）から帳票を組み立て、保存先を訊いて書き、関連付けで開く。</summary>
    private async Task ExportSheetAsync(string kind, string filter, string ext, Func<SheetDocument, string, Task> write)
    {
        if (_template is null || _snapshot is null) return;
        ErrorMessage = null;
        try
        {
            var answers = BuildAnswers();
            var plan    = _generator.BuildPlan(_template, answers, _snapshot);
            await _appAssoc.EnsureLoadedAsync();   // 既定のアプリの分類辞書（帳票の関連付け表で使う）
            var doc     = _sheets.Build(_template, answers, plan);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title            = $"{kind}の保存",
                Filter           = filter,
                DefaultExt       = ext,
                FileName         = $"{answers.MasterName}_{kind}_{DateTime.Now:yyyyMMdd}{ext}",
                InitialDirectory = _workspace.RootPath ?? "",
            };
            var owner = Application.Current?.MainWindow;
            var ok = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (ok != true) return;

            await write(doc, dialog.FileName);
            StatusMessage = $"✓ {kind}を保存しました（{Path.GetFileName(dialog.FileName)}）";
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{kind}の出力に失敗: {ex.Message}";
        }
    }

    /// <summary>自動採番した hostlist.csv の管理番号を 4 章の項目へ記録する（次回から同じ行を置き換えるため）。</summary>
    private void RecordHostAdminId(MasterPlan plan)
    {
        if (string.IsNullOrEmpty(plan.HostAdminId)) return;
        if (!_itemsById.TryGetValue(BaseSettingsEmitter.AdminIdItemId, out var item) || item is not NumberItemViewModel number) return;
        if (!string.IsNullOrWhiteSpace(number.Text)) return;
        _suppressChanges = true;
        number.Text = plan.HostAdminId;
        _suppressChanges = false;
    }

    /// <summary>計画ダイアログを開く。生成が実行されたら回答に記録し、スナップショットを再読込する。</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        if (_template is null || _snapshot is null) return;

        // 回答は先に保存する（生成物と回答ファイルの対応を保つ）
        if (IsDirty || !_answersService.Exists(MasterName.Trim()))
        {
            if (!await SaveAnswersAsync()) return;
        }

        var plan = _generator.BuildPlan(_template, BuildAnswers(), _snapshot);
        var result = MasterPlanDialog.Show(plan, _generator, Application.Current.MainWindow);
        if (result is null) return;   // 閉じただけ

        if (result.Succeeded)
        {
            _current.LastGenerated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _current.LastFiles     = result.Written.ToList();
            RecordHostAdminId(plan);
            try
            {
                await _answersService.SaveAsync(BuildAnswers());
            }
            catch (Exception ex)
            {
                ErrorMessage = $"回答ファイルの更新に失敗: {ex.Message}";
            }
            LastGeneratedText = $"最終生成: {_current.LastGenerated}";
            StatusMessage = $"✓ 生成しました（{result.Written.Count} ファイル）";
        }
        else
        {
            ErrorMessage = result.Error ?? $"一部のファイルを書き込めませんでした（{result.Failed.Count} 件）";
        }

        // 生成後はワークスペースの状態（Segment 行・プロファイル）が変わるのでスナップショットを取り直す
        try
        {
            _snapshot = await _generator.LoadSnapshotAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"再読込エラー: {ex.Message}";
        }
        TakeSnapshot();
        RebuildPreview();
        RefreshActionStates();
        WeakReferenceMessenger.Default.Send(new WorkspaceDataUpdatedMessage("MasterParam"));
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (!DirtyConfirmHelper.ConfirmDiscard(this)) return;
        await LoadAllAsync();
    }
}
