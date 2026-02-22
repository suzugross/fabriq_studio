using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 基本パラメータモード
///   - 作業者管理       (kernel/csv/workers.csv)
///   - ログ出力先管理   (kernel/csv/log_destinations.csv)
///   - プロファイル選択 (profiles/*.csv)
/// </summary>
public partial class BasicParamsViewModel : ObservableObject
{
    private readonly ICsvService     _csvService;
    private readonly IProfileService _profileService;

    // ─── 作業者 ────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<WorkerEntry> _workers         = [];
    [ObservableProperty] private bool                              _isWorkersLoading;
    [ObservableProperty] private string?                           _workersError;
    [ObservableProperty] private string?                           _workersSaveStatus;

    // ─── ログ出力先 ────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<LogDestination> _logDestinations = [];
    [ObservableProperty] private bool                                 _isLogDestLoading;
    [ObservableProperty] private string?                              _logDestError;
    [ObservableProperty] private string?                              _logDestSaveStatus;

    // ─── プロファイル ──────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ProfileEntry> _profiles        = [];
    [ObservableProperty] private ProfileEntry?                      _selectedProfile;
    [ObservableProperty] private bool                               _isProfilesLoading;
    [ObservableProperty] private string?                            _profilesError;

    public BasicParamsViewModel(ICsvService csvService, IProfileService profileService)
    {
        _csvService     = csvService;
        _profileService = profileService;
        _ = LoadAllAsync();
    }

    // ── 初期ロード（全セクション並列）─────────────────────────────
    private Task LoadAllAsync()
        => Task.WhenAll(LoadWorkersAsync(), LoadLogDestAsync(), LoadProfilesAsync());

    // ── 作業者: 読み込み ──────────────────────────────────────────
    private async Task LoadWorkersAsync()
    {
        IsWorkersLoading = true;
        WorkersError     = null;
        try
        {
            var items = await _csvService.ReadAsync<WorkerEntry>("kernel/csv/workers.csv");
            Workers = new ObservableCollection<WorkerEntry>(items);
        }
        catch (Exception ex)
        {
            WorkersError = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsWorkersLoading = false;
        }
    }

    // ── 作業者: 保存 ──────────────────────────────────────────────
    [RelayCommand]
    private async Task SaveWorkersAsync()
    {
        WorkersError      = null;
        WorkersSaveStatus = null;
        try
        {
            await _csvService.WriteAsync("kernel/csv/workers.csv", Workers);
            WorkersSaveStatus = "✓ 保存しました";
        }
        catch (Exception ex)
        {
            WorkersError = $"保存エラー: {ex.Message}";
        }
    }

    // ── ログ出力先: 読み込み ──────────────────────────────────────
    private async Task LoadLogDestAsync()
    {
        IsLogDestLoading = true;
        LogDestError     = null;
        try
        {
            var items = await _csvService.ReadAsync<LogDestination>("kernel/csv/log_destinations.csv");
            LogDestinations = new ObservableCollection<LogDestination>(items);
        }
        catch (Exception ex)
        {
            LogDestError = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsLogDestLoading = false;
        }
    }

    // ── ログ出力先: 保存 ──────────────────────────────────────────
    [RelayCommand]
    private async Task SaveLogDestAsync()
    {
        LogDestError      = null;
        LogDestSaveStatus = null;
        try
        {
            await _csvService.WriteAsync("kernel/csv/log_destinations.csv", LogDestinations);
            LogDestSaveStatus = "✓ 保存しました";
        }
        catch (Exception ex)
        {
            LogDestError = $"保存エラー: {ex.Message}";
        }
    }

    // ── プロファイル: 読み込み ────────────────────────────────────
    private async Task LoadProfilesAsync()
    {
        IsProfilesLoading = true;
        ProfilesError     = null;
        try
        {
            var items = await _profileService.GetProfilesAsync();
            Profiles        = new ObservableCollection<ProfileEntry>(items);
            SelectedProfile = Profiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ProfilesError = $"プロファイル一覧の読み込みに失敗: {ex.Message}";
        }
        finally
        {
            IsProfilesLoading = false;
        }
    }
}
