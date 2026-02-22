namespace FabriqStudio.Models;

/// <summary>
/// AutoKey レシピで使用できるアクション種別の定数と既定値。
/// </summary>
public static class ActionType
{
    public const string Open     = "Open";
    public const string WaitWin  = "WaitWin";
    public const string AppFocus = "AppFocus";
    public const string Type     = "Type";
    public const string Key      = "Key";
    public const string Wait     = "Wait";

    /// <summary>UI の ComboBox に表示する全アクション一覧（定義順）。</summary>
    public static readonly IReadOnlyList<string> All =
        [Open, WaitWin, AppFocus, Type, Key, Wait];

    /// <summary>アクション変更時に Wait 列へ自動適用するデフォルト値 (ms)。</summary>
    public static int DefaultWait(string action) => action switch
    {
        WaitWin              => 10000,
        AppFocus or Type     => 500,
        Key                  => 200,
        _                    => 0      // Open, Wait
    };

    /// <summary>アクション変更時に Value 列へ自動適用するデフォルト値。</summary>
    public static string DefaultValue(string action) =>
        action == Wait ? "1000" : "";
}
