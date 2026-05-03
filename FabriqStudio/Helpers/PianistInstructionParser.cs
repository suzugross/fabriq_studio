using System.Text;
using System.Text.RegularExpressions;
using FabriqStudio.Models;

namespace FabriqStudio.Helpers;

/// <summary>
/// instructions/&lt;PhaseID&gt;.txt の section marker DSL のパーサ + シリアライザ。
///
/// <para>セマンティクスは pianist.ps1 の <c>Parse-PianistInstructionFile</c>
/// （<c>e:/fabriq/modules/extended/pianist/pianist.ps1</c> L937 以降）と
/// バイト単位で一致させてある。Studio で書いて pianist で読む round-trip と、
/// pianist で書いて Studio で読む round-trip の両方が成立するようテスト不在でも
/// 規則を厳守すること。</para>
///
/// <para>パース規則:</para>
/// <list type="bullet">
///   <item>section header 正規表現は <c>^\[([A-Za-z]+)\]$</c>（行 trim 後の完全一致）</item>
///   <item>section の前にある行は <c>_Pre</c> バケツに入り、最後に Manual 先頭へ折り込む</item>
///   <item>section が一つも無いファイルは全文を Manual に流し込む（legacy）</item>
///   <item>Variables は <c>[,\s]+</c> で分割、<c>^[A-Za-z_][A-Za-z0-9_]*$</c> のみ採用、<c>#</c> コメント可</item>
///   <item>Samples は <c>^(\S+)(\s+(.+))?$</c>、<c>#</c> コメント可、空行スキップ</item>
///   <item>改行は CRLF / LF / CR を吸収、内部表現は LF</item>
/// </list>
///
/// <para>シリアライズ規則: 出力は <c>[RPA]</c> → <c>[Manual]</c> → <c>[Variables]</c>
/// → <c>[Samples]</c> の順。中身が空の section は省略。改行は CRLF、末尾改行 1 つ。</para>
/// </summary>
public static class PianistInstructionParser
{
    private static readonly Regex SectionHeaderRegex =
        new(@"^\[([A-Za-z]+)\]$", RegexOptions.Compiled);

    private static readonly Regex VariableNameRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly Regex SampleLineRegex =
        new(@"^(\S+)(\s+(.+))?$", RegexOptions.Compiled);

    private static readonly char[] VariableSplit = { ',', ' ', '\t' };

    /// <summary>
    /// テキストを <see cref="PianistInstructionFile"/> へパースする。
    /// 入力 null は空ファイル扱い。
    /// </summary>
    public static PianistInstructionFile Parse(string? text)
    {
        var result = new PianistInstructionFile();
        var raw = text ?? "";

        // Normalize newlines to LF
        raw = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = raw.Split('\n');

        // section name → list of raw lines (preserve indentation, trailing spaces)
        var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? currentSection = null;
        bool hasAnySection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var headerMatch = SectionHeaderRegex.Match(trimmed);
            if (headerMatch.Success)
            {
                currentSection = headerMatch.Groups[1].Value;
                hasAnySection = true;
                if (!buckets.ContainsKey(currentSection))
                    buckets[currentSection] = new List<string>();
                continue;
            }

            var bucketKey = currentSection ?? "_Pre";
            if (!buckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new List<string>();
                buckets[bucketKey] = bucket;
            }
            bucket.Add(line);
        }

        // Legacy fallback: no section markers anywhere → entire file is Manual
        if (!hasAnySection)
        {
            result.WasLegacyFormat = true;
            result.Manual = string.Join("\r\n", lines).TrimEnd('\r', '\n', ' ', '\t');
            return result;
        }

        if (buckets.TryGetValue("RPA", out var rpaLines))
            result.RPA = string.Join("\r\n", rpaLines).Trim('\r', '\n', ' ', '\t');

        if (buckets.TryGetValue("Manual", out var manualLines))
            result.Manual = string.Join("\r\n", manualLines).Trim('\r', '\n', ' ', '\t');

        // Pre-section text gets folded into Manual (lenient parsing — author may put
        // intro lines before the first marker)
        if (buckets.TryGetValue("_Pre", out var preLines))
        {
            var preText = string.Join("\r\n", preLines).Trim('\r', '\n', ' ', '\t');
            if (!string.IsNullOrEmpty(preText))
            {
                result.Manual = string.IsNullOrEmpty(result.Manual)
                    ? preText
                    : preText + "\r\n\r\n" + result.Manual;
            }
        }

        if (buckets.TryGetValue("Variables", out var varLines))
        {
            foreach (var l in varLines)
            {
                var t = l.Trim();
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.StartsWith("#", StringComparison.Ordinal)) continue;

                var tokens = t.Split(VariableSplit, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tok in tokens)
                {
                    if (VariableNameRegex.IsMatch(tok))
                        result.Variables.Add(tok);
                }
            }
        }

        if (buckets.TryGetValue("Samples", out var sampleLines))
        {
            foreach (var l in sampleLines)
            {
                var t = l.Trim();
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.StartsWith("#", StringComparison.Ordinal)) continue;

                var m = SampleLineRegex.Match(t);
                if (!m.Success) continue;

                var entry = new PianistSampleEntry
                {
                    File    = m.Groups[1].Value,
                    Caption = m.Groups[3].Success ? m.Groups[3].Value : "",
                    // 物理ファイル存在は呼び出し側で別途判定して set する
                    Exists  = false,
                };
                result.Samples.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    /// <see cref="PianistInstructionFile"/> をテキスト化する。
    /// 出力順: <c>[RPA]</c> → <c>[Manual]</c> → <c>[Variables]</c> → <c>[Samples]</c>。
    /// 中身が空の section は省略。改行は CRLF、末尾改行 1 つ（書き出し側で付加）。
    /// </summary>
    public static string Serialize(PianistInstructionFile data)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(data.RPA))
        {
            sb.Append("[RPA]\r\n");
            sb.Append(NormalizeBody(data.RPA));
            sb.Append("\r\n\r\n");
        }

        if (!string.IsNullOrEmpty(data.Manual))
        {
            sb.Append("[Manual]\r\n");
            sb.Append(NormalizeBody(data.Manual));
            sb.Append("\r\n\r\n");
        }

        if (data.Variables.Count > 0)
        {
            sb.Append("[Variables]\r\n");
            // 1 行 1 変数で書く（カンマ区切りも合法だが diff の見やすさ優先）
            foreach (var v in data.Variables)
                sb.Append(v).Append("\r\n");
            sb.Append("\r\n");
        }

        if (data.Samples.Count > 0)
        {
            sb.Append("[Samples]\r\n");
            foreach (var s in data.Samples)
            {
                sb.Append(s.File);
                if (!string.IsNullOrEmpty(s.Caption))
                    sb.Append("  ").Append(s.Caption);
                sb.Append("\r\n");
            }
            sb.Append("\r\n");
        }

        // 末尾の余分な空行を 1 つの改行に整える
        var output = sb.ToString().TrimEnd('\r', '\n');
        if (output.Length == 0) return "";
        return output + "\r\n";
    }

    /// <summary>本文の改行を CRLF へ正規化（途中の段落構造は維持）。</summary>
    private static string NormalizeBody(string body)
        => body.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
}
