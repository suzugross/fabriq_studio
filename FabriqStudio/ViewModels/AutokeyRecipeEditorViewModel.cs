using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

public partial class AutokeyRecipeEditorViewModel : ObservableObject
{
    private readonly IAutokeyService _autokeyService;

    // ── コレクション ────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<RecipeRow> _rows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private RecipeRow? _selectedRow;

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
    /// <summary>Action 列 ComboBox の選択肢。</summary>
    public IReadOnlyList<string> ActionTypes => ActionType.All;

    // ────────────────────────────────────────────────────────────────────────

    public AutokeyRecipeEditorViewModel(IAutokeyService autokeyService)
    {
        _autokeyService = autokeyService;
        SubscribeDirty(_rows);
    }

    // ── 新規 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void New()
    {
        Rows.Clear();       // CollectionChanged → IsDirty = true (後で上書き)
        RenumberSteps();
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
            Title      = "レシピ CSV を開く",
            Filter     = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ErrorMessage = null;
            var items    = await _autokeyService.LoadRecipeAsync(dialog.FileName);
            var col      = new ObservableCollection<RecipeRow>(items);
            SubscribeDirty(col);
            Rows          = col;
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
                await _autokeyService.ExportModuleAsync(ModuleName.Trim(), Rows, overwrite: false);
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

                await _autokeyService.ExportModuleAsync(ModuleName.Trim(), Rows, overwrite: true);
            }

            StatusMessage = $"✓ エクスポート完了: modules/extended/{ModuleName.Trim()}/";
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
            var log = await _autokeyService.TestRunAsync(Rows);

            Views.LogViewerDialog.ShowLog("テスト実行結果", log);

            StatusMessage = "✓ テスト実行完了";
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
        var row = new RecipeRow();
        Rows.Add(row);       // CollectionChanged → 購読・IsDirty
        RenumberSteps();
        SelectedRow = row;
    }

    private bool CanInsertRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanInsertRow))]
    private void InsertRow()
    {
        var idx = SelectedRow is not null ? Rows.IndexOf(SelectedRow) : Rows.Count;
        var row = new RecipeRow();
        Rows.Insert(idx, row);
        RenumberSteps();
        SelectedRow = row;
    }

    private bool CanDeleteRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteRow))]
    private void DeleteRow()
    {
        if (SelectedRow is null) return;
        var idx = Rows.IndexOf(SelectedRow);
        Rows.Remove(SelectedRow);
        RenumberSteps();
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
        RenumberSteps();
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
        RenumberSteps();
        IsDirty = true;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── ヘルパー ──────────────────────────────────────────────────────────────

    private void RenumberSteps()
    {
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].Step = i + 1;
    }

    private void SubscribeDirty(ObservableCollection<RecipeRow> collection)
    {
        foreach (var row in collection)
            row.PropertyChanged += OnRowPropertyChanged;

        collection.CollectionChanged += OnRowsCollectionChanged;
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        IsDirty = true;
        if (e.NewItems is not null)
            foreach (RecipeRow r in e.NewItems)
                r.PropertyChanged += OnRowPropertyChanged;

        ExportCommand.NotifyCanExecuteChanged();
        TestRunCommand.NotifyCanExecuteChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsDirty = true;

        // Action が変わったら Wait と Value のデフォルト値を自動適用
        if (e.PropertyName == nameof(RecipeRow.Action) && sender is RecipeRow row)
        {
            row.Wait  = ActionType.DefaultWait(row.Action);
            var defVal = ActionType.DefaultValue(row.Action);
            if (!string.IsNullOrEmpty(defVal))
                row.Value = defVal;
        }
    }
}
