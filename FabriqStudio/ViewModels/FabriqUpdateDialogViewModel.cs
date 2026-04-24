using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 「Update fabriq from template」ダイアログの ViewModel。
/// <list type="bullet">
///   <item>Step 1: Template / Target パス指定</item>
///   <item>Step 2: ComputePlan コマンドで plan 計算</item>
///   <item>Step 3: Preview DataGrid（<see cref="PlanItems"/>）でチェック</item>
///   <item>Step 4: Preflight 結果を <see cref="PreflightMessage"/> に反映</item>
///   <item>Step 5: Apply / DryRun 実行</item>
///   <item>Step 6: <see cref="ReportText"/> に結果表示</item>
/// </list>
/// </summary>
public partial class FabriqUpdateDialogViewModel : ObservableObject
{
    private readonly IFabriqUpdateService _service;

    public FabriqUpdateDialogViewModel(IFabriqUpdateService service, string? initialTarget)
    {
        _service     = service;
        _targetPath  = initialTarget ?? "";
        _backupPath  = BuildDefaultBackupPath(initialTarget);
        _logPath     = BuildDefaultLogPath();
    }

    // ─── パス ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ComputePlanCommand))]
    private string _templatePath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ComputePlanCommand))]
    private string _targetPath;

    [ObservableProperty] private string _backupPath;
    [ObservableProperty] private string _logPath;

    // ─── Plan ──────────────────────────────────────────────────

    [ObservableProperty] private FabriqUpdatePlan? _plan;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DryRunCommand))]
    private bool _isPlanReady;

    public ObservableCollection<BundleUpdateItem> PlanItems { get; } = new();

    [ObservableProperty] private string? _planSummary;

    // ─── Preflight ────────────────────────────────────────────

    [ObservableProperty] private string? _preflightMessage;
    [ObservableProperty] private bool    _preflightOk;

    // ─── Execution ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ComputePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DryRunCommand))]
    private bool _isRunning;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string  _logBuffer  = "";

    [ObservableProperty] private string? _reportText;
    [ObservableProperty] private bool    _hasReport;

    // ===================================================================
    // ComputePlan
    // ===================================================================

    private bool CanComputePlan()
        => !IsRunning
        && !string.IsNullOrWhiteSpace(TemplatePath) && Directory.Exists(TemplatePath)
        && !string.IsNullOrWhiteSpace(TargetPath)   && Directory.Exists(TargetPath);

    [RelayCommand(CanExecute = nameof(CanComputePlan))]
    private async Task ComputePlanAsync()
    {
        try
        {
            IsRunning     = true;
            IsPlanReady   = false;
            StatusMessage = "Computing plan...";
            PlanItems.Clear();
            Plan          = null;
            PreflightMessage = null;
            ReportText    = null;
            HasReport     = false;

            var plan = await _service.ComputePlanAsync(TemplatePath, TargetPath);
            Plan = plan;
            foreach (var item in plan.Bundles)
                PlanItems.Add(item);

            PlanSummary = BuildPlanSummary(plan);
            RunPreflight();
            IsPlanReady = true;
            StatusMessage = "Plan ready.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Plan failed: {ex.Message}";
            MessageBox.Show($"Plan 計算に失敗しました:\n{ex.Message}",
                "Update fabriq", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void RunPreflight()
    {
        if (Plan is null) return;
        var selected = PlanItems.Where(i => i.IsSelected).ToList();
        var pre = _service.RunPreflight(Plan, selected);
        PreflightOk = pre.CanProceed;

        if (pre.CanProceed)
        {
            PreflightMessage = "✓ Preflight OK";
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("✗ Preflight に問題があります:");
            foreach (var e in pre.Errors) sb.AppendLine("  · " + e);
            foreach (var b in pre.RequiresKernelBlocks) sb.AppendLine("  · " + b);
            PreflightMessage = sb.ToString();
        }
    }

    /// <summary>
    /// チェック変更時に呼ばれる。Preflight の再評価と
    /// Apply / DryRun ボタンの CanExecute 再評価を行う。
    /// </summary>
    public void OnSelectionChanged()
    {
        RunPreflight();
        ApplyCommand.NotifyCanExecuteChanged();
        DryRunCommand.NotifyCanExecuteChanged();
    }

    // ===================================================================
    // Apply / DryRun
    // ===================================================================

    private bool CanExecute()
        => !IsRunning && IsPlanReady && Plan is not null
        && PlanItems.Any(i => i.IsSelected && !i.IsBlocked);

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private Task ApplyAsync() => RunExecutionAsync(dryRun: false);

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private Task DryRunAsync() => RunExecutionAsync(dryRun: true);

    private async Task RunExecutionAsync(bool dryRun)
    {
        if (Plan is null) return;

        // Preflight 最終チェック
        var selected = PlanItems.Where(i => i.IsSelected && !i.IsBlocked).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("対象となる bundle が選択されていません。",
                "Update fabriq", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!dryRun)
        {
            var pre = _service.RunPreflight(Plan, selected);
            if (!pre.CanProceed)
            {
                MessageBox.Show("Preflight に失敗しているため実行できません。\n" + PreflightMessage,
                    "Update fabriq", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var confirm = MessageBox.Show(
                $"以下の設定で overlay を実行します:\n\n" +
                $"Template : {TemplatePath}\n" +
                $"Target   : {TargetPath}\n" +
                $"Backup   : {BackupPath}\n" +
                $"Bundles  : {selected.Count}\n\n" +
                "続行しますか?",
                "Update fabriq", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;
        }

        try
        {
            IsRunning = true;
            StatusMessage = dryRun ? "Dry-run..." : "Applying overlay...";
            LogBuffer = "";

            var progress = new Progress<string>(line =>
            {
                LogBuffer += line + Environment.NewLine;
            });

            var request = new FabriqUpdateRequest(Plan, selected, BackupPath, dryRun, LogPath);
            var result  = await _service.ApplyAsync(request, progress);

            ReportText = BuildReportText(result, dryRun);
            HasReport  = true;
            StatusMessage = dryRun ? "Dry-run complete." : "Apply complete.";

            // スクロール可能な専用ダイアログで表示（MessageBox は長文で画面外にはみ出す）
            Views.ReportDialog.Show(
                dryRun ? "Dry-run 結果" : "Update 結果",
                ReportText,
                Application.Current.MainWindow);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            MessageBox.Show($"実行中に致命的エラーが発生しました:\n{ex.Message}",
                "Update fabriq", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsRunning = false;
        }
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static string BuildPlanSummary(FabriqUpdatePlan plan)
    {
        int upd  = plan.Bundles.Count(b => b.Action == UpdateAction.Update);
        int newC = plan.Bundles.Count(b => b.Action == UpdateAction.New);
        int same = plan.Bundles.Count(b => b.Action == UpdateAction.SkipSame);
        int tNew = plan.Bundles.Count(b => b.Action == UpdateAction.SkipTargetNewer);
        int pres = plan.Bundles.Count(b => b.Action == UpdateAction.Preserve);
        return $"kernel {plan.TargetKernel?.ToString() ?? "(none)"} -> {plan.TemplateKernel?.ToString() ?? "(none)"} | "
             + $"update={upd}, new={newC}, same={same}, target-newer={tNew}, preserved={pres}";
    }

    private static string BuildReportText(FabriqUpdateResult result, bool dryRun)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(dryRun ? "Dry-run complete." : "Update complete.");
        sb.AppendLine();

        var updated = result.BundleResults.Where(r => r.Success
            && r.Action is UpdateAction.Update or UpdateAction.New).ToList();
        if (updated.Count > 0)
        {
            sb.AppendLine("Updated:");
            foreach (var r in updated)
                sb.AppendLine($"  ✓ {r.BundleKey}: {r.FromVersion?.ToString() ?? "(none)"} → {r.ToVersion?.ToString() ?? "(none)"} ({r.TouchedFileCount} files)");
            sb.AppendLine();
        }

        var failed = result.BundleResults.Where(r => !r.Success).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine("Failed:");
            foreach (var r in failed)
            {
                sb.AppendLine($"  ✗ {r.BundleKey}");
                foreach (var e in r.Errors.Take(5)) sb.AppendLine($"      {e}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Preserved (untouched):");
        sb.AppendLine("  · profiles/ (entire directory)");
        sb.AppendLine("  · kernel/csv/{hostlist,workers,log_destinations}.csv");
        sb.AppendLine("  · kernel/json/ runtime artifacts (async_config.json 以外)");
        sb.AppendLine("  · kernel/txt/{passphrase_verify.txt,silence.flag}");
        sb.AppendLine("  · modules/**/*.csv not in whitelist (_list.csv family)");
        sb.AppendLine();

        if (result.SchemaWarnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var w in result.SchemaWarnings)
                sb.AppendLine($"  ⚠ {w}");
            sb.AppendLine();
        }

        sb.AppendLine($"Backup: {result.BackupZipPath}");
        sb.AppendLine($"Log   : {result.LogFilePath}");
        return sb.ToString();
    }

    private static string BuildDefaultBackupPath(string? target)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        if (string.IsNullOrWhiteSpace(target)) return $"fabriq_backup_{stamp}.zip";
        var parent = Path.GetDirectoryName(target.TrimEnd('\\', '/'));
        return Path.Combine(parent ?? "", $"fabriq_backup_{stamp}.zip");
    }

    private static string BuildDefaultLogPath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FabriqStudio", "UpdateLogs");
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        return Path.Combine(baseDir, $"update_{stamp}.log");
    }
}
