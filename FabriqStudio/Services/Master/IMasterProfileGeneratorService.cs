using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// 回答 → 計画（プレビュー）→ 書き込み。
/// BuildPlan は同期・純粋（スナップショットと辞書だけを見る）で、画面の入力に追従してライブ再計算できる。
/// </summary>
public interface IMasterProfileGeneratorService
{
    /// <summary>ワークスペースの必要情報（モジュール・CSV ヘッダー・既存 Segment 等）を読み込む。</summary>
    Task<MasterWorkspaceSnapshot> LoadSnapshotAsync();

    /// <summary>計画を計算する。例外は投げず、問題は Plan.Messages に入れる。</summary>
    MasterPlan BuildPlan(MasterTemplate template, MasterAnswers answers, MasterWorkspaceSnapshot snapshot);

    /// <summary>計画をディスクに書く。ファイルごとに続行し、結果に成否を列挙する。</summary>
    Task<MasterApplyResult> ApplyAsync(MasterPlan plan, IProgress<string>? progress = null);
}
