using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// プリンタドライバ検出画面の ViewModel。
/// 指定フォルダを <see cref="IPrinterDriverDetectorService"/> でスキャンし、
/// 抽出された <see cref="PrinterDriverInfo"/> を一覧表示する。
/// <para>
/// ワークスペース非依存（外部フォルダもスキャン可能）。
/// ワークスペースが開いている場合は初期値としてそのプリンタドライバモジュールの
/// <c>INF/</c> フォルダを自動投入する。
/// </para>
/// </summary>
public partial class PrinterDriverDetectorViewModel : ObservableObject
{
    private readonly IPrinterDriverDetectorService _service;
    private readonly IWorkspaceService             _workspace;

    /// <summary>ワークスペース内のプリンタドライバモジュールの INF フォルダ相対パス。</summary>
    private const string WorkspaceInfRelPath = @"modules\standard\printer_driver_config\INF";

    // ── スキャン対象 ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _scanPath = "";

    // ── 結果 ─────────────────────────────────────────────────────

    public ObservableCollection<PrinterDriverInfo> Drivers { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyDriverNameCommand))]
    private PrinterDriverInfo? _selectedDriver;

    // ── 状態 ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    private bool _isScanning;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    public PrinterDriverDetectorViewModel(
        IPrinterDriverDetectorService service,
        IWorkspaceService             workspace)
    {
        _service   = service;
        _workspace = workspace;

        // 初期値: ワークスペースの既定 INF フォルダ（存在すれば）
        TryFillDefaultScanPath();

        // ワークスペース切替時に既定スキャン先を更新
        workspace.WorkspaceChanged += (_, _) => TryFillDefaultScanPath();
    }

    /// <summary>
    /// ワークスペースが開いていて、プリンタドライバモジュールの INF フォルダが存在する場合のみ
    /// <see cref="ScanPath"/> を自動投入する。外部フォルダを手動指定中のケースは上書きしない。
    /// </summary>
    private void TryFillDefaultScanPath()
    {
        if (!string.IsNullOrWhiteSpace(ScanPath)) return;
        if (_workspace.RootPath is null) return;

        var candidate = Path.Combine(_workspace.RootPath, WorkspaceInfRelPath);
        if (Directory.Exists(candidate))
            ScanPath = candidate;
    }

    // ── コマンド: フォルダ選択 ───────────────────────────────────

    private bool CanBrowse() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title             = "プリンタドライバの INF を含むフォルダを選択",
            InitialDirectory  = Directory.Exists(ScanPath) ? ScanPath : "",
        };
        if (dialog.ShowDialog() == true)
            ScanPath = dialog.FolderName;
    }

    // ── コマンド: スキャン実行 ───────────────────────────────────

    private bool CanScan() => !IsScanning && !string.IsNullOrWhiteSpace(ScanPath);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning    = true;
        ErrorMessage  = null;
        StatusMessage = "スキャン中...";
        Drivers.Clear();

        try
        {
            var results = await _service.ScanAsync(ScanPath);
            foreach (var r in results)
                Drivers.Add(r);

            StatusMessage = results.Count == 0
                ? "有効なプリンタドライバ INF は見つかりませんでした。"
                : $"{results.Count} 件のドライバを検出しました。";
        }
        catch (Exception ex)
        {
            ErrorMessage  = ex.Message;
            StatusMessage = null;
        }
        finally
        {
            IsScanning = false;
        }
    }

    // ── コマンド: ドライバ名をクリップボードへコピー ─────────────

    private bool CanCopyDriverName() => SelectedDriver is not null;

    [RelayCommand(CanExecute = nameof(CanCopyDriverName))]
    private void CopyDriverName(PrinterDriverInfo? info)
    {
        var target = info ?? SelectedDriver;
        if (target is null) return;

        try
        {
            Clipboard.SetText(target.DriverName);
            StatusMessage = $"「{target.DriverName}」をクリップボードにコピーしました。";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"クリップボードへのコピーに失敗: {ex.Message}";
        }
    }
}
