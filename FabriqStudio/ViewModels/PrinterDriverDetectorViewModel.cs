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
/// Phase 2:
/// <list type="bullet">
///   <item>スキャン前に .exe/.zip を自動展開（ワークスペース内 7z.exe を利用）</item>
///   <item>選択行を workspace の <c>printer_driver_list.csv</c> に追記</item>
/// </list>
/// </para>
/// <para>
/// ワークスペース非依存（外部フォルダもスキャン可能）。
/// ワークスペースが開いている場合は初期値としてそのプリンタドライバモジュールの
/// <c>INF/</c> フォルダを自動投入する。エクスポートのみワークスペース必須。
/// </para>
/// </summary>
public partial class PrinterDriverDetectorViewModel : ObservableObject
{
    private readonly IPrinterDriverDetectorService _service;
    private readonly IWorkspaceService             _workspace;

    /// <summary>ワークスペース内のプリンタドライバモジュールの INF フォルダ相対パス。</summary>
    private const string WorkspaceInfRelPath =
        @"modules\standard\printer_driver_config\INF";

    /// <summary>ワークスペース内の 7z.exe 相対パス。アーカイブ展開で使用。</summary>
    private const string WorkspaceSevenZipRelPath =
        @"modules\standard\printer_driver_config\tools\7z.exe";

    // ── スキャン対象 ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _scanPath = "";

    /// <summary>スキャン前に .exe / .zip を自動展開するか。既定 ON。</summary>
    [ObservableProperty] private bool _autoExtractArchives = true;

    // ── 結果 ─────────────────────────────────────────────────────

    public ObservableCollection<PrinterDriverInfo> Drivers { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyDriverNameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private PrinterDriverInfo? _selectedDriver;

    // ── 状態 ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isScanning;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>ワークスペースの状態（エクスポートの CanExecute 制御用）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isWorkspaceOpen;

    public PrinterDriverDetectorViewModel(
        IPrinterDriverDetectorService service,
        IWorkspaceService             workspace)
    {
        _service        = service;
        _workspace      = workspace;
        IsWorkspaceOpen = workspace.IsOpen;

        TryFillDefaultScanPath();

        // ワークスペース切替時に既定スキャン先を更新し、エクスポートの有効状態も反映
        workspace.WorkspaceChanged += (_, _) =>
        {
            IsWorkspaceOpen = _workspace.IsOpen;
            TryFillDefaultScanPath();
        };
    }

    /// <summary>
    /// ワークスペースが開いていて、プリンタドライバモジュールの INF フォルダが存在する場合のみ
    /// <see cref="ScanPath"/> を自動投入する。手動指定中のケースは上書きしない。
    /// </summary>
    private void TryFillDefaultScanPath()
    {
        if (!string.IsNullOrWhiteSpace(ScanPath)) return;
        if (_workspace.RootPath is null) return;

        var candidate = Path.Combine(_workspace.RootPath, WorkspaceInfRelPath);
        if (Directory.Exists(candidate))
            ScanPath = candidate;
    }

    /// <summary>ワークスペースから 7z.exe のパスを解決する（未配置時は null）。</summary>
    private string? ResolveWorkspaceSevenZipPath()
    {
        if (_workspace.RootPath is null) return null;
        var path = Path.Combine(_workspace.RootPath, WorkspaceSevenZipRelPath);
        return File.Exists(path) ? path : null;
    }

    // ── コマンド: フォルダ選択 ───────────────────────────────────

    private bool CanBrowse() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title            = "プリンタドライバの INF を含むフォルダを選択",
            InitialDirectory = Directory.Exists(ScanPath) ? ScanPath : "",
        };
        if (dialog.ShowDialog() == true)
            ScanPath = dialog.FolderName;
    }

    // ── コマンド: スキャン実行（+ オプションで自動展開）──────────

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
            // Phase 2: アーカイブ自動展開
            if (AutoExtractArchives)
            {
                StatusMessage = "アーカイブを展開中...";
                var sevenZip = ResolveWorkspaceSevenZipPath();
                var ex = await _service.ExtractArchivesAsync(ScanPath, sevenZip);

                if (ex.Extracted + ex.Skipped + ex.Failed > 0)
                {
                    var parts = new List<string>();
                    if (ex.Extracted > 0) parts.Add($"展開 {ex.Extracted} 件");
                    if (ex.Skipped   > 0) parts.Add($"スキップ {ex.Skipped} 件");
                    if (ex.Failed    > 0) parts.Add($"失敗 {ex.Failed} 件");
                    StatusMessage = $"アーカイブ: {string.Join(" / ", parts)}。スキャン中...";
                }
            }

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

    // ── コマンド: DriverName をクリップボードへコピー ──────────

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

    // ── コマンド: printer_driver_list.csv へエクスポート (Phase 2) ─

    private bool CanExport()
        => SelectedDriver is not null && IsWorkspaceOpen && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (SelectedDriver is null || _workspace.RootPath is null) return;

        ErrorMessage  = null;
        StatusMessage = null;

        try
        {
            var result = await _service.ExportToWorkspaceAsync(SelectedDriver, _workspace.RootPath);

            StatusMessage = result switch
            {
                { Error: not null } => null,
                { Skipped: > 0    } =>
                    $"「{SelectedDriver.DriverName}」は既に printer_driver_list.csv に登録済みです。",
                _ =>
                    $"printer_driver_list.csv に追加しました: {SelectedDriver.DriverName}",
            };

            if (result.Error is not null)
                ErrorMessage = $"エクスポート失敗: {result.Error}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"エクスポート失敗: {ex.Message}";
        }
    }
}
