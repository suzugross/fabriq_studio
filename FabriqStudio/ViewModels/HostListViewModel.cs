using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 端末一覧モード — hostlist.csv を表示する。
/// 行をダブルクリックすると ShowHostDetailMessage を送信して詳細画面へ遷移する。
/// </summary>
public partial class HostListViewModel : ObservableObject
{
    private readonly ICsvService  _csvService;
    private readonly IFileService _fileService;

    [ObservableProperty] private ObservableCollection<HostEntry> _hosts        = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteHostCommand))]
    private HostEntry? _selectedHost;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddHostCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteHostCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportHostListCommand))]
    private bool _isLocked = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public HostListViewModel(ICsvService csvService, IFileService fileService, IWorkspaceService workspace)
    {
        _csvService  = csvService;
        _fileService = fileService;
        workspace.WorkspaceChanged += (_, e) =>
        {
            if (e.NewPath is null) { Hosts.Clear(); return; }
            _ = LoadAsync();
        };
        if (workspace.IsOpen)
            _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var items = await _csvService.ReadAsync<HostEntry>("kernel/csv/hostlist.csv");
            Hosts = new ObservableCollection<HostEntry>(items.OrderBy(h => h.AdminID));
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

    /// <summary>DataGrid 行のダブルクリック時に呼び出す（CommandParameter = SelectedHost）</summary>
    [RelayCommand]
    private void ViewDetail(HostEntry? host)
    {
        if (host is null) return;
        WeakReferenceMessenger.Default.Send(new ShowHostDetailMessage(host));
    }

    private bool CanAddHost() => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanAddHost))]
    private void AddHost()
    {
        var newHost = new HostEntry();
        Hosts.Add(newHost);
        SelectedHost = newHost;
        WeakReferenceMessenger.Default.Send(new ShowHostDetailMessage(newHost));
    }

    private bool CanDeleteHost() => SelectedHost is not null && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanDeleteHost))]
    private void DeleteHost()
    {
        if (SelectedHost is null) return;
        var next = Hosts.SkipWhile(h => h != SelectedHost).Skip(1).FirstOrDefault()
                   ?? Hosts.TakeWhile(h => h != SelectedHost).LastOrDefault();
        Hosts.Remove(SelectedHost);
        SelectedHost = next;
        IsDirty      = true;
    }

    // ── 端末リスト インポート ───────────────────────────────────
    private bool CanImportHostList() => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanImportHostList))]
    private async Task ImportHostListAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "端末リストをインポート",
            Filter = "CSV (*.csv)|*.csv|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var filePath  = dialog.FileName;
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // 拡張子に応じてパース方式を分岐
        List<HostEntry> imported;
        try
        {
            if (extension == ".csv")
            {
                // CSV → CsvHelper で HostEntry にマッピング
                imported = await _fileService.LoadCsvAsModelAsync<HostEntry>(filePath);
            }
            else
            {
                // TXT 等 → 1行1端末名として読み込み
                var lines = await _fileService.LoadLinesFromFileAsync(filePath);
                imported = lines.Select(l => new HostEntry { NewPCName = l }).ToList();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"ファイルの読み込みに失敗しました:\n{ex.Message}",
                "インポートエラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (imported.Count == 0)
        {
            MessageBox.Show("インポート可能なデータがありませんでした。", "インポート", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = $"ファイルから {imported.Count} 件のデータを読み込みました。\n" +
                  "インポートするデータを既存のリストに【追加】しますか？\n\n" +
                  "[はい] : 既存のリストの末尾に追加する（重複はスキップ）\n" +
                  "[いいえ] : 既存のリストをすべて消去して【上書き】する\n" +
                  "[キャンセル] : インポートを取りやめる";
        var result = MessageBox.Show(msg, "インポート方法の選択", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return;

        if (result == MessageBoxResult.No)
            Hosts.Clear();

        var existing = Hosts.Select(h => h.NewPCName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in imported)
        {
            if (string.IsNullOrWhiteSpace(entry.NewPCName)) continue;
            if (existing.Contains(entry.NewPCName)) continue;
            Hosts.Add(entry);
            existing.Add(entry.NewPCName);
        }

        IsDirty = true;
    }

    private bool CanSave() => IsDirty && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        SaveError  = null;
        SaveStatus = null;
        try
        {
            await _csvService.WriteAsync("kernel/csv/hostlist.csv", Hosts);
            IsDirty    = false;
            SaveStatus = "✓ 保存しました";
        }
        catch (Exception ex)
        {
            SaveError = $"保存エラー: {ex.Message}";
        }
    }
}
