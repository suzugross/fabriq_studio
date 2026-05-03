namespace FabriqStudio.Models;

/// <summary>
/// pianist プロファイルの整合性チェック結果（§12）。
///
/// <see cref="Severity"/> は表示時のソート / 色分けに使う:
///   - Error : 必須項目違反（実行不能 / pianist.ps1 が読めない）
///   - Warning: 推奨違反（実行は通るが想定外の挙動になる可能性）
///   - Info   : 情報通知（無くても動く / プレースホルダ表示で済む）
/// </summary>
public class PianistValidationIssue
{
    public enum Severity { Error, Warning, Info }

    public Severity Level    { get; init; }
    public string   Category { get; init; } = "";
    public string   Message  { get; init; } = "";
    /// <summary>具体的な発生箇所（PhaseID, Step 番号, 列名など）</summary>
    public string?  Source   { get; init; }
}
