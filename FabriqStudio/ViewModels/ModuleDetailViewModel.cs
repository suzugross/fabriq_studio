using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Helpers;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Views;

namespace FabriqStudio.ViewModels;

/// <summary>
/// モジュール詳細表示／編集
///   - module.csv: メニュー名・カテゴリ等のメタ情報 + ロック/解除トグル + Dirty 検知 + 保存
///   - guide.txt: テキスト表示 + ロック/解除トグル + Dirty 検知 + 保存
///   - 汎用CSV: DataTable で動的表示 + DataTable 組み込みの RowChanged で Dirty 検知 + 保存
///
/// ロック機構:
///   IsLocked=true（初期値）→ module.csv / guide.txt TextBox / DataGrid が読み取り専用
///   IsLocked=false          → 編集可能
///
/// 保存:
///   CanExecute = (IsGuideDirty || HasCsvChanges || HasModuleCsvChanges) &amp;&amp; !IsLocked
/// </summary>
public partial class ModuleDetailViewModel : ObservableObject, IDirtyAwareViewModel
{
    // ─── IDirtyAwareViewModel ───────────────────────────────────────
    public bool HasUnsavedChanges => IsGuideDirty || HasCsvChanges || HasModuleCsvChanges;
    public string DirtyDescription => Module is not null
        ? $"モジュール: {(string.IsNullOrEmpty(Module.MenuName) ? Module.ModuleDir : Module.MenuName)}"
        : "モジュール詳細";

    /// <summary>
    /// 3 系統の編集すべてをロールバックする。
    /// Module は ModuleEdit のリストとインスタンス共有しているため、フィールドコピーで戻す。
    /// </summary>
    public void DiscardChanges()
    {
        // module.csv 行（メニュー名・カテゴリ等）— View の TextChanged が再発火するので一時抑制
        if (Module is not null && _originalModule is not null)
        {
            _suppressModuleCsvDirty = true;
            Module.MenuName = _originalModule.MenuName;
            Module.Category = _originalModule.Category;
            Module.Script   = _originalModule.Script;
            Module.Order    = _originalModule.Order;
            Module.Enabled  = _originalModule.Enabled;
            _suppressModuleCsvDirty = false;
        }
        HasModuleCsvChanges = false;

        // guide.txt — OriginalGuideText に戻すと IsGuideDirty が自動で false になる
        if (HasGuideText)
            GuideText = OriginalGuideText;

        // 汎用 CSV — RejectChanges が RowChanged を再発火するのでハンドラを一時解除
        if (HasConfigCsv)
        {
            ConfigCsvData.RowChanged -= OnCsvRowChanged;
            ConfigCsvData.RowDeleted -= OnCsvRowChanged;
            ConfigCsvData.RejectChanges();
            ConfigCsvData.RowChanged += OnCsvRowChanged;
            ConfigCsvData.RowDeleted += OnCsvRowChanged;
        }
        HasCsvChanges = false;
    }

    /// <summary>module.csv カラムのみを浅くコピーした破棄用スナップショットを返す。</summary>
    private static ModuleMasterEntry CloneModuleSnapshot(ModuleMasterEntry src) => new()
    {
        MenuName  = src.MenuName,
        Category  = src.Category,
        Script    = src.Script,
        Order     = src.Order,
        Enabled   = src.Enabled,
        ModuleDir = src.ModuleDir,
        Kind      = src.Kind,
    };

    private readonly IFileService                _fileService;
    private readonly ICsvService                 _csvService;
    private readonly IRegistryCollectionService   _registryCollection;
    private readonly ICryptoService               _crypto;
    private readonly IModulePresetService         _presetService;
    private readonly IModuleDataResolver          _resolver;
    private readonly IProfileDataService          _profileData;

    // ─── モジュール情報 ───────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInExplorerCommand))]
    private ModuleMasterEntry? _module;

    /// <summary>module.csv 編集の破棄時に復元するためのスナップショット（Load 時に取得）。</summary>
    private ModuleMasterEntry? _originalModule;

    /// <summary>現在のモジュールがレジストリ設定モジュール（reg_hklm_config / reg_hkcu_config）か。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromCollectionCommand))]
    private bool _isRegistryModule;

    /// <summary>Picker 起動時に渡す対象 Hive（"HKLM" or "HKCU"）。非レジストリモジュールでは null。</summary>
    private string? _detectedHive;

    // ─── ロック ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFromCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSampleRowsCommand))]
    private bool _isLocked = true;

    // ─── guide.txt ───────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuideDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _guideText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuideDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _originalGuideText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasGuideText;

    /// <summary>guide.txt が元の内容から変更されているか。</summary>
    public bool IsGuideDirty =>
        HasGuideText && GuideText != OriginalGuideText;

    // ─── 汎用CSV ─────────────────────────────────────────────────
    [ObservableProperty] private DataTable _configCsvData = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFromCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSampleRowsCommand))]
    private bool _hasConfigCsv;
    [ObservableProperty] private string?   _configCsvFileName;

    /// <summary>module.csv 以外の CSV ファイル名一覧（View のドロップダウン用）。</summary>
    [ObservableProperty] private ObservableCollection<string> _configCsvFiles = [];

    /// <summary>現在選択中の CSV ファイル名。変更時に LoadSelectedCsvAsync が発火する。</summary>
    [ObservableProperty] private string? _selectedConfigCsvFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasCsvChanges;

    /// <summary>
    /// 現在表示中の CSV に対して適用されるプリセット辞書（列名 → 候補値リスト）。
    /// preset.csv が無いモジュールでは空の辞書。View 側の AutoGeneratingColumn で参照する。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnPresets { get; private set; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    // ─── module.csv メタ情報 ──────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasModuleCsvChanges;

    /// <summary>Load 中の TextChanged による誤検知を抑制するフラグ。</summary>
    private bool _suppressModuleCsvDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    private System.Data.DataRowView? _selectedCsvRow;

    // ─── 状態 ────────────────────────────────────────────────────
    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private string? _errorMessage;

    // ── PDF（profiles/<名>/modules/<module>）による上書き ────────
    // この画面は本体側を編集する。上書きしているプロファイルの実行には反映されないので注意を出す。

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverrides))]
    [NotifyPropertyChangedFor(nameof(OverrideSummary))]
    private IReadOnlyList<ModuleOverride> _overrides = [];

    public bool   HasOverrides    => Overrides.Count > 0;
    public string OverrideSummary => ModuleOverrideText.Summary(Overrides);

    /// <summary>表示中の CSV が PDF 側で上書きされているときの注記（無ければ null）。</summary>
    [ObservableProperty] private string? _csvOverrideNote;

    // ── 編集先データセット（PDF 文脈）────────────────────────────
    // null = 本体（Advanced のモジュール編集）。プロファイル名 = そのプロファイルの ⚙ から開いた
    //（profiles/<名>/modules/<module>/ を編集する）。

    /// <summary>編集先のデータセット（プロファイル名）。null なら本体。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileContext))]
    [NotifyPropertyChangedFor(nameof(DataSetLabel))]
    private string? _dataSet;

    public bool   IsProfileContext => !string.IsNullOrEmpty(DataSet);
    public string DataSetLabel     => IsProfileContext
        ? $"編集先: プロファイル {DataSet}（profiles/{DataSet}/modules/）"
        : "編集先: 本体（既定値）";

    /// <summary>表示中の CSV が PDF に無く、本体の内容を読み取り専用で見せている状態。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCsvCommand))]
    private bool _isFallbackView;

    /// <summary>フォールバック表示の説明。</summary>
    [ObservableProperty] private string? _fallbackNote;

    /// <summary>表示中の CSV の採用元（PDF / 本体）。本体文脈では空。</summary>
    [ObservableProperty] private string _csvSourceLabel = "";

    /// <summary>取り込み直後の案内（サンプル行の整理を促す）。</summary>
    [ObservableProperty] private string? _importedNote;

    /// <summary>保存先。PDF 文脈では ResolveWrite の結果（読み取り元と違うことがある）。</summary>
    private string? _csvSavePath;

    /// <summary>「フォルダを開く」で開く場所（PDF 文脈で PDF 側のモジュールフォルダがあればそちら）。</summary>
    private string? _explorerDir;

    // ファイルパス（保存時に使用）
    private string? _guidePath;
    private string? _csvFilePath;
    private string? _moduleDir;

    /// <summary>初期ロード中の CSV 切り替え発火を抑制するフラグ。</summary>
    private bool _suppressCsvSwitch;

    public ModuleDetailViewModel(
        IFileService              fileService,
        ICsvService               csvService,
        IRegistryCollectionService registryCollection,
        ICryptoService             crypto,
        IModulePresetService      presetService,
        IModuleDataResolver       resolver,
        IProfileDataService       profileData)
    {
        _fileService        = fileService;
        _csvService         = csvService;
        _registryCollection = registryCollection;
        _crypto             = crypto;
        _presetService      = presetService;
        _resolver           = resolver;
        _profileData        = profileData;
    }

    /// <summary>View の TextChanged から呼ばれ、module.csv 変更フラグを立てる。</summary>
    public void MarkModuleCsvDirty()
    {
        if (!_suppressModuleCsvDirty)
            HasModuleCsvChanges = true;
    }

    /// <summary>選択されたモジュールを本体文脈で読み込む。</summary>
    public void Load(ModuleMasterEntry module) => Load(module, null);

    /// <summary>選択されたモジュールを読み込む。<paramref name="dataSet"/> はプロファイル名（PDF 文脈）、null なら本体。</summary>
    public void Load(ModuleMasterEntry module, string? dataSet)
    {
        DataSet = string.IsNullOrWhiteSpace(dataSet) ? null : dataSet.Trim();
        _suppressModuleCsvDirty = true;
        Module     = module;
        _originalModule = CloneModuleSnapshot(module);
        IsLocked   = true;
        SaveStatus = null;
        SaveError  = null;
        HasModuleCsvChanges = false;

        // レジストリモジュール判定
        var dir = module.ModuleDir;
        if (dir.Contains("reg_hklm_config", StringComparison.OrdinalIgnoreCase))
        {
            IsRegistryModule = true;
            _detectedHive    = "HKLM";
        }
        else if (dir.Contains("reg_hkcu_config", StringComparison.OrdinalIgnoreCase))
        {
            IsRegistryModule = true;
            _detectedHive    = "HKCU";
        }
        else
        {
            IsRegistryModule = false;
            _detectedHive    = null;
        }

        _ = LoadFilesAsync(module);
    }

    private async Task LoadFilesAsync(ModuleMasterEntry module)
    {
        _suppressCsvSwitch = true;      // CSV 切り替えハンドラを抑制
        IsLoading          = true;
        ErrorMessage       = null;

        DetachCsvHandlers();
        GuideText             = null;
        OriginalGuideText     = null;
        HasGuideText          = false;
        ColumnPresets         = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        ConfigCsvData         = new DataTable();
        HasConfigCsv          = false;
        HasCsvChanges         = false;
        ConfigCsvFileName     = null;
        SelectedConfigCsvFile = null;
        ConfigCsvFiles.Clear();
        _guidePath            = null;
        _csvFilePath          = null;
        _csvSavePath          = null;
        _moduleDir            = null;
        _explorerDir          = null;
        Overrides             = [];
        CsvOverrideNote       = null;
        IsFallbackView        = false;
        FallbackNote          = null;
        CsvSourceLabel        = "";
        ImportedNote          = null;

        try
        {
            // 本体側のモジュールフォルダ。guide.txt / module.csv / preset.csv はフレームワーク資産なので常に本体を見る
            var moduleRel = _resolver.ModuleRelPath(module.Kind, module.ModuleDir, "");
            var moduleDir = _resolver.ResolveRead(moduleRel, null).AbsPath;

            // ── guide.txt ──────────────────────────────────────────
            _guidePath = Path.Combine(moduleDir, "guide.txt");
            var guideText = await _fileService.ReadTextAsync(_guidePath);
            GuideText         = guideText;
            OriginalGuideText = guideText;
            HasGuideText      = guideText is not null;

            // ── module.csv 以外の CSV 一覧を取得し、先頭を選択 ──────
            _moduleDir = moduleDir;

            // PDF 文脈: 「フォルダを開く」は PDF 側のモジュールフォルダがあればそちらを開く
            var pdfModuleDir = IsProfileContext ? _resolver.ResolveWrite(moduleRel, DataSet).AbsPath : null;
            _explorerDir = pdfModuleDir is not null && Directory.Exists(pdfModuleDir) ? pdfModuleDir : moduleDir;

            // PDF 側の上書き有無（本体側の編集画面でだけ注意喚起する）
            Overrides = IsProfileContext ? [] : _resolver.FindOverrides(module.ModuleDir);

            if (Directory.Exists(moduleDir))
            {
                // カーネルと同じ合成一覧（PDF 文脈では PDF 優先、reg_* はモジュール単位）
                var csvNames = _resolver.EnumerateCsvs(module.ModuleDir, DataSet)
                    .Select(e => e.Name)
                    .ToList();

                foreach (var name in csvNames)
                    ConfigCsvFiles.Add(name);

                // 先頭を選択して CSV をロード
                // （_suppressCsvSwitch で partial method の二重発火を防いでいるため直接 await する）
                SelectedConfigCsvFile = csvNames.FirstOrDefault();
                await LoadSelectedCsvAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            // Load() で立てた抑制フラグを非同期ロード完了後に解除。
            // View の DataTemplate 生成・バインディング初期評価による
            // TextChanged の誤検知がここまでに完了していることを保証する。
            _suppressCsvSwitch      = false;   // CSV 切り替えハンドラを再開
            _suppressModuleCsvDirty = false;
            HasModuleCsvChanges = false;

            // 非 Observable な _moduleDir を参照する CanExecute を再評価させる
            OpenInExplorerCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnCsvRowChanged(object sender, DataRowChangeEventArgs e)
        => HasCsvChanges = true;

    // ── CSV 切り替え ─────────────────────────────────────────────

    /// <summary>
    /// SelectedConfigCsvFile が変更されたときに呼ばれる。
    /// 初期ロード中は _suppressCsvSwitch で抑制される。
    /// </summary>
    partial void OnSelectedConfigCsvFileChanged(string? value)
    {
        if (!_suppressCsvSwitch)
            _ = LoadSelectedCsvAsync();
    }

    /// <summary>現在の ConfigCsvData から RowChanged/RowDeleted ハンドラを解除する。</summary>
    private void DetachCsvHandlers()
    {
        ConfigCsvData.RowChanged -= OnCsvRowChanged;
        ConfigCsvData.RowDeleted -= OnCsvRowChanged;
    }

    /// <summary>
    /// SelectedConfigCsvFile に対応する CSV を読み込み、ConfigCsvData を差し替える。
    /// 旧 DataTable のイベントハンドラ解除 → 新 DataTable のロード → ハンドラ登録 を行う。
    /// </summary>
    private async Task LoadSelectedCsvAsync()
    {
        DetachCsvHandlers();
        ConfigCsvData     = new DataTable();
        HasConfigCsv      = false;
        HasCsvChanges     = false;
        ConfigCsvFileName = null;
        CsvOverrideNote   = null;
        _csvFilePath      = null;
        _csvSavePath      = null;
        IsFallbackView    = false;
        FallbackNote      = null;
        CsvSourceLabel    = "";
        ImportedNote      = null;
        ColumnPresets     = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        if (SelectedConfigCsvFile is null || _moduleDir is null || Module is null) return;

        try
        {
            // 読み取り元と保存先を解決する（PDF 文脈では PDF 優先。無ければ本体を読み取り専用で見せる）
            var csvRel   = _resolver.ModuleRelPath(Module.Kind, Module.ModuleDir, SelectedConfigCsvFile);
            var read     = _resolver.ResolveRead(csvRel, DataSet);
            var csvFile  = read.AbsPath;
            _csvFilePath = csvFile;
            _csvSavePath = IsProfileContext ? _resolver.ResolveWrite(csvRel, DataSet).AbsPath : csvFile;
            ApplyCsvSource(read.Source, SelectedConfigCsvFile);

            var table         = await _fileService.ReadCsvAsDataTableAsync(csvFile);
            HasConfigCsv      = table.Columns.Count > 0;
            ConfigCsvFileName = SelectedConfigCsvFile;
            CsvOverrideNote   = IsProfileContext ? null : ModuleOverrideText.CsvNote(Overrides, SelectedConfigCsvFile);

            // preset.csv を同期ロード。ConfigCsvData 代入（= DataGrid 再生成）より前に
            // 更新しておくことで、AutoGeneratingColumn 発火時に最新辞書が参照できる。
            ColumnPresets = await _presetService.LoadAsync(_moduleDir);

            // AcceptChanges を先に呼び初期状態をクリーンにしてからイベント購読する。
            // 逆順だと AcceptChanges が RowChanged を発火し HasCsvChanges が即 true になる。
            table.AcceptChanges();
            table.RowChanged += OnCsvRowChanged;
            table.RowDeleted += OnCsvRowChanged;
            ConfigCsvData = table;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"CSV 読み込みエラー: {ex.Message}";
        }
    }

    // ── ロック切り替え ────────────────────────────────────────────
    [RelayCommand]
    private void ToggleLock()
    {
        if (IsFallbackView) return;   // 本体のフォールバック表示は読み取り専用（取り込んでから編集する）
        IsLocked = !IsLocked;
    }

    // ── CSV 行追加 ────────────────────────────────────────────────
    private bool CanAddCsvRow() => HasConfigCsv && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanAddCsvRow))]
    private void AddCsvRow()
    {
        var row = ConfigCsvData.NewRow();
        foreach (DataColumn col in ConfigCsvData.Columns)
            row[col] = "";
        ConfigCsvData.Rows.Add(row);
    }

    // ── CSV 行削除 ────────────────────────────────────────────────
    private bool CanDeleteCsvRow() => SelectedCsvRow is not null && HasConfigCsv && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanDeleteCsvRow))]
    private void DeleteCsvRow()
    {
        if (SelectedCsvRow is null) return;
        SelectedCsvRow.Row.Delete();
        SelectedCsvRow = null;
    }

    // ── 保存コマンド ──────────────────────────────────────────────
    private bool CanSave() => (IsGuideDirty || HasCsvChanges || HasModuleCsvChanges) && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        SaveError  = null;
        SaveStatus = null;

        try
        {
            // guide.txt 保存
            if (IsGuideDirty && _guidePath is not null && GuideText is not null)
            {
                await _fileService.WriteTextAsync(_guidePath, GuideText);
                OriginalGuideText = GuideText;   // スナップショット更新 → ハイライト解除
            }

            // module.csv 保存（フレームワーク資産なので常に本体側）
            if (HasModuleCsvChanges && Module is not null)
            {
                var relativePath = _resolver.ModuleRelPath(Module.Kind, Module.ModuleDir, "module.csv");
                await _csvService.WriteAsync(relativePath, new[] { Module });
                HasModuleCsvChanges = false;
            }

            // 汎用CSV 保存（PDF 文脈では profiles/<名>/modules/<module>/ へ。フォルダは保存の瞬間だけ作る）
            var savePath = _csvSavePath ?? _csvFilePath;
            if (HasCsvChanges && savePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                await _fileService.WriteCsvFromDataTableAsync(savePath, ConfigCsvData);
                ConfigCsvData.AcceptChanges();   // RowState リセット → Dirty 解除
                HasCsvChanges = false;
                _csvFilePath  = savePath;
                if (IsProfileContext) ApplyCsvSource(ModuleDataSource.Profile, ConfigCsvFileName);
            }

            SaveStatus = "✓ 保存しました";
            WeakReferenceMessenger.Default.Send(new WorkspaceDataUpdatedMessage("ModuleDetail"));
        }
        catch (Exception ex)
        {
            SaveError = $"保存エラー: {ex.Message}";
        }
    }

    // ── PDF 文脈: 採用元の反映 / 取り込んで編集 ─────────────────

    /// <summary>採用元に応じて、フォールバック表示（読み取り専用 + 取り込み導線）と採用元ラベルを更新する。</summary>
    private void ApplyCsvSource(ModuleDataSource source, string? csvName)
    {
        IsFallbackView = IsProfileContext && source == ModuleDataSource.Fallback;
        CsvSourceLabel = !IsProfileContext ? ""
                       : IsFallbackView    ? "採用元: 本体（未取り込み）"
                                           : "採用元: データフォルダ（PDF）";
        FallbackNote = IsFallbackView
            ? $"{csvName} はまだプロファイル {DataSet} のデータフォルダにありません。本体の内容を読み取り専用で表示しています。"
              + "編集するには取り込んでください（本体からそのままコピーするので、実行結果は変わりません）。"
            : null;
        if (IsFallbackView) IsLocked = true;
    }

    private bool CanImportCsv() => IsFallbackView && IsProfileContext && Module is not null && SelectedConfigCsvFile is not null;

    [RelayCommand(CanExecute = nameof(CanImportCsv))]
    private async Task ImportCsvAsync()
    {
        if (DataSet is null || Module is null || SelectedConfigCsvFile is null) return;
        SaveError = null;
        try
        {
            var name   = SelectedConfigCsvFile;
            var copied = await _profileData.MaterializeCsvAsync(DataSet, Module.ModuleDir, name);

            // 一覧を取り直す（reg_* はモジュール単位で PDF に切り替わるので、本体側の名前が消えることがある）
            _suppressCsvSwitch = true;
            ConfigCsvFiles.Clear();
            foreach (var e in _resolver.EnumerateCsvs(Module.ModuleDir, DataSet)) ConfigCsvFiles.Add(e.Name);
            SelectedConfigCsvFile = ConfigCsvFiles.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))
                                    ?? ConfigCsvFiles.FirstOrDefault();
            _suppressCsvSwitch = false;
            await LoadSelectedCsvAsync();

            IsLocked     = false;   // 編集したくて取り込んだので開く
            ImportedNote = $"本体から取り込みました（{string.Join(", ", copied)}）。"
                         + "案件に不要なサンプル行（無効行・Description に Sample／サンプルを含む行）は「サンプル行を削除」で落とせます。";
        }
        catch (Exception ex)
        {
            SaveError = $"取り込みエラー: {ex.Message}";
        }
    }

    // ── サンプル行の整理（取り込み直後の導線）────────────────────
    private bool CanRemoveSampleRows() => HasConfigCsv && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanRemoveSampleRows))]
    private void RemoveSampleRows()
    {
        var targets = ConfigCsvData.Rows.Cast<DataRow>()
            .Where(r => r.RowState != DataRowState.Deleted && IsSampleRow(r))
            .ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("サンプル行（Enabled=0、または Description に Sample／サンプルを含む行）はありません。",
                "サンプル行を削除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"{targets.Count} 行を削除します（無効行・Description に Sample／サンプルを含む行）。よろしいですか？",
                "サンプル行を削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var r in targets) r.Delete();   // RowDeleted → HasCsvChanges
        ImportedNote = null;
    }

    private static bool IsSampleRow(DataRow row)
    {
        var t = row.Table;
        string Cell(string col) => t.Columns.Contains(col) ? (row[col]?.ToString() ?? "").Trim() : "";
        if (t.Columns.Contains("Enabled") && Cell("Enabled") == "0") return true;
        var desc = Cell("Description");
        return desc.Contains("sample", StringComparison.OrdinalIgnoreCase) || desc.Contains("サンプル", StringComparison.Ordinal);
    }

    // ── 辞書から追加 ─────────────────────────────────────────────
    private bool CanAddFromCollection() => HasConfigCsv && !IsLocked && IsRegistryModule;

    [RelayCommand(CanExecute = nameof(CanAddFromCollection))]
    private void AddFromCollection()
    {
        var entry = RegistryPickerWindow.Show(
            _registryCollection.Entries,
            Application.Current.MainWindow,
            _detectedHive);

        if (entry is null) return;

        var row = ConfigCsvData.NewRow();
        SetIfColumnExists(row, "Enabled",      "1");
        SetIfColumnExists(row, "AdminID",      NextAdminId().ToString());
        SetIfColumnExists(row, "SettingTitle",  entry.Title);
        SetIfColumnExists(row, "KeyPath",       entry.KeyPath);
        SetIfColumnExists(row, "KeyName",       entry.KeyName);
        SetIfColumnExists(row, "Type",          entry.Type);
        SetIfColumnExists(row, "Value",         entry.Value);
        ConfigCsvData.Rows.Add(row);
    }

    private static void SetIfColumnExists(DataRow row, string columnName, string value)
    {
        if (row.Table.Columns.Contains(columnName))
            row[columnName] = value;
    }

    private int NextAdminId()
    {
        if (!ConfigCsvData.Columns.Contains("AdminID")) return 1;

        var max = 0;
        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (int.TryParse(row["AdminID"]?.ToString(), out var id) && id > max)
                max = id;
        }
        return max + 1;
    }

    // ── セル暗号化・復号（View の code-behind から呼び出し）────────
    /// <summary>指定セルの値を暗号化する。戻り値 = エラーメッセージ（成功時 null）。</summary>
    public string? EncryptCell(System.Data.DataRowView row, string columnName)
    {
        if (!_crypto.HasPassphrase)
            return "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";

        var value = row[columnName]?.ToString() ?? "";
        if (string.IsNullOrEmpty(value))
            return "空のセルは暗号化できません。";
        if (value.StartsWith("ENC:", StringComparison.Ordinal))
            return "このセルは既に暗号化されています。";

        row[columnName] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
        return null;
    }

    /// <summary>指定セルの値を復号する。戻り値 = エラーメッセージ（成功時 null）。</summary>
    public string? DecryptCell(System.Data.DataRowView row, string columnName)
    {
        if (!_crypto.HasPassphrase)
            return "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";

        var value = row[columnName]?.ToString() ?? "";
        if (!value.StartsWith("ENC:", StringComparison.Ordinal))
            return "このセルは暗号化されていません（ENC: プレフィクスがありません）。";

        try
        {
            row[columnName] = _crypto.Decrypt(value, _crypto.MasterPassphrase!);
            return null;
        }
        catch (Exception ex)
        {
            return $"復号に失敗しました。パスフレーズが正しいか確認してください。\n{ex.Message}";
        }
    }

    // ── 列一括暗号化・復号 ─────────────────────────────────────────

    /// <summary>指定列の全行を暗号化する。</summary>
    public BatchCryptoResult EncryptColumn(string columnName)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);
        if (!CryptoHelper.IsEncryptableColumn(columnName))
            return new BatchCryptoResult(0, 0, [$"列 '{columnName}' は暗号化対象外です。"]);

        int processed = 0, skipped = 0;
        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var value = row[columnName]?.ToString() ?? "";
            if (string.IsNullOrEmpty(value) || value.StartsWith("ENC:", StringComparison.Ordinal))
            { skipped++; continue; }
            row[columnName] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
            processed++;
        }
        return new BatchCryptoResult(processed, skipped, []);
    }

    /// <summary>指定列の全行を復号する。</summary>
    public BatchCryptoResult DecryptColumn(string columnName)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var value = row[columnName]?.ToString() ?? "";
            if (!value.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
            try { row[columnName] = _crypto.Decrypt(value, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"行{ConfigCsvData.Rows.IndexOf(row) + 1}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    // ── 行一括暗号化・復号 ─────────────────────────────────────────

    /// <summary>指定行の全暗号化可能列を暗号化する。</summary>
    public BatchCryptoResult EncryptRow(DataRowView rowView)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        foreach (DataColumn col in ConfigCsvData.Columns)
        {
            if (!CryptoHelper.IsEncryptableColumn(col.ColumnName)) { skipped++; continue; }
            var value = rowView[col.ColumnName]?.ToString() ?? "";
            if (string.IsNullOrEmpty(value) || value.StartsWith("ENC:", StringComparison.Ordinal))
            { skipped++; continue; }
            rowView[col.ColumnName] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
            processed++;
        }
        return new BatchCryptoResult(processed, skipped, []);
    }

    /// <summary>指定行の全暗号化済み列を復号する。</summary>
    public BatchCryptoResult DecryptRow(DataRowView rowView)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (DataColumn col in ConfigCsvData.Columns)
        {
            if (!CryptoHelper.IsEncryptableColumn(col.ColumnName)) { skipped++; continue; }
            var value = rowView[col.ColumnName]?.ToString() ?? "";
            if (!value.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
            try { rowView[col.ColumnName] = _crypto.Decrypt(value, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"{col.ColumnName}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    // ── テーブル全体暗号化・復号 ────────────────────────────────────

    /// <summary>テーブル全体の暗号化可能セルを一括暗号化する。</summary>
    public BatchCryptoResult EncryptAll()
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var encryptableCols = ConfigCsvData.Columns.Cast<DataColumn>()
            .Where(c => CryptoHelper.IsEncryptableColumn(c.ColumnName)).ToList();

        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            foreach (var col in encryptableCols)
            {
                var value = row[col]?.ToString() ?? "";
                if (string.IsNullOrEmpty(value) || value.StartsWith("ENC:", StringComparison.Ordinal))
                { skipped++; continue; }
                row[col] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
                processed++;
            }
        }
        return new BatchCryptoResult(processed, skipped, []);
    }

    /// <summary>テーブル全体の暗号化済みセルを一括復号する。</summary>
    public BatchCryptoResult DecryptAll()
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        var encryptableCols = ConfigCsvData.Columns.Cast<DataColumn>()
            .Where(c => CryptoHelper.IsEncryptableColumn(c.ColumnName)).ToList();

        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var rowIdx = ConfigCsvData.Rows.IndexOf(row) + 1;
            foreach (var col in encryptableCols)
            {
                var value = row[col]?.ToString() ?? "";
                if (!value.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
                try { row[col] = _crypto.Decrypt(value, _crypto.MasterPassphrase!); processed++; }
                catch (Exception ex) { errors.Add($"行{rowIdx}/{col.ColumnName}: {ex.Message}"); }
            }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    // ── フォルダをエクスプローラーで開く ────────────────────────────
    /// <summary>
    /// Module が未ロード、<c>_moduleDir</c> が未設定、または実フォルダが存在しない場合は無効。
    /// CanExecute は <see cref="Module"/> の変更通知 + <c>LoadFilesAsync</c> 末尾の
    /// 手動 <c>NotifyCanExecuteChanged</c> で追従する。
    /// </summary>
    private bool CanOpenInExplorer()
        => Module is not null
        && _moduleDir is not null
        && Directory.Exists(_moduleDir);

    [RelayCommand(CanExecute = nameof(CanOpenInExplorer))]
    private void OpenInExplorer()
    {
        if (_moduleDir is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "explorer.exe",
                Arguments       = $"\"{_explorerDir ?? _moduleDir}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"エクスプローラー起動エラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("ModuleEdit"));
}
