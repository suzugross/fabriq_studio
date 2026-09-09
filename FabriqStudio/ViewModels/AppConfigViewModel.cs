using System.Data;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Helpers;

namespace FabriqStudio.ViewModels;

/// <summary>
/// app_config モジュール専用の編集画面。
/// 汎用 ModuleDetailViewModel と同じ guide.txt / CSV 編集機能に加え、
/// インストーラーファイルの取り込み機能（file/ ディレクトリへのコピー + CSV 行追加）を持つ。
///
/// CSV カラム: Enabled, AppName, FileName, Type, SilentArgs, Description
/// 対応 Type: exe, msi, bat
/// </summary>
public partial class AppConfigViewModel : ObservableObject, IDirtyAwareViewModel
{
    // ─── IDirtyAwareViewModel ───────────────────────────────────────
    public bool HasUnsavedChanges => IsGuideDirty || HasCsvChanges;
    public string DirtyDescription => Module is not null
        ? $"アプリ設定: {(string.IsNullOrEmpty(Module.MenuName) ? Module.ModuleDir : Module.MenuName)}"
        : "アプリ設定";

    /// <summary>
    /// guide.txt と CSV の編集をロールバックする。
    /// Module 自体はこの画面で編集対象としていないためスナップショット不要。
    /// </summary>
    public void DiscardChanges()
    {
        // guide.txt — OriginalGuideText に戻すと IsGuideDirty が自動で false になる
        if (HasGuideText)
            GuideText = OriginalGuideText;

        // CSV — RejectChanges が RowChanged を再発火するのでハンドラを一時解除
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

    private readonly IFileService      _fileService;
    private readonly IModuleDataResolver _resolver;
    private readonly IProfileDataService _profileData;

    // ─── モジュール情報 ───────────────────────────────────────────
    [ObservableProperty] private ModuleMasterEntry? _module;

    // ─── ロック ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportInstallerCommand))]
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

    // ─── CSV ──────────────────────────────────────────────────────
    [ObservableProperty] private DataTable _configCsvData = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportInstallerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSampleRowsCommand))]
    private bool _hasConfigCsv;

    [ObservableProperty] private string? _configCsvFileName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasCsvChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    private DataRowView? _selectedCsvRow;

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

    /// <summary>本体側の file/（PDF に file/ を作る前の確認に使う）。</summary>
    private string? _bodyFileDirPath;

    // ファイルパス（保存時に使用）
    private string? _guidePath;
    private string? _csvFilePath;
    private string? _fileDirPath;

    public AppConfigViewModel(
        IFileService        fileService,
        IModuleDataResolver resolver,
        IProfileDataService profileData)
    {
        _fileService = fileService;
        _resolver    = resolver;
        _profileData = profileData;
    }

    /// <summary>選択されたモジュールを本体文脈で読み込む。</summary>
    public void Load(ModuleMasterEntry module) => Load(module, null);

    /// <summary>選択されたモジュールを読み込む。<paramref name="dataSet"/> はプロファイル名（PDF 文脈）、null なら本体。</summary>
    public void Load(ModuleMasterEntry module, string? dataSet)
    {
        DataSet    = string.IsNullOrWhiteSpace(dataSet) ? null : dataSet.Trim();
        Module     = module;
        IsLocked   = true;
        SaveStatus = null;
        SaveError  = null;
        _ = LoadFilesAsync(module);
    }

    private async Task LoadFilesAsync(ModuleMasterEntry module)
    {
        IsLoading         = true;
        ErrorMessage      = null;
        GuideText         = null;
        OriginalGuideText = null;
        HasGuideText      = false;
        ConfigCsvData     = new DataTable();
        HasConfigCsv      = false;
        HasCsvChanges     = false;
        ConfigCsvFileName = null;
        _guidePath        = null;
        _csvFilePath      = null;
        _csvSavePath      = null;
        _fileDirPath      = null;
        _bodyFileDirPath  = null;
        Overrides         = [];
        CsvOverrideNote   = null;
        IsFallbackView    = false;
        FallbackNote      = null;
        CsvSourceLabel    = "";
        ImportedNote      = null;

        try
        {
            // 本体側のモジュールフォルダ（guide.txt / module.csv はフレームワーク資産なので常に本体）
            var moduleRel = _resolver.ModuleRelPath(module.Kind, module.ModuleDir, "");
            var moduleDir = _resolver.ResolveRead(moduleRel, null).AbsPath;

            // file/（インストーラー置き場）。PDF 文脈では PDF 側に置く（フォルダは実際に置くときだけ作る）
            var fileRel      = _resolver.ModuleRelPath(module.Kind, module.ModuleDir, "file");
            _bodyFileDirPath = _resolver.ResolveRead(fileRel, null).AbsPath;
            _fileDirPath     = IsProfileContext ? _resolver.ResolveWrite(fileRel, DataSet).AbsPath : _bodyFileDirPath;

            // PDF 側の上書き有無（本体側の編集画面でだけ注意喚起する）
            Overrides = IsProfileContext ? [] : _resolver.FindOverrides(module.ModuleDir);

            // ── guide.txt ──────────────────────────────────────────
            _guidePath = Path.Combine(moduleDir, "guide.txt");
            var guideText = await _fileService.ReadTextAsync(_guidePath);
            GuideText         = guideText;
            OriginalGuideText = guideText;
            HasGuideText      = guideText is not null;

            // ── module.csv 以外の CSV（1件目を動的表示対象）──────
            // preset.csv は AppConfig の編集対象外（Enabled はチェックボックス、
            // 他列は XAML 側で明示定義済みのため）
            if (Directory.Exists(moduleDir))
            {
                // 先頭の設定 CSV（カーネルと同じ合成一覧。PDF 文脈では PDF 優先）
                var entry = _resolver.EnumerateCsvs(module.ModuleDir, DataSet).FirstOrDefault();

                if (entry is not null)
                {
                    var csvRel        = _resolver.ModuleRelPath(module.Kind, module.ModuleDir, entry.Name);
                    var csvFile       = entry.AbsPath;
                    _csvFilePath      = csvFile;
                    _csvSavePath      = IsProfileContext ? _resolver.ResolveWrite(csvRel, DataSet).AbsPath : csvFile;
                    ApplyCsvSource(entry.Source, entry.Name);

                    var table         = await _fileService.ReadCsvAsDataTableAsync(csvFile);
                    HasConfigCsv      = table.Columns.Count > 0;
                    ConfigCsvFileName = entry.Name;
                    CsvOverrideNote   = IsProfileContext ? null : ModuleOverrideText.CsvNote(Overrides, ConfigCsvFileName);

                    table.AcceptChanges();
                    table.RowChanged += OnCsvRowChanged;
                    table.RowDeleted += OnCsvRowChanged;
                    ConfigCsvData = table;
                }
            }
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

    private void OnCsvRowChanged(object sender, DataRowChangeEventArgs e)
        => HasCsvChanges = true;

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
    private bool CanSave() => (IsGuideDirty || HasCsvChanges) && !IsLocked;

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
                OriginalGuideText = GuideText;
            }

            // CSV 保存（PDF 文脈では profiles/<名>/modules/app_config/ へ。フォルダは保存の瞬間だけ作る）
            var savePath = _csvSavePath ?? _csvFilePath;
            if (HasCsvChanges && savePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                await _fileService.WriteCsvFromDataTableAsync(savePath, ConfigCsvData);
                ConfigCsvData.AcceptChanges();
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

    private bool CanImportCsv() => IsFallbackView && IsProfileContext && Module is not null && ConfigCsvFileName is not null;

    [RelayCommand(CanExecute = nameof(CanImportCsv))]
    private async Task ImportCsvAsync()
    {
        if (DataSet is null || Module is null || ConfigCsvFileName is null) return;
        SaveError = null;
        try
        {
            var copied = await _profileData.MaterializeCsvAsync(DataSet, Module.ModuleDir, ConfigCsvFileName);
            await LoadFilesAsync(Module);   // 採用元が PDF に変わる

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

    // ── インストーラー取り込み ─────────────────────────────────────
    private bool CanImportInstaller() => HasConfigCsv && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanImportInstaller))]
    private async Task ImportInstallerAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "インストーラーファイルを選択",
            Filter = "インストーラー (*.exe;*.msi;*.bat)|*.exe;*.msi;*.bat|すべてのファイル (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var sourceFile = dialog.FileName;
        var fileName   = Path.GetFileName(sourceFile);
        var extension  = Path.GetExtension(sourceFile).TrimStart('.').ToLowerInvariant();

        // Type 自動判定
        var type = extension switch
        {
            "msi" => "msi",
            "bat" => "bat",
            _     => "exe"
        };

        // file/ ディレクトリの確保。PDF 文脈で初めて作るときは、本体側の file/ がこのプロファイルでは
        // 使われなくなる（資材フォルダはフォルダ単位 all-or-nothing）ことを確認してから作る
        if (_fileDirPath is null) return;
        if (IsProfileContext && !Directory.Exists(_fileDirPath)
            && _bodyFileDirPath is not null && Directory.Exists(_bodyFileDirPath))
        {
            var bodyCount = Directory.EnumerateFileSystemEntries(_bodyFileDirPath).Count();
            if (bodyCount > 0)
            {
                var ok = MessageBox.Show(
                    $"プロファイル {DataSet} のデータフォルダに file/ を作ります。\n" +
                    $"作ると、本体側の file/（{bodyCount} 件）はこのプロファイルの実行では一切使われなくなります" +
                    "（フォルダ単位で切り替わり、足りない分を本体から補うことはありません）。\n\n" +
                    "この案件で使うインストーラーは、すべてデータフォルダ側に置いてください。続行しますか？",
                    "データフォルダに file/ を作成", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.OK) return;
            }
        }
        Directory.CreateDirectory(_fileDirPath);

        var destPath = Path.Combine(_fileDirPath, fileName);

        // 同名ファイル上書き確認
        if (File.Exists(destPath))
        {
            var result = MessageBox.Show(
                $"ファイル「{fileName}」は既に存在します。\n上書きしますか？",
                "上書き確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        // ファイルコピー（UI フリーズ防止）
        try
        {
            await Task.Run(() => File.Copy(sourceFile, destPath, overwrite: true));
        }
        catch (Exception ex)
        {
            SaveError = $"ファイルコピーエラー: {ex.Message}";
            return;
        }

        // CSV に新規行追加
        var row = ConfigCsvData.NewRow();
        SetIfColumnExists(row, "Enabled",     "1");
        SetIfColumnExists(row, "AppName",     Path.GetFileNameWithoutExtension(fileName));
        SetIfColumnExists(row, "FileName",    fileName);
        SetIfColumnExists(row, "Type",        type);
        SetIfColumnExists(row, "SilentArgs",  "");
        SetIfColumnExists(row, "Description", "");
        ConfigCsvData.Rows.Add(row);

        SaveStatus = $"✓ {fileName} を追加しました";
    }

    private static void SetIfColumnExists(DataRow row, string columnName, string value)
    {
        if (row.Table.Columns.Contains(columnName))
            row[columnName] = value;
    }

    // ── 戻る ──────────────────────────────────────────────────────
    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("ModuleEdit"));
}
