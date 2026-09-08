using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Master;

namespace FabriqStudio.ViewModels;

/// <summary>計画ダイアログの 1 行（プロファイル行の表示用）。</summary>
public sealed class PlanRowView
{
    public string Profile     { get; init; } = "";
    public int    Order       { get; init; }
    public string ScriptPath  { get; init; } = "";
    public string Description { get; init; } = "";
    public string Segment     { get; init; } = "";
    public string ErrorMode   { get; init; } = "";
    public string Group       { get; init; } = "";
    public bool   IsMarker    { get; init; }
}

/// <summary>
/// 生成計画の確認と書き込み。モーダル内で実行するため、生成中の画面遷移・二重生成は構造的に起きない。
/// </summary>
public partial class MasterPlanDialogViewModel : ObservableObject
{
    private readonly IMasterProfileGeneratorService _generator;

    public MasterPlan Plan { get; }

    public ObservableCollection<PlanRowView>     Rows        { get; } = [];
    public ObservableCollection<PlanFileSummary> Files       { get; } = [];
    public ObservableCollection<PlanMessage>     Messages    { get; } = [];
    public ObservableCollection<string>          ManualTasks { get; } = [];

    public string Title => $"生成計画 — {Plan.MasterName}";
    public bool   HasErrors   => Plan.HasErrors;
    public bool   HasManual   => ManualTasks.Count > 0;
    public bool   HasMessages => Messages.Count > 0;
    public int    ErrorCount   => Plan.Messages.Count(m => m.Severity == PlanSeverity.Error);
    public int    WarningCount => Plan.Messages.Count(m => m.Severity == PlanSeverity.Warning);

    public string Summary
    {
        get
        {
            var master  = Plan.Profiles.FirstOrDefault(p => p.Kind == ProfileKind.Master);
            var sysprep = Plan.Profiles.FirstOrDefault(p => p.IsSysprep);
            var parts = new List<string>
            {
                $"マスタ {master?.Rows.Count ?? 0} 行",
            };
            if (sysprep is not null) parts.Add($"Sysprep {sysprep.Rows.Count} 行");
            parts.Add($"レジストリ {Plan.RegistryOps.Sum(r => r.Rows.Count)} 件");
            parts.Add($"モジュール CSV {Plan.CsvOps.Count} ファイル / {Plan.CsvOps.Sum(c => c.Rows.Count)} 行");
            if (Plan.Deletes.Count > 0) parts.Add($"削除 {Plan.Deletes.Count}");
            return string.Join("  ·  ", parts);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isApplying;

    [ObservableProperty] private string? _progressText;

    /// <summary>生成結果。未実行なら null。</summary>
    [ObservableProperty] private MasterApplyResult? _result;

    /// <summary>ダイアログを閉じる要求（View が購読）。</summary>
    public event EventHandler? CloseRequested;

    public MasterPlanDialogViewModel(MasterPlan plan, IMasterProfileGeneratorService generator)
    {
        Plan       = plan;
        _generator = generator;

        foreach (var p in plan.Profiles)
            foreach (var r in p.Rows)
                Rows.Add(new PlanRowView
                {
                    Profile     = p.Name,
                    Order       = r.Order,
                    ScriptPath  = r.ScriptPath,
                    Description = r.Description,
                    Segment     = r.Segment,
                    ErrorMode   = r.ErrorMode,
                    Group       = r.Group,
                    IsMarker    = r.IsSystemCommand,
                });

        foreach (var f in plan.FileSummaries) Files.Add(f);
        foreach (var m in plan.Messages.OrderByDescending(m => m.Severity)) Messages.Add(m);
        foreach (var t in plan.ManualTasks) ManualTasks.Add(t);
    }

    private bool CanApply() => !HasErrors && !IsApplying && Result is null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsApplying   = true;
        ProgressText = "書き込み中...";
        try
        {
            var progress = new Progress<string>(p => ProgressText = $"書き込み中: {p}");
            Result = await _generator.ApplyAsync(Plan, progress);
            ProgressText = Result.Succeeded
                ? $"✓ {Result.Written.Count} ファイルを書き込みました"
                : $"⚠ {Result.Failed.Count} 件の書き込みに失敗しました";
        }
        catch (Exception ex)
        {
            Result = new MasterApplyResult { Error = ex.Message };
            ProgressText = $"エラー: {ex.Message}";
        }
        finally
        {
            IsApplying = false;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnResultChanged(MasterApplyResult? value) => ApplyCommand.NotifyCanExecuteChanged();
}
