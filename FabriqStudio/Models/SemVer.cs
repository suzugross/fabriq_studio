using System.Text.RegularExpressions;

namespace FabriqStudio.Models;

/// <summary>
/// fabriq の `VERSION` / `KERNEL_VERSION` / `REQUIRES_KERNEL` 用 3 コンポーネント SemVer。
/// <para>
/// 書式: <c>^(\d+)\.(\d+)\.(\d+)$</c>。pre-release / build metadata は現行 fabriq で未使用のため非対応。
/// <see cref="System.Version"/> は 4 コンポーネントなので意図的に独自パーサを用意する。
/// </para>
/// </summary>
public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    private static readonly Regex Pattern = new(@"^\s*(\d+)\.(\d+)\.(\d+)\s*$", RegexOptions.Compiled);

    public static bool TryParse(string? s, out SemVer result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var m = Pattern.Match(s);
        if (!m.Success) return false;
        result = new SemVer(
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value));
        return true;
    }

    /// <summary>VERSION ファイルを読んで SemVer に変換。ファイル欠損・不正書式時は null。</summary>
    public static SemVer? TryParseFile(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        var text = System.IO.File.ReadAllText(path);
        return TryParse(text, out var v) ? v : null;
    }

    public int CompareTo(SemVer other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        return Patch.CompareTo(other.Patch);
    }

    public static bool operator > (SemVer a, SemVer b) => a.CompareTo(b) >  0;
    public static bool operator < (SemVer a, SemVer b) => a.CompareTo(b) <  0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
