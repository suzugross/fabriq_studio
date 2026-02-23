using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

public partial class BasicParamsViewModel : ObservableObject
{
    private readonly ICsvService     _csvService;
    private readonly IProfileService _profileService;

    [ObservableProperty] private ObservableCollection<WorkerEntry> _workers         = [];
    [ObservableProperty] private bool                              _isWorkersLoading;
    [ObservableProperty] private string?                           _workersError;
    [ObservableProperty] private string?                           _workersSaveStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkersEditable))]
    [NotifyCanExecuteChangedFor(nameof(SaveWorkersCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddWorkerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteWorkerCommand))]
    private bool _isWorkersLocked = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveWorkersCommand))]
    private bool _isWorkersDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteWorkerCommand))]
    private WorkerEntry? _selectedWorker;

    public bool IsWorkersEditable => !IsWorkersLocked;

    [ObservableProperty] private ObservableCollection<LogDestination> _logDestinations = [];
    [ObservableProperty] private bool                                 _isLogDestLoading;
    [ObservableProperty] private string?                              _logDestError;
    [ObservableProperty] private string?                              _logDestSaveStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLogDestEditable))]
    [NotifyCanExecuteChangedFor(nameof(SaveLogDestCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddLogDestCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteLogDestinationCommand))]
    private bool _isLogDestLocked = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLogDestCommand))]
    private bool _isLogDestDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteLogDestinationCommand))]
    private LogDestination? _selectedLogDest;

    public bool IsLogDestEditable => !IsLogDestLocked;

    [ObservableProperty] private ObservableCollection<ProfileEntry>       _profiles        = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenProfileEditorCommand))]
    private ProfileEntry? _selectedProfile;

    [ObservableProperty] private bool    _isProfilesLoading;
    [ObservableProperty] private string? _profilesError;

    // ─── プロファイル新規作成 ─────────────────────────────────────
    [ObservableProperty] private bool    _isCreatingProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCreateProfileCommand))]
    private string _newProfileName = "";

    [ObservableProperty] private string? _createProfileError;

    [ObservableProperty] private ObservableCollection<ProfileScriptEntry> _profileModules  = [];
    [ObservableProperty] private bool                                     _isModulesLoading;
    [ObservableProperty] private string?                                  _modulesError;

    public BasicParamsViewModel(ICsvService csvService, IProfileService profileService, IWorkspaceService workspace)
    {
        _csvService     = csvService;
        _profileService = profileService;
        workspace.WorkspaceChanged += (_, e) =>
        {
            if (e.NewPath is null) { ClearAll(); return; }
            _ = LoadAllAsync();
        };
        if (workspace.IsOpen)
            _ = LoadAllAsync();

        // 詳細画面での保存完了を受信してデータを自動リフレッシュ
        WeakReferenceMessenger.Default.Register<WorkspaceDataUpdatedMessage>(this, (_, msg) =>
        {
            _ = OnWorkspaceDataUpdatedAsync(msg.Value);
        });
    }

    /// <summary>
    /// 詳細画面で保存が完了したとき、該当セクションのデータをリロードする。
    /// SelectedProfile は名前で復元する。
    /// </summary>
    private async Task OnWorkspaceDataUpdatedAsync(string source)
    {
        switch (source)
        {
            case "ProfileDetail":
                var profileName = SelectedProfile?.Name;
                await LoadProfilesAsync();
                if (profileName is not null)
                    SelectedProfile = Profiles.FirstOrDefault(p => p.Name == profileName);
                break;

            case "HostDetail":
                // ホスト一覧は BasicParams に直接表示していないが、将来拡張に備える
                break;

            case "ModuleDetail":
                // プロファイルモジュール構成が変わった可能性があるため再読み込み
                if (SelectedProfile is not null)
                    _ = LoadProfileModulesAsync(SelectedProfile);
                break;
        }
    }

    private Task LoadAllAsync()
        => Task.WhenAll(LoadWorkersAsync(), LoadLogDestAsync(), LoadProfilesAsync());

    private void ClearAll()
    {
        Workers.Clear();
        LogDestinations.Clear();
        Profiles.Clear();
        ProfileModules.Clear();
    }

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
        foreach (var item in collection)
            item.PropertyChanged += OnWorkerItemChanged;

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

    private bool CanAddWorker() => !IsWorkersLocked;

    [RelayCommand(CanExecute = nameof(CanAddWorker))]
    private void AddWorker() => Workers.Add(new WorkerEntry());

    private bool CanDeleteWorker() => SelectedWorker is not null && !IsWorkersLocked;

    [RelayCommand(CanExecute = nameof(CanDeleteWorker))]
    private void DeleteWorker()
    {
        if (SelectedWorker is null) return;
        Workers.Remove(SelectedWorker);
        SelectedWorker = null;
    }

    private bool CanSaveWorkers() => IsWorkersDirty && !IsWorkersLocked;

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

    private bool CanAddLogDest() => !IsLogDestLocked;

    [RelayCommand(CanExecute = nameof(CanAddLogDest))]
    private void AddLogDest() => LogDestinations.Add(new LogDestination());

    private bool CanDeleteLogDestination() => SelectedLogDest is not null && !IsLogDestLocked;

    [RelayCommand(CanExecute = nameof(CanDeleteLogDestination))]
    private void DeleteLogDestination()
    {
        if (SelectedLogDest is null) return;
        LogDestinations.Remove(SelectedLogDest);
        SelectedLogDest = null;
    }

    private bool CanSaveLogDest() => IsLogDestDirty && !IsLogDestLocked;

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

    partial void OnSelectedProfileChanged(ProfileEntry? value)
    {
        ProfileModules.Clear();
        ModulesError = null;
        if (value is not null)
            _ = LoadProfileModulesAsync(value);
    }

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

    // ─── プロファイル編集画面へ遷移 ──────────────────────────────
    private bool CanOpenProfileEditor() => SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanOpenProfileEditor))]
    private void OpenProfileEditor()
    {
        if (SelectedProfile is null) return;
        WeakReferenceMessenger.Default.Send(new ShowProfileDetailMessage(SelectedProfile));
    }

    // ─── プロファイル新規作成 ─────────────────────────────────────

    /// <summary>入力フォームを表示してテキストをクリアする。</summary>
    [RelayCommand]
    private void BeginCreateProfile()
    {
        CreateProfileError = null;
        NewProfileName     = "";
        IsCreatingProfile  = true;
    }

    /// <summary>入力フォームを非表示にしてテキストをクリアする。</summary>
    [RelayCommand]
    private void CancelCreateProfile()
    {
        IsCreatingProfile  = false;
        NewProfileName     = "";
        CreateProfileError = null;
    }

    private bool CanConfirmCreateProfile() => !string.IsNullOrWhiteSpace(NewProfileName);

    /// <summary>プロファイルを作成し、一覧をリフレッシュして新しいプロファイルを自動選択する。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmCreateProfile))]
    private async Task ConfirmCreateProfileAsync()
    {
        CreateProfileError = null;
        try
        {
            // ファイル作成（バリデーション含む）
            var newProfile = await _profileService.CreateProfileAsync(NewProfileName.Trim());

            // プロファイル一覧をリフレッシュ
            var items = await _profileService.GetProfilesAsync();
            Profiles = new ObservableCollection<ProfileEntry>(items);

            // 作成したばかりのプロファイルを自動選択
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == newProfile.Name);

            // フォームを閉じる
            IsCreatingProfile = false;
            NewProfileName    = "";
        }
        catch (Exception ex)
        {
            // バリデーション失敗・同名ファイル存在・IO エラーをフォーム内に表示
            CreateProfileError = ex.Message;
        }
    }
}
