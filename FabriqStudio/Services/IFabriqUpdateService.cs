using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// fabriq 本体を template からオーバーレイ更新するサービス。
/// fabriq 公開契約（KERNEL_API.md § 9）および <c>dev/framework_overlay_rules.json</c> に準拠。
/// <list type="bullet">
///   <item><see cref="LoadRulesAsync"/> — template の overlay ルール読み込み（schemaVersion 検証付き）</item>
///   <item><see cref="ComputePlanAsync"/> — bundle 単位の SemVer 比較 + plan 作成</item>
///   <item><see cref="RunPreflight"/> — 安全チェック（§ 9.7）</item>
///   <item><see cref="ApplyAsync"/> — backup → overlay 実行</item>
/// </list>
/// </summary>
public interface IFabriqUpdateService
{
    /// <summary>
    /// template 側の overlay ルールを読み込む。
    /// schemaVersion が未対応の場合 / ファイルが存在しない場合は例外で失敗させる（§ 9.8 準拠）。
    /// </summary>
    Task<OverlayRules> LoadRulesAsync(string templateRoot);

    /// <summary>
    /// Plan 計算: kernel と各モジュール bundle の VERSION を両側で比較してアクションを決定する。
    /// </summary>
    Task<FabriqUpdatePlan> ComputePlanAsync(string templateRoot, string targetRoot);

    /// <summary>
    /// 更新前の安全チェック（§ 9.7）。Fabriq.exe 実行状態 / resume_state / 書込権限 / REQUIRES_KERNEL。
    /// </summary>
    PreflightResult RunPreflight(FabriqUpdatePlan plan, IReadOnlyList<BundleUpdateItem> selected);

    /// <summary>
    /// バックアップ zip を作成してから overlay を実行する。DryRun=true の場合は zip のみ作成しコピーは行わない。
    /// 進捗は <paramref name="progress"/> に string メッセージで通知する。
    /// </summary>
    Task<FabriqUpdateResult> ApplyAsync(FabriqUpdateRequest request, IProgress<string>? progress = null);
}
