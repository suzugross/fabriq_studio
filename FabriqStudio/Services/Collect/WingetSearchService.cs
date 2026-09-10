using System.Text;
using System.Text.RegularExpressions;

namespace FabriqStudio.Services.Collect;

/// <summary>
/// <c>winget search</c> の表形式出力を解析する。JSON 出力が無いので、ヘッダー行の列位置（表示幅）で切る。
/// 日本語名は表示幅 2 で桁が動くため、文字数ではなく表示幅で切る。
/// </summary>
public sealed class WingetSearchService : IWingetSearchService
{
    private readonly IPowerShellRunner _ps;

    public WingetSearchService(IPowerShellRunner ps)
    {
        _ps = ps;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var r = await _ps.RunAsync("if (Get-Command winget -ErrorAction SilentlyContinue) { 'winget:yes' } else { 'winget:no' }", ct);
        return r.StdOut.Contains("winget:yes", StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<WingetPackage>> SearchAsync(string query, CancellationToken ct = default)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];

        // 画面幅が狭いと winget が名前を … で詰めるので、コンソールのバッファ幅を広げてから実行する
        var script =
            "try { $host.UI.RawUI.BufferSize = New-Object System.Management.Automation.Host.Size(400, 3000) } catch { }\r\n" +
            $"winget search --query '{q.Replace("'", "''")}' --source winget --accept-source-agreements --disable-interactivity\r\n" +
            "exit 0\r\n";   // 該当なしは winget が非 0 を返すが、ここでは空一覧として扱う
        var r = await _ps.RunAsync(script, ct);
        return r.Cancelled ? [] : Parse(r.StdOut);
    }

    // ── 解析 ─────────────────────────────────────────────────────

    private static readonly Regex HeaderId      = new(@"\bId\b",      RegexOptions.Compiled);
    private static readonly Regex HeaderVersion = new(@"\bVersion\b", RegexOptions.Compiled);
    private static readonly Regex HeaderMatch   = new(@"\bMatch\b",   RegexOptions.Compiled);
    private static readonly Regex HeaderSource  = new(@"\bSource\b",  RegexOptions.Compiled);

    /// <summary>winget search の出力を解析する（テスト可能な純粋関数）。</summary>
    public static IReadOnlyList<WingetPackage> Parse(string output)
    {
        var lines  = (output ?? "").Replace("\r", "").Split('\n');
        var header = Array.FindIndex(lines, l =>
            l.StartsWith("Name", StringComparison.Ordinal) && HeaderId.IsMatch(l) && HeaderVersion.IsMatch(l));
        if (header < 0) return [];

        var h        = lines[header];                       // ヘッダーは ASCII なので文字位置 = 表示幅
        var idStart  = HeaderId.Match(h).Index;
        var verStart = HeaderVersion.Match(h).Index;
        var verEnd   = FirstIndex(h, HeaderMatch, HeaderSource) ?? int.MaxValue;

        var result = new List<WingetPackage>();
        for (var i = header + 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal)) continue;

            var name    = Slice(line, 0, idStart).Trim();
            var id      = Slice(line, idStart, verStart).Trim();
            var version = Slice(line, verStart, verEnd).Trim();
            if (id.Length == 0 || id.Contains(' ') || name.Length == 0) continue;   // 進捗行やフッターの取りこぼし

            result.Add(new WingetPackage(name, id, version));
        }
        return result;
    }

    private static int? FirstIndex(string header, params Regex[] patterns)
    {
        int? best = null;
        foreach (var p in patterns)
        {
            var m = p.Match(header);
            if (m.Success && (best is null || m.Index < best)) best = m.Index;
        }
        return best;
    }

    /// <summary>表示幅 [start, end) の範囲の文字列を返す。</summary>
    internal static string Slice(string line, int start, int end)
    {
        var sb  = new StringBuilder();
        var col = 0;
        foreach (var ch in line)
        {
            if (col >= end) break;
            if (col >= start) sb.Append(ch);
            col += Width(ch);
        }
        return sb.ToString();
    }

    /// <summary>コンソールの表示幅（東アジアの全角 = 2、結合文字・下位サロゲート = 0、他 = 1）。winget の桁揃えと同じ考え方。</summary>
    internal static int Width(char c)
    {
        if (char.IsLowSurrogate(c)) return 0;
        if (char.IsHighSurrogate(c)) return 2;
        if (c is >= '̀' and <= 'ͯ') return 0;   // 結合ダイアクリティカル
        if (c < 'ᄀ') return 1;
        return c switch
        {
            >= 'ᄀ' and <= 'ᅟ' => 2,   // ハングル字母
            >= '⺀' and <= '〾' => 2,   // CJK 部首・記号
            >= 'ぁ' and <= '㏿' => 2,   // かな・カナ・CJK 互換
            >= '㐀' and <= '䶿' => 2,   // CJK 拡張 A
            >= '一' and <= '鿿' => 2,   // CJK 統合漢字
            >= 'ꀀ' and <= '꓏' => 2,   // 彝文字
            >= '가' and <= '힣' => 2,   // ハングル音節
            >= '豈' and <= '﫿' => 2,   // CJK 互換漢字
            >= '︰' and <= '﹏' => 2,   // CJK 互換形
            >= '＀' and <= '｠' => 2,   // 全角英数
            >= '￠' and <= '￦' => 2,   // 全角記号
            _ => 1,
        };
    }
}
