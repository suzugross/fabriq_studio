using FabriqStudio.Services;

namespace FabriqStudio.Helpers;

/// <summary>バッチ暗号化・復号の共通ヘルパー。</summary>
public static class CryptoHelper
{
    /// <summary>暗号化対象外のカラム名セット。</summary>
    private static readonly HashSet<string> ExcludedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        // ID / Primary Key
        "AdminID", "TaskID", "StepID", "ID", "No",
        // フラグ
        "Enabled",
        // 順序 / セグメント
        "Order", "Step", "Segment",
        // 分類 / メタ
        "Category", "Type", "Kind", "MenuName",
        // 数値制御
        "MaxRetry", "IntervalSec", "TimeoutSec",
        // スクリプト参照 / アクション
        "Script", "ScriptPath", "Action",
        // Condition
        "Condition",
    };

    /// <summary>指定カラムが暗号化可能か判定する。</summary>
    public static bool IsEncryptableColumn(string columnName)
        => !ExcludedColumns.Contains(columnName);

    /// <summary>パスフレーズ未設定時のエラーメッセージを返す。設定済みなら null。</summary>
    public static string? ValidatePassphrase(ICryptoService crypto)
        => crypto.HasPassphrase
            ? null
            : "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";
}

/// <summary>バッチ暗号化・復号の実行結果。</summary>
public record BatchCryptoResult(int Processed, int Skipped, IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;

    /// <summary>ユーザー表示用のサマリー文字列。</summary>
    public string ToSummary(bool isEncrypt)
    {
        var action = isEncrypt ? "暗号化" : "復号";
        var msg = $"{action}: {Processed} 件処理, {Skipped} 件スキップ";
        if (HasErrors)
            msg += $"\n⚠ {Errors.Count} 件エラー:\n" + string.Join("\n", Errors.Take(10));
        return msg;
    }
}
