namespace FabriqStudio.Models;

/// <summary>Update 実行リクエスト。</summary>
/// <param name="Plan">事前計算済み Plan</param>
/// <param name="SelectedBundles">ユーザーがチェックした bundle のみ</param>
/// <param name="BackupZipPath">バックアップ zip 出力先の絶対パス</param>
/// <param name="DryRun">true なら実際のファイル書き込みを行わず、計画とログだけ出す</param>
/// <param name="LogFilePath">ログファイル出力先の絶対パス</param>
public record FabriqUpdateRequest(
    FabriqUpdatePlan                Plan,
    IReadOnlyList<BundleUpdateItem> SelectedBundles,
    string                          BackupZipPath,
    bool                            DryRun,
    string                          LogFilePath);

/// <summary>bundle 1 単位の実行結果。</summary>
/// <param name="BundleKey">`kernel` or `modules/{std,ext}/<name>`</param>
/// <param name="Success">全ファイルのコピーが成功した場合 true</param>
/// <param name="TouchedFileCount">コピーしたファイル数</param>
/// <param name="SkippedCount">preserve ルールで意図的にスキップしたファイル数</param>
/// <param name="Errors">コピー失敗ファイル / その他のエラー</param>
/// <param name="FromVersion">実行前の target 側バージョン</param>
/// <param name="ToVersion">実行後（予定）の template 側バージョン</param>
public record BundleUpdateResult(
    string              BundleKey,
    bool                Success,
    int                 TouchedFileCount,
    int                 SkippedCount,
    IReadOnlyList<string> Errors,
    SemVer?             FromVersion,
    SemVer?             ToVersion,
    UpdateAction        Action);

/// <summary>Update 実行全体の結果。</summary>
public record FabriqUpdateResult(
    IReadOnlyList<BundleUpdateResult> BundleResults,
    string                            BackupZipPath,
    string                            LogFilePath,
    bool                              DryRun,
    IReadOnlyList<string>             SchemaWarnings)
{
    public int SuccessCount => BundleResults.Count(r => r.Success
        && r.Action is UpdateAction.Update or UpdateAction.New);
    public int FailureCount => BundleResults.Count(r => !r.Success);
    public int SkippedCount => BundleResults.Count(r => r.Action is UpdateAction.SkipSame
        or UpdateAction.SkipTargetNewer or UpdateAction.SkipNoTemplate or UpdateAction.Preserve);
}

/// <summary>更新前の安全チェック結果（§ 9.7）。</summary>
/// <param name="Errors">ブロック要因。1 件でもあれば Apply 不可</param>
/// <param name="RequiresKernelBlocks">REQUIRES_KERNEL 不整合で block されたモジュールキー</param>
public record PreflightResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> RequiresKernelBlocks)
{
    public bool CanProceed => Errors.Count == 0 && RequiresKernelBlocks.Count == 0;
}
