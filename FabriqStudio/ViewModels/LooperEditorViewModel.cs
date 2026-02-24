using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

public partial class LooperEditorViewModel : ObservableObject
{
    private readonly ILooperService _looperService;

    // ── コレクション ────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<LooperEntry> _rows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private LooperEntry? _selectedRow;

    // ── 状態 ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private string _moduleName = "";

    [ObservableProperty] private bool    _isDirty;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isExporting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestRunCommand))]
    private bool _isRunning;

    // ── UI 用定数 ───────────────────────────────────────────────────────────
    /// <summary>Condition 列 ComboBox の選択肢。</summary>
    public IReadOnlyList<string> ConditionTypes { get; } = ["OnError", "Always"];

    // ────────────────────────────────────────────────────────────────────────

    public LooperEditorViewModel(ILooperService looperService)
    {
        _looperService = looperService;
        SubscribeDirty(_rows);
    }

    // ── 新規 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void New()
    {
        Rows.Clear();
        ModuleName    = "";
        IsDirty       = false;
        StatusMessage = null;
        ErrorMessage  = null;
        ExportCommand.NotifyCanExecuteChanged();
        TestRunCommand.NotifyCanExecuteChanged();
    }

    // ── 読み込み ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title      = "looper_list.csv を開く",
            Filter     = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ErrorMessage = null;
            var items = await _looperService.LoadLooperListAsync(dialog.FileName);
            var col   = new ObservableCollection<LooperEntry>(items);
            SubscribeDirty(col);
            Rows          = col;

            // 親ディレクトリ名をモジュール名として推定
            // 例: modules/extended/my_looper/looper_list.csv → "my_looper"
            var parentDir = System.IO.Path.GetDirectoryName(dialog.FileName);
            ModuleName    = parentDir is not null
                ? System.IO.Path.GetFileName(parentDir)
                : "";

            IsDirty       = false;
            StatusMessage = $"読み込みました: {System.IO.Path.GetFileName(dialog.FileName)}";
            ExportCommand.NotifyCanExecuteChanged();
            TestRunCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    // ── エクスポート ──────────────────────────────────────────────────────────

    private bool CanExport() =>
        !IsExporting && Rows.Count > 0 && !string.IsNullOrWhiteSpace(ModuleName);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        ErrorMessage  = null;
        StatusMessage = null;
        IsExporting   = true;
        try
        {
            // まず overwrite=false で試みる
            try
            {
                await _looperService.ExportModuleAsync(ModuleName.Trim(), Rows, overwrite: false);
            }
            catch (InvalidOperationException ex)
            {
                // 既存ディレクトリ → 上書き確認
                var result = System.Windows.MessageBox.Show(
                    $"{ex.Message}\n上書きしますか？",
                    "上書き確認",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result != System.Windows.MessageBoxResult.Yes)
                    return;

                await _looperService.ExportModuleAsync(ModuleName.Trim(), Rows, overwrite: true);
            }

            StatusMessage = $"エクスポート完了: modules/extended/{ModuleName.Trim()}/";
            IsDirty       = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"エクスポートエラー: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    // ── テスト実行 ──────────────────────────────────────────────────────────

    private bool CanTestRun() => !IsRunning && Rows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanTestRun))]
    private async Task TestRunAsync()
    {
        IsRunning     = true;
        ErrorMessage  = null;
        StatusMessage = null;
        try
        {
            var log = await _looperService.TestRunAsync(Rows);

            Views.LogViewerDialog.ShowLog("テスト実行結果", log);

            StatusMessage = "テスト実行完了";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"テスト実行エラー: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    // ── 行操作 ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddRow()
    {
        var row = new LooperEntry();
        Rows.Add(row);
        SelectedRow = row;
    }

    private bool CanInsertRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanInsertRow))]
    private void InsertRow()
    {
        var idx = SelectedRow is not null ? Rows.IndexOf(SelectedRow) : Rows.Count;
        var row = new LooperEntry();
        Rows.Insert(idx, row);
        SelectedRow = row;
    }

    private bool CanDeleteRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteRow))]
    private void DeleteRow()
    {
        if (SelectedRow is null) return;
        var idx = Rows.IndexOf(SelectedRow);
        Rows.Remove(SelectedRow);
        SelectedRow = Rows.Count > 0
            ? Rows[Math.Min(idx, Rows.Count - 1)]
            : null;
    }

    private bool CanMoveUp() =>
        SelectedRow is not null && Rows.IndexOf(SelectedRow) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (SelectedRow is null) return;
        var idx = Rows.IndexOf(SelectedRow);
        Rows.Move(idx, idx - 1);
        IsDirty = true;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveDown() =>
        SelectedRow is not null && Rows.IndexOf(SelectedRow) < Rows.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (SelectedRow is null) return;
        var idx = Rows.IndexOf(SelectedRow);
        Rows.Move(idx, idx + 1);
        IsDirty = true;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── ヘルパー ──────────────────────────────────────────────────────────────

    private void SubscribeDirty(ObservableCollection<LooperEntry> collection)
    {
        foreach (var row in collection)
            row.PropertyChanged += OnRowPropertyChanged;

        collection.CollectionChanged += OnRowsCollectionChanged;
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        IsDirty = true;
        if (e.NewItems is not null)
            foreach (LooperEntry r in e.NewItems)
                r.PropertyChanged += OnRowPropertyChanged;

        ExportCommand.NotifyCanExecuteChanged();
        TestRunCommand.NotifyCanExecuteChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsDirty = true;
    }
}
