using System.Data;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// モジュール詳細表示／編集
///   - guide.txt: テキスト表示 + ロック/解除トグル + Dirty 検知 + 保存
///   - 汎用CSV: DataTable で動的表示 + DataTable 組み込みの RowChanged で Dirty 検知 + 保存
///
/// ロック機構:
///   IsLocked=true（初期値）→ guide.txt TextBox / DataGrid が読み取り専用
///   IsLocked=false          → 編集可能
///
/// 保存:
///   CanExecute = (IsGuideDirty || HasCsvChanges) &amp;&amp; !IsLocked
/// </summary>
public partial class ModuleDetailViewModel : ObservableObject
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
    private bool _hasConfigCsv;
    [ObservableProperty] private string?   _configCsvFileName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasCsvChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    private System.Data.DataRowView? _selectedCsvRow;

    // ─── 状態 ────────────────────────────────────────────────────
    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private string? _errorMessage;

    // ファイルパス（保存時に使用）
    private string? _guidePath;
    private string? _csvFilePath;

    public ModuleDetailViewModel(IFileService fileService, IWorkspaceService workspace)
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

        try
        {
            var root      = _workspace.RootPath
                ?? throw new InvalidOperationException(
                    "ワークスペースが開かれていません。fabriq フォルダを選択してください。");
            var moduleDir = Path.Combine(root, "modules", module.Kind, module.ModuleDir);

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

                    // AcceptChanges を先に呼び初期状態をクリーンにしてからイベント購読する。
                    // 逆順だと AcceptChanges が RowChanged を発火し HasCsvChanges が即 true になる。
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

    // ── ロック切り替え ────────────────────────────────────────────
    [RelayCommand]
    private void ToggleLock() => IsLocked = !IsLocked;

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
                OriginalGuideText = GuideText;   // スナップショット更新 → ハイライト解除
            }

            // 汎用CSV 保存
            if (HasCsvChanges && _csvFilePath is not null)
            {
                await _fileService.WriteCsvFromDataTableAsync(_csvFilePath, ConfigCsvData);
                ConfigCsvData.AcceptChanges();   // RowState リセット → Dirty 解除
                HasCsvChanges = false;
            }

            SaveStatus = "✓ 保存しました";
        }
        catch (Exception ex)
        {
            SaveError = $"保存エラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("ModuleEdit"));
}
