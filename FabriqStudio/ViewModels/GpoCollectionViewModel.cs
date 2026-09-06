using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Services;
using FabriqStudio.Services.Gpo;
using FabriqStudio.ViewModels.Gpo;

namespace FabriqStudio.ViewModels;

/// <summary>
/// GPO 辞書画面。ADMX から生成した辞書を検索し、状態・要素を決めて gpo_config/gpo_list.csv へ書き出す。
/// 辞書の読み込み元（ADMX フォルダー）の切り替えもここで行う。IGpoCatalogService はワークスペース非依存。
/// </summary>
public partial class GpoCollectionViewModel : ObservableObject
{
    private readonly IGpoCatalogService _service;
    private readonly IGpoExportService  _export;
    private readonly IWorkspaceService  _workspace;

    public GpoBrowserViewModel      Browser { get; }
    public GpoPolicyConfigViewModel Config  { get; } = new();

    [ObservableProperty] private string  _sourcePath  = "";
    [ObservableProperty] private string  _catalogInfo = "";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportHint))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isWorkspaceOpen;

    public string ExportHint => IsWorkspaceOpen
        ? $"右の状態・オプションで {_export.RelPath} に行を追加します（同じポリシーの既存行は置き換え。Segment 付きの行は触りません）。"
        : "ワークスペースを開くと gpo_list.csv へ書き出せます。";

    public GpoCollectionViewModel(IGpoCatalogService service, IGpoExportService export, IWorkspaceService workspace)
    {
        _service   = service;
        _export    = export;
        _workspace = workspace;
        Browser    = new GpoBrowserViewModel(service);

        Browser.PropertyChanged += OnBrowserPropertyChanged;
        Config.Changed          += () => ExportCommand.NotifyCanExecuteChanged();

        IsWorkspaceOpen = workspace.IsOpen;
        workspace.WorkspaceChanged += (_, e) => IsWorkspaceOpen = e.NewPath is not null;

        SourcePath = service.SourcePath;
        service.CatalogChanged += (_, _) => RunOnUi(OnCatalogChanged);
        OnCatalogChanged();
    }

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GpoBrowserViewModel.SelectedPolicy)) return;
        Config.Load(Browser.SelectedPolicy, null);
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void OnCatalogChanged()
    {
        IsLoading    = _service.IsLoading;
        ErrorMessage = _service.LoadError;
        CatalogInfo  = _service.Catalog?.VersionTag ?? (IsLoading ? "ADMX を読み込み中..." : "未読込");
        if (!IsLoading) SourcePath = _service.SourcePath;
        Browser.RefreshCatalogState();
    }

    // ── ADMX の場所 ───────────────────────────────────────────────

    private bool CanReload() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        StatusMessage = null;
        ErrorMessage  = null;
        await _service.ReloadAsync(SourcePath);
        if (_service.IsLoaded) StatusMessage = $"✓ 読み込みました（{_service.Catalog!.Policies.Count} ポリシー）";
    }

    [RelayCommand]
    private void BrowseSource()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title         = "ADMX（PolicyDefinitions）フォルダーを選択してください",
            InitialDirectory = SourcePath,
        };
        if (dialog.ShowDialog() != true) return;
        SourcePath = dialog.FolderName;
    }

    [RelayCommand]
    private void UseDefaultSource() => SourcePath = _service.DefaultSourcePath;

    // ── エクスポート ─────────────────────────────────────────────

    private bool CanExport()
        => IsWorkspaceOpen && Config.HasPolicy && !Config.HasErrors && Config.PreviewRows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_workspace.RootPath is null) return;
        StatusMessage = null;
        ErrorMessage  = null;

        var rows   = Config.PreviewRows.ToList();
        var result = await _export.ExportAsync(_workspace.RootPath, rows);
        if (result.Succeeded)
            StatusMessage = $"✓ {result.RelPath} に {result.Added} 行を追加しました"
                            + (result.Replaced > 0 ? $"（既存 {result.Replaced} 行を置き換え）" : "");
        else
            ErrorMessage = $"書き出し失敗: {result.Error}";
    }

    private static void RunOnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }
}
