using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// モジュール編集モード — categories.csv を表示する
/// </summary>
public partial class ModuleEditViewModel : ObservableObject
{
    private readonly ICsvService _csvService;

    [ObservableProperty]
    private ObservableCollection<CategoryItem> _categories = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public ModuleEditViewModel(ICsvService csvService)
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
            var items = await _csvService.ReadAsync<CategoryItem>("kernel/csv/categories.csv");
            Categories = new ObservableCollection<CategoryItem>(items);
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
