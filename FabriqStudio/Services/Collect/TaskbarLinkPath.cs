namespace FabriqStudio.Services.Collect;

/// <summary>
/// タスクバーのピン留めに書く LinkPath の整形。ユーザープロファイル配下のパスは環境変数に置き換える
/// （LayoutModification.xml は新規ユーザーに適用されるので、参照機のユーザー名を含むパスは使えない。
/// fabriq の taskbar_config は値をそのまま XML に書き、Windows がユーザーごとに展開する）。
/// </summary>
public static class TaskbarLinkPath
{
    /// <summary>環境変数に置き換えられるときは置き換える（できないときはそのまま）。</summary>
    public static string ToPortable(string path, IReadOnlyList<(string Var, string Value)>? env = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        env ??= DefaultEnv();

        foreach (var (name, value) in env)
        {
            var root = (value ?? "").TrimEnd('\\', '/');
            if (root.Length == 0) continue;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (path.Length != root.Length && path[root.Length] != '\\' && path[root.Length] != '/') continue;   // 名前の途中で切らない
            return $"%{name}%" + path[root.Length..];
        }
        return path;
    }

    /// <summary>候補は「より深いフォルダ」を先に（LOCALAPPDATA / APPDATA は USERPROFILE の下）。</summary>
    private static IReadOnlyList<(string, string)> DefaultEnv() =>
    [
        ("LOCALAPPDATA", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        ("APPDATA",      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        ("USERPROFILE",  Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    ];
}
