namespace FabriqStudio.Models;

/// <summary>
/// procedure.csv の Step 群を PhaseID で集約した、Phase 一覧表示用のサマリ。
///
/// PhaseLabel / Color は Step に denormalize されて保存されているため、
/// 集約時には先頭 Step の値を採用する（pianist.ps1 も同様の暗黙ルール）。
/// 編集（PhaseID rename / 削除 / 色変更）は Phase 6 で実装する。Phase 3 では読み取り専用表示。
/// </summary>
public class PianistPhaseSummary
{
    public string PhaseID    { get; init; } = "";
    public string PhaseLabel { get; init; } = "";
    public string Color      { get; init; } = "";
    public int    StepCount  { get; init; }

    public override string ToString() => $"{PhaseID}: {PhaseLabel}";
}
