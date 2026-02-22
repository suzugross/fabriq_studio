using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// モジュール編集モード
///   - 左ペイン: カテゴリ一覧（kernel/csv/categories.csv）
///   - 右ペイン: カテゴリでフィルタしたモジュールマスターリスト
///              （modules/standard/**/module.csv + modules/extended/**/module.csv）
/// 行をダブルクリックすると ShowModuleDetailMessage を送信して詳細画面へ遷移する。
/// </summary>
public partial class ModuleEditViewModel : ObservableObject
{
    private readonly IModuleService _moduleService;
    private readonly ICsvService    _csvService;

    // ─── モジュール全件 ────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ModuleMasterEntry> _allModules      = [];

    // ─── フィルタ済みモジュール（右ペイン表示用）─────────────────
    [ObservableProperty] private ObservableCollection<ModuleMasterEntry> _filteredModules = [];

    // ─── カテゴリ一覧（左ペイン）─────────────────────────────────
    [ObservableProperty] private ObservableCollection<string> _categoryNames = [];
    [ObservableProperty] private string?                      _selectedCategoryName;

    // ─── 選択中モジュール（ダブルクリックで詳細遷移）─────────────
    [ObservableProperty] private ModuleMasterEntry? _selectedModule;

    // ─── 状態 ─────────────────────────────────────────────────────
    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public ModuleEditViewModel(IModuleService moduleService, ICsvService csvService)
    {
        _moduleService = moduleService;
        _csvService    = csvService;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var modulesTask    = _moduleService.GetAllModulesAsync();
            var categoriesTask = _csvService.ReadAsync<CategoryItem>("kernel/csv/categories.csv");
            await Task.WhenAll(modulesTask, categoriesTask);

            AllModules = new ObservableCollection<ModuleMasterEntry>(modulesTask.Result);

            // カテゴリリスト先頭に「すべて」を追加し、categories.csv の Order 順で並べる
            var catNames = new[] { "すべて" }
                .Concat(categoriesTask.Result
                    .OrderBy(c => c.Order)
                    .Select(c => c.Category))
                .ToList();
            CategoryNames        = new ObservableCollection<string>(catNames);
            SelectedCategoryName = "すべて";   // ← OnSelectedCategoryNameChanged が FilteredModules を構築する
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

    // ── カテゴリ選択変更時: FilteredModules を再構築 ──────────────
    partial void OnSelectedCategoryNameChanged(string? value)
    {
        var items = (value is null || value == "すべて")
            ? AllModules
            : (IEnumerable<ModuleMasterEntry>)AllModules.Where(m => m.Category == value);
        FilteredModules = new ObservableCollection<ModuleMasterEntry>(items);
    }

    // ── 全件ロード完了後: フィルタを同期 ─────────────────────────
    partial void OnAllModulesChanged(ObservableCollection<ModuleMasterEntry> value)
    {
        OnSelectedCategoryNameChanged(SelectedCategoryName);
    }

    /// <summary>DataGrid 行のダブルクリック時に呼び出す（CommandParameter = SelectedModule）</summary>
    [RelayCommand]
    private void ViewDetail(ModuleMasterEntry? module)
    {
        if (module is null) return;
        WeakReferenceMessenger.Default.Send(new ShowModuleDetailMessage(module));
    }
}
