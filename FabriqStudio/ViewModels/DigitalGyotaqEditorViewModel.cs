using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Views;

namespace FabriqStudio.ViewModels;

public partial class DigitalGyotaqEditorViewModel : ObservableObject
{
    private readonly IDigitalGyotaqService _gyotaqService;

    // ── コレクション ────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<GyotaqTask> _tasks = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommandCommand))]
    private GyotaqTask? _selectedTask;

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

    /// <summary>現在の作業ファイルパス。Save で上書き保存する際に使用。</summary>
    private string? _currentFilePath;

    /// <summary>コマンドライブラリのキャッシュ。初回アクセス時にロードされる。</summary>
    private IReadOnlyList<GyotaqCommand>? _commandLibrary;

    // ────────────────────────────────────────────────────────────────────────

    public DigitalGyotaqEditorViewModel(IDigitalGyotaqService gyotaqService)
    {
        _gyotaqService = gyotaqService;
        SubscribeDirty(_tasks);
    }

    // ── 新規 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void New()
    {
        Tasks.Clear();
        RenumberTaskIds();
        ModuleName       = "";
        _currentFilePath = null;
        IsDirty          = false;
        StatusMessage    = null;
        ErrorMessage     = null;
        ExportCommand.NotifyCanExecuteChanged();
    }

    // ── 読み込み ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title      = "タスクリスト CSV を開く",
            Filter     = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ErrorMessage = null;
            var items = await _gyotaqService.LoadTaskListAsync(dialog.FileName);
            var col   = new ObservableCollection<GyotaqTask>(items);
            SubscribeDirty(col);
            Tasks            = col;
            _currentFilePath = dialog.FileName;
            IsDirty          = false;
            StatusMessage    = $"読み込みました: {System.IO.Path.GetFileName(dialog.FileName)}";
            ExportCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    // ── 保存 ──────────────────────────────────────────────────────────────────

    private bool CanSave() => Tasks.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        // パスが未設定なら「名前を付けて保存」に委譲
        if (_currentFilePath is null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "タスクリスト CSV を保存",
                Filter     = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
                DefaultExt = ".csv",
                FileName   = "task_list.csv"
            };

            if (dialog.ShowDialog() != true) return;
            _currentFilePath = dialog.FileName;
        }

        try
        {
            ErrorMessage = null;
            await _gyotaqService.SaveTaskListAsync(_currentFilePath, Tasks);
            IsDirty       = false;
            StatusMessage = $"保存しました: {System.IO.Path.GetFileName(_currentFilePath)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存エラー: {ex.Message}";
        }
    }

    // ── エクスポート ──────────────────────────────────────────────────────────

    private bool CanExport() =>
        !IsExporting && Tasks.Count > 0 && !string.IsNullOrWhiteSpace(ModuleName);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        ErrorMessage  = null;
        StatusMessage = null;
        IsExporting   = true;
        try
        {
            try
            {
                await _gyotaqService.ExportModuleAsync(ModuleName.Trim(), Tasks, overwrite: false);
            }
            catch (InvalidOperationException ex)
            {
                var result = System.Windows.MessageBox.Show(
                    $"{ex.Message}\n上書きしますか？",
                    "上書き確認",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result != System.Windows.MessageBoxResult.Yes)
                    return;

                await _gyotaqService.ExportModuleAsync(ModuleName.Trim(), Tasks, overwrite: true);
            }

            MessageBox.Show(
                $"モジュール '{ModuleName.Trim()}' を出力しました。\n\n" +
                $"出力先: modules/extended/{ModuleName.Trim()}/",
                "エクスポート完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

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

    // ── タスク操作 ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddTask()
    {
        var task = new GyotaqTask();
        Tasks.Add(task);
        RenumberTaskIds();
        SelectedTask = task;
    }

    private bool CanInsertTask() => SelectedTask is not null;

    [RelayCommand(CanExecute = nameof(CanInsertTask))]
    private void InsertTask()
    {
        var idx  = SelectedTask is not null ? Tasks.IndexOf(SelectedTask) : Tasks.Count;
        var task = new GyotaqTask();
        Tasks.Insert(idx, task);
        RenumberTaskIds();
        SelectedTask = task;
    }

    private bool CanDeleteTask() => SelectedTask is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteTask))]
    private void DeleteTask()
    {
        if (SelectedTask is null) return;
        var idx = Tasks.IndexOf(SelectedTask);
        Tasks.Remove(SelectedTask);
        RenumberTaskIds();
        SelectedTask = Tasks.Count > 0
            ? Tasks[Math.Min(idx, Tasks.Count - 1)]
            : null;
    }

    private bool CanMoveUp() =>
        SelectedTask is not null && Tasks.IndexOf(SelectedTask) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (SelectedTask is null) return;
        var idx = Tasks.IndexOf(SelectedTask);
        Tasks.Move(idx, idx - 1);
        RenumberTaskIds();
        IsDirty = true;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveDown() =>
        SelectedTask is not null && Tasks.IndexOf(SelectedTask) < Tasks.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (SelectedTask is null) return;
        var idx = Tasks.IndexOf(SelectedTask);
        Tasks.Move(idx, idx + 1);
        RenumberTaskIds();
        IsDirty = true;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── コマンド選択 ────────────────────────────────────────────────────────

    private bool CanSelectCommand() => SelectedTask is not null;

    [RelayCommand(CanExecute = nameof(CanSelectCommand))]
    private async Task SelectCommandAsync()
    {
        if (SelectedTask is null) return;

        // 初回アクセス時にライブラリをロード & キャッシュ
        if (_commandLibrary is null)
        {
            try
            {
                _commandLibrary = await _gyotaqService.LoadCommandLibraryAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"コマンドライブラリ読み込みエラー: {ex.Message}";
                return;
            }
        }

        var selected = CommandSelectorDialog.Show(
            _commandLibrary!,
            Window.GetWindow(Application.Current.MainWindow));

        if (selected is null) return;

        // OpenCommand / OpenArgs は常に上書き
        SelectedTask.OpenCommand = selected.OpenCommand;
        SelectedTask.OpenArgs    = selected.OpenArgs;

        // TaskTitle / Instruction は空の場合のみ既定値で補完
        if (string.IsNullOrWhiteSpace(SelectedTask.TaskTitle))
            SelectedTask.TaskTitle = selected.DefaultTitle;

        if (string.IsNullOrWhiteSpace(SelectedTask.Instruction))
            SelectedTask.Instruction = selected.DefaultInstruction;
    }

    // ── ヘルパー ──────────────────────────────────────────────────────────────

    /// <summary>TaskId を T001, T002, ... の連番で振り直す。</summary>
    private void RenumberTaskIds()
    {
        for (int i = 0; i < Tasks.Count; i++)
            Tasks[i].TaskId = $"T{i + 1:D3}";
    }

    private void SubscribeDirty(ObservableCollection<GyotaqTask> collection)
    {
        foreach (var task in collection)
            task.PropertyChanged += OnTaskPropertyChanged;

        collection.CollectionChanged += OnTasksCollectionChanged;
    }

    private void OnTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        IsDirty = true;
        if (e.NewItems is not null)
            foreach (GyotaqTask t in e.NewItems)
                t.PropertyChanged += OnTaskPropertyChanged;

        ExportCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsDirty = true;
    }
}
