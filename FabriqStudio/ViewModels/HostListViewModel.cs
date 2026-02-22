using System.Collections.ObjectModel;
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
    [ObservableProperty] private HostEntry?                      _selectedHost;
    [ObservableProperty] private bool                            _isLoading;
    [ObservableProperty] private string?                         _errorMessage;

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
            Hosts = new ObservableCollection<HostEntry>(items);
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
}
