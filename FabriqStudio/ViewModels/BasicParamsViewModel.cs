using System.Collections.ObjectModel;
using System.ComponentModel;
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
///
/// ロック機構 / Dirty 検知 / 保存 は HostDetailViewModel / ModuleDetailViewModel と統一。
///   IsLocked=true（初期値）→ DataGrid が読み取り専用・行追加削除不可
///   IsLocked=false          → 編集可能
///   Dirty: CollectionChanged（行追加/削除）＋ 各アイテムの PropertyChanged（セル編集）で検知
///   SaveCommand: CanExecute = IsDirty のとき有効（ボタンが青くなる）
/// </summary>
public partial class BasicParamsViewModel : ObservableObject
{
    private readonly ICsvService     _csvService;
    private readonly IProfileService _profileService;

    // ─── 作業者 ────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<WorkerEntry> _workers         = [];
    [ObservableProperty] private bool                              _isWorkersLoading;
    [ObservableProperty] private string?                           _workersError;
    [ObservableProperty] private string?                           _workersSaveStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkersEditable))]
    private bool _isWorkersLocked = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveWorkersCommand))]
    private bool _isWorkersDirty;

    /// <summary>IsWorkersLocked の逆数。DataGrid の CanUserAddRows/CanUserDeleteRows にバインド。</summary>
    public bool IsWorkersEditable => !IsWorkersLocked;

    // ─── ログ出力先 ────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<LogDestination> _logDestinations = [];
    [ObservableProperty] private bool                                 _isLogDestLoading;
    [ObservableProperty] private string?                              _logDestError;
    [ObservableProperty] private string?                              _logDestSaveStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLogDestEditable))]
    private bool _isLogDestLocked = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLogDestCommand))]
    private bool _isLogDestDirty;

    /// <summary>IsLogDestLocked の逆数。DataGrid の CanUserAddRows/CanUserDeleteRows にバインド。</summary>
    public bool IsLogDestEditable => !IsLogDestLocked;

    // ─── プロファイル ──────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ProfileEntry>       _profiles        = [];
    [ObservableProperty] private ProfileEntry?                            _selectedProfile;
    [ObservableProperty] private bool                                     _isProfilesLoading;
    [ObservableProperty] private string?                                  _profilesError;

    // ─── プロファイル モジュールリスト ─────────────────────────────
    [ObservableProperty] private ObservableCollection<ProfileScriptEntry> _profileModules  = [];
    [ObservableProperty] private bool                                     _isModulesLoading;
    [ObservableProperty] private string?                                  _modulesError;

    public BasicParamsViewModel(ICsvService csvService, IProfileService profileService)
    {
        _csvService     = csvService;
        _profileService = profileService;
        _ = LoadAllAsync();
    }

    // ── 初期ロード（全セクション並列）─────────────────────────────────
    private Task LoadAllAsync()
        => Task.WhenAll(LoadWorkersAsync(), LoadLogDestAsync(), LoadProfilesAsync());

    // ── 作業者: 読み込み ────────────────────────────────────────────
    private async Task LoadWorkersAsync()
    {
        IsWorkersLoading = true;
        WorkersError     = null;
        IsWorkersDirty   = false;
        try
        {
            var items      = await _csvService.ReadAsync<WorkerEntry>("kernel/csv/workers.csv");
            var collection = new ObservableCollection<WorkerEntry>(items);
            SubscribeWorkersDirty(collection);
            Workers = collection;
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

    private void SubscribeWorkersDirty(ObservableCollection<WorkerEntry> collection)
    {
        // 既存アイテムのセル編集を検知
        foreach (var item in collection)
            item.PropertyChanged += OnWorkerItemChanged;

        // 行追加・削除を検知し、追加アイテムにも購読を付ける
        collection.CollectionChanged += (_, e) =>
        {
            IsWorkersDirty = true;
            if (e.NewItems is not null)
                foreach (WorkerEntry w in e.NewItems)
                    w.PropertyChanged += OnWorkerItemChanged;
        };
    }

    private void OnWorkerItemChanged(object? sender, PropertyChangedEventArgs e)
        => IsWorkersDirty = true;

    // ── 作業者: 保存 ────────────────────────────────────────────────
    private bool CanSaveWorkers() => IsWorkersDirty;

    [RelayCommand(CanExecute = nameof(CanSaveWorkers))]
    private async Task SaveWorkersAsync()
    {
        WorkersError      = null;
        WorkersSaveStatus = null;
        try
        {
            await _csvService.WriteAsync("kernel/csv/workers.csv", Workers);
            IsWorkersDirty    = false;
            WorkersSaveStatus = "✓ 保存しました";
        }
        catch (Exception ex)
        {
            WorkersError = $"保存エラー: {ex.Message}";
        }
    }

    // ── ログ出力先: 読み込み ────────────────────────────────────────
    private async Task LoadLogDestAsync()
    {
        IsLogDestLoading = true;
        LogDestError     = null;
        IsLogDestDirty   = false;
        try
        {
            var items      = await _csvService.ReadAsync<LogDestination>("kernel/csv/log_destinations.csv");
            var collection = new ObservableCollection<LogDestination>(items);
            SubscribeLogDestDirty(collection);
            LogDestinations = collection;
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

    private void SubscribeLogDestDirty(ObservableCollection<LogDestination> collection)
    {
        foreach (var item in collection)
            item.PropertyChanged += OnLogDestItemChanged;

        collection.CollectionChanged += (_, e) =>
        {
            IsLogDestDirty = true;
            if (e.NewItems is not null)
                foreach (LogDestination d in e.NewItems)
                    d.PropertyChanged += OnLogDestItemChanged;
        };
    }

    private void OnLogDestItemChanged(object? sender, PropertyChangedEventArgs e)
        => IsLogDestDirty = true;

    // ── ログ出力先: 保存 ────────────────────────────────────────────
    private bool CanSaveLogDest() => IsLogDestDirty;

    [RelayCommand(CanExecute = nameof(CanSaveLogDest))]
    private async Task SaveLogDestAsync()
    {
        LogDestError      = null;
        LogDestSaveStatus = null;
        try
        {
            await _csvService.WriteAsync("kernel/csv/log_destinations.csv", LogDestinations);
            IsLogDestDirty    = false;
            LogDestSaveStatus = "✓ 保存しました";
        }
        catch (Exception ex)
        {
            LogDestError = $"保存エラー: {ex.Message}";
        }
    }

    // ── プロファイル一覧: 読み込み ──────────────────────────────────
    private async Task LoadProfilesAsync()
    {
        IsProfilesLoading = true;
        ProfilesError     = null;
        try
        {
            var items = await _profileService.GetProfilesAsync();
            Profiles        = new ObservableCollection<ProfileEntry>(items);
            SelectedProfile = Profiles.FirstOrDefault();
            // SelectedProfile のセットが OnSelectedProfileChanged を発火し
            // モジュールリストの読み込みが自動的に開始される
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

    // ── プロファイル選択変更時: モジュールリストを自動更新 ──────────
    partial void OnSelectedProfileChanged(ProfileEntry? value)
    {
        ProfileModules.Clear();
        ModulesError = null;
        if (value is not null)
            _ = LoadProfileModulesAsync(value);
    }

    // ── プロファイル モジュールリスト: 読み込み ─────────────────────
    private async Task LoadProfileModulesAsync(ProfileEntry profile)
    {
        IsModulesLoading = true;
        ModulesError     = null;
        try
        {
            var items = await _profileService.GetProfileModulesAsync(profile);
            ProfileModules = new ObservableCollection<ProfileScriptEntry>(items);
        }
        catch (Exception ex)
        {
            ModulesError = $"モジュールリストの読み込みに失敗: {ex.Message}";
        }
        finally
        {
            IsModulesLoading = false;
        }
    }
}
