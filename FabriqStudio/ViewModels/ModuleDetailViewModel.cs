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
/// モジュール詳細表示
///   - guide.txt: テキスト表示 + 編集ロック/解除トグル（モック）
///   - 汎用CSV: module.csv 以外の .csv を DataTable として動的表示
/// </summary>
public partial class ModuleDetailViewModel : ObservableObject
{
    private readonly IFileService        _fileService;
    private readonly IAppSettingsService _settings;

    [ObservableProperty] private ModuleMasterEntry? _module;

    // ─── guide.txt ───────────────────────────────────────────────
    [ObservableProperty] private string? _guideText;
    [ObservableProperty] private bool    _hasGuideText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuideReadOnly))]
    private bool _isGuideEditable;

    /// <summary>TextBox の IsReadOnly バインド用（IsGuideEditable の逆）</summary>
    public bool IsGuideReadOnly => !IsGuideEditable;

    // ─── 汎用 CSV ────────────────────────────────────────────────
    [ObservableProperty] private DataTable _configCsvData = new();
    [ObservableProperty] private bool      _hasConfigCsv;
    [ObservableProperty] private string?   _configCsvFileName;

    // ─── 状態 ────────────────────────────────────────────────────
    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public ModuleDetailViewModel(IFileService fileService, IAppSettingsService settings)
    {
        _fileService = fileService;
        _settings    = settings;
    }

    /// <summary>選択されたモジュールを読み込む。</summary>
    public void Load(ModuleMasterEntry module)
    {
        Module          = module;
        IsGuideEditable = false;
        _ = LoadFilesAsync(module);
    }

    private async Task LoadFilesAsync(ModuleMasterEntry module)
    {
        IsLoading       = true;
        ErrorMessage    = null;
        GuideText       = null;
        HasGuideText    = false;
        ConfigCsvData   = new DataTable();
        HasConfigCsv    = false;
        ConfigCsvFileName = null;

        try
        {
            var moduleDir = Path.Combine(
                _settings.FabriqRootPath, "modules", module.Kind, module.ModuleDir);

            // ── guide.txt ──────────────────────────────────────────
            var guidePath = Path.Combine(moduleDir, "guide.txt");
            var guideText = await _fileService.ReadTextAsync(guidePath);
            GuideText    = guideText;
            HasGuideText = guideText is not null;

            // ── module.csv 以外の CSV（1件目を動的表示対象にする）──
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
                    ConfigCsvData     = await _fileService.ReadCsvAsDataTableAsync(csvFile);
                    HasConfigCsv      = ConfigCsvData.Columns.Count > 0;
                    ConfigCsvFileName = Path.GetFileName(csvFile);
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

    [RelayCommand]
    private void ToggleGuideEdit() => IsGuideEditable = !IsGuideEditable;

    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("ModuleEdit"));
}
