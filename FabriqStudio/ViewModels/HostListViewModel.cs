using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 端末一覧モード — hostlist.csv を表示する
/// </summary>
public partial class HostListViewModel : ObservableObject
{
    private readonly ICsvService _csvService;

    [ObservableProperty]
    private ObservableCollection<HostEntry> _hosts = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public HostListViewModel(ICsvService csvService)
    {
        _csvService = csvService;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
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
}
