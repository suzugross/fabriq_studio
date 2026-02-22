using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly ICsvService _csvService;

    [ObservableProperty] private ObservableCollection<HostEntry> _hosts        = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteHostCommand))]
    private HostEntry? _selectedHost;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddHostCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteHostCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLocked = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public HostListViewModel(ICsvService csvService)
    {
        _csvService = csvService;
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
