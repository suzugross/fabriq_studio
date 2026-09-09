using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Helpers;

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
public partial class PrinterDriverDetectorViewModel : ObservableObject, IDataSetDependentViewModel
{
    private const string ModuleDir = "printer_driver_config";

    private readonly IPrinterDriverDetectorService _service;
    private readonly IWorkspaceService             _workspace;
    private readonly IModuleDataResolver           _resolver;
    private readonly IDataSetContext               _dataSet;
    private readonly IProfileDataService           _profileData;

    /// <summary>自動投入したスキャン先（操作者が手で変えていないときだけ追従して差し替える）。</summary>
    private string? _autoScanPath;

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

    // ── 編集先データセット（PDF）と INF の所在 ─────────────────────
    // INF/ は資材フォルダ = フォルダ単位 all-or-nothing。プロファイルのデータフォルダに INF/ があれば、本体の INF は
    // そのプロファイルでは一切使われない。中間は無いので、二択（作る / 本体の共通庫を使う）を明示的に切り替える。

    public string DataSetLabel    => DataSetText.WriteTarget(_dataSet.Current);
    public bool   IsProfileTarget => _dataSet.Current is not null;

    /// <summary>データフォルダ側に INF/ があるか（IsProfileTarget のときだけ意味を持つ）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateProfileInf))]
    [NotifyPropertyChangedFor(nameof(CanRemoveProfileInf))]
    private bool _hasProfileInf;

    /// <summary>INF の所在の説明（本体の共通庫 / データフォルダ）。</summary>
    [ObservableProperty] private string _infStatus = "";

    public bool CanCreateProfileInf => IsProfileTarget && !HasProfileInf;
    public bool CanRemoveProfileInf => IsProfileTarget && HasProfileInf;

    /// <summary>確認ダイアログ（テストで差し替え可能）。既定は MessageBox。</summary>
    public Func<string, string, bool> ConfirmAction { get; set; } =
        (message, title) => MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    public PrinterDriverDetectorViewModel(
        IPrinterDriverDetectorService service,
        IWorkspaceService             workspace,
        IModuleDataResolver           resolver,
        IDataSetContext               dataSet,
        IProfileDataService           profileData)
    {
        _service        = service;
        _workspace      = workspace;
        _resolver       = resolver;
        _dataSet        = dataSet;
        _profileData    = profileData;
        IsWorkspaceOpen = workspace.IsOpen;

        RefreshDataSetState();
        TryFillDefaultScanPath();

        // ワークスペース切替時に既定スキャン先を更新し、エクスポートの有効状態も反映
        workspace.WorkspaceChanged += (_, _) =>
        {
            IsWorkspaceOpen = _workspace.IsOpen;
            RefreshDataSetState();
            TryFillDefaultScanPath();
        };
        dataSet.Changed += (_, _) =>
        {
            RefreshDataSetState();
            TryFillDefaultScanPath();
        };
    }

    /// <summary>INF/ の解決結果（編集先データセットに INF/ があればそちら、無ければ本体。モジュールが無ければ null）。</summary>
    private string? InfDir()
    {
        var rel = _resolver.ModuleRelPath(ModuleDir, "INF");
        return rel is null ? null : _resolver.ResolveRead(rel, _dataSet.Current).AbsPath;
    }

    /// <summary>データフォルダ側の INF/ の絶対パス（存在しなくても返す。モジュールが無い／本体選択中は null）。</summary>
    private string? ProfileInfDir()
    {
        var rel = _resolver.ModuleRelPath(ModuleDir, "INF");
        return rel is null || _dataSet.Current is null ? null : _resolver.ResolveWrite(rel, _dataSet.Current).AbsPath;
    }

    private void RefreshDataSetState()
    {
        OnPropertyChanged(nameof(DataSetLabel));
        OnPropertyChanged(nameof(IsProfileTarget));

        var profileInf = _workspace.IsOpen ? ProfileInfDir() : null;
        HasProfileInf  = profileInf is not null && Directory.Exists(profileInf);
        OnPropertyChanged(nameof(CanCreateProfileInf));
        OnPropertyChanged(nameof(CanRemoveProfileInf));

        InfStatus = !_workspace.IsOpen ? ""
            : !IsProfileTarget ? "INF: 本体（printer_driver_config/INF/）"
            : HasProfileInf    ? $"INF: プロファイル {_dataSet.Current} のデータフォルダ側（本体の INF はこのプロファイルでは使われません）"
                               : $"INF: 本体の共通庫を使用（プロファイル {_dataSet.Current} のデータフォルダに INF/ が無いため。実行時はフォールバック警告が出ますが正常に動きます）";
    }

    /// <summary>
    /// ワークスペースが開いていて、プリンタドライバモジュールの INF フォルダが存在する場合のみ
    /// <see cref="ScanPath"/> を自動投入する。手動指定中のケースは上書きしない。
    /// </summary>
    private void TryFillDefaultScanPath()
    {
        // 操作者が手で変えたスキャン先は上書きしない（自動投入した値のままなら追従して差し替える）
        if (!string.IsNullOrWhiteSpace(ScanPath) && ScanPath != _autoScanPath) return;
        if (_workspace.RootPath is null) return;

        var candidate = InfDir();
        if (candidate is not null && Directory.Exists(candidate))
        {
            ScanPath      = candidate;
            _autoScanPath = candidate;
        }
    }

    /// <summary>ワークスペースから 7z.exe のパスを解決する（tools/ はフレームワーク資産なので常に本体。未配置時は null）。</summary>
    private string? ResolveWorkspaceSevenZipPath()
    {
        if (_workspace.RootPath is null) return null;
        var rel = _resolver.ModuleRelPath(ModuleDir, "tools/7z.exe");
        if (rel is null) return null;
        var path = _resolver.ResolveRead(rel, null).AbsPath;
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

    // ── コマンド: データフォルダ側の INF/（二択の切り替え）────────

    /// <summary>プロファイルのデータフォルダに INF/ を作る（= この案件のドライバを全部そちらに置く選択）。</summary>
    [RelayCommand]
    private void CreateProfileInf()
    {
        var dir = ProfileInfDir();
        if (dir is null || _dataSet.Current is null || Directory.Exists(dir)) return;

        var bodyInf   = _resolver.ModuleRelPath(ModuleDir, "INF") is { } rel ? _resolver.ResolveRead(rel, null).AbsPath : null;
        var bodyCount = bodyInf is not null && Directory.Exists(bodyInf) ? Directory.EnumerateFileSystemEntries(bodyInf).Count() : 0;
        var ok = ConfirmAction(
            $"プロファイル {_dataSet.Current} のデータフォルダに INF/ を作ります。\n\n" +
            $"作ると、本体の INF（{bodyCount} 件）はこのプロファイルの実行では一切使われなくなります" +
            "（フォルダ単位で切り替わり、足りない分を本体から補うことはありません）。\n" +
            "この案件で使うドライバは、すべてデータフォルダ側の INF/ に置いてください。\n\n" +
            "本体の共通庫をそのまま使う場合は作らないでください（キャンセル）。",
            "データフォルダに INF/ を作成");
        if (!ok) return;

        try
        {
            Directory.CreateDirectory(dir);
            ScanPath      = dir;
            _autoScanPath = dir;
            RefreshDataSetState();
            ErrorMessage  = null;
            StatusMessage = $"データフォルダに INF/ を作りました: {dir}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"INF/ の作成に失敗: {ex.Message}";
        }
    }

    /// <summary>データフォルダ側の INF/ を削除して、本体の共通庫を使う状態に戻す。</summary>
    [RelayCommand]
    private void RemoveProfileInf()
    {
        var dir = ProfileInfDir();
        if (dir is null || _dataSet.Current is null || !Directory.Exists(dir)) return;

        var count = Directory.EnumerateFileSystemEntries(dir).Count();
        var ok = ConfirmAction(
            $"プロファイル {_dataSet.Current} のデータフォルダの INF/（{count} 件）を削除して、本体の共通庫を使う状態に戻します。\n元に戻せません。よろしいですか？",
            "データフォルダの INF/ を削除");
        if (!ok) return;

        try
        {
            Directory.Delete(dir, recursive: true);
            if (ScanPath == dir) { ScanPath = ""; _autoScanPath = null; }
            RefreshDataSetState();
            TryFillDefaultScanPath();
            ErrorMessage  = null;
            StatusMessage = "データフォルダの INF/ を削除しました（本体の共通庫を使います）";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"INF/ の削除に失敗: {ex.Message}";
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
            var rel = _resolver.ModuleRelPath(ModuleDir, "printer_driver_list.csv");
            if (rel is null)
            {
                ErrorMessage = $"モジュール {ModuleDir} がワークスペースにありません。";
                return;
            }

            // 編集先データセット（PDF）が選ばれていれば、本体から as-is で取り込んでから PDF 側に追記する
            var dataSet = _dataSet.Current;
            if (dataSet is not null)
                await _profileData.MaterializeCsvAsync(dataSet, ModuleDir, "printer_driver_list.csv");
            var target = _resolver.ResolveWrite(rel, dataSet);

            var result = await _service.ExportToCsvAsync(SelectedDriver, target.AbsPath);

            StatusMessage = result switch
            {
                { Error: not null } => null,
                { Skipped: > 0    } =>
                    $"「{SelectedDriver.DriverName}」は既に {target.RelPath} に登録済みです。",
                _ =>
                    $"{target.RelPath} に追加しました: {SelectedDriver.DriverName}",
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
