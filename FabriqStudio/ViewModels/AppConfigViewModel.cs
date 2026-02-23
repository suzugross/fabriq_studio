using System.Data;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// app_config モジュール専用の編集画面。
/// 汎用 ModuleDetailViewModel と同じ guide.txt / CSV 編集機能に加え、
/// インストーラーファイルの取り込み機能（file/ ディレクトリへのコピー + CSV 行追加）を持つ。
///
/// CSV カラム: Enabled, AppName, FileName, Type, SilentArgs, Description
/// 対応 Type: exe, msi, bat
/// </summary>
public partial class AppConfigViewModel : ObservableObject
{
    private readonly IFileService      _fileService;
    private readonly IWorkspaceService _workspace;

    // ─── モジュール情報 ───────────────────────────────────────────
    [ObservableProperty] private ModuleMasterEntry? _module;

    // ─── ロック ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportInstallerCommand))]
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

    // ファイルパス（保存時に使用）
    private string? _guidePath;
    private string? _csvFilePath;
    private string? _fileDirPath;

    public AppConfigViewModel(IFileService fileService, IWorkspaceService workspace)
    {
        _fileService = fileService;
        _workspace   = workspace;
    }

    /// <summary>選択されたモジュールを読み込む。</summary>
    public void Load(ModuleMasterEntry module)
    {
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
        _fileDirPath      = null;

        try
        {
            var root = _workspace.RootPath
                ?? throw new InvalidOperationException(
                    "ワークスペースが開かれていません。fabriq フォルダを選択してください。");
            var moduleDir = Path.Combine(root, "modules", module.Kind, module.ModuleDir);

            // file/ ディレクトリパスを算出
            _fileDirPath = Path.Combine(moduleDir, "file");

            // ── guide.txt ──────────────────────────────────────────
            _guidePath = Path.Combine(moduleDir, "guide.txt");
            var guideText = await _fileService.ReadTextAsync(_guidePath);
            GuideText         = guideText;
            OriginalGuideText = guideText;
            HasGuideText      = guideText is not null;

            // ── module.csv 以外の CSV（1件目を動的表示対象）──────
            if (Directory.Exists(moduleDir))
            {
                var csvFile = Directory
                    .GetFiles(moduleDir, "*.csv")
                    .Where(f => !string.Equals(
                        Path.GetFileName(f), "module.csv",
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .FirstOrDefault();

                if (csvFile is not null)
                {
                    _csvFilePath      = csvFile;
                    var table         = await _fileService.ReadCsvAsDataTableAsync(csvFile);
                    HasConfigCsv      = table.Columns.Count > 0;
                    ConfigCsvFileName = Path.GetFileName(csvFile);

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

            // CSV 保存
            if (HasCsvChanges && _csvFilePath is not null)
            {
                await _fileService.WriteCsvFromDataTableAsync(_csvFilePath, ConfigCsvData);
                ConfigCsvData.AcceptChanges();
                HasCsvChanges = false;
            }

            SaveStatus = "✓ 保存しました";
            WeakReferenceMessenger.Default.Send(new WorkspaceDataUpdatedMessage("ModuleDetail"));
        }
        catch (Exception ex)
        {
            SaveError = $"保存エラー: {ex.Message}";
        }
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

        // file/ ディレクトリの確保
        if (_fileDirPath is null) return;
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
