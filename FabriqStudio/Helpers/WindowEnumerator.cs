using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FabriqStudio.Helpers;

/// <summary>
/// Visible top-level window enumeration for the Pianist Window Picker dialog.
///
/// The P/Invoke surface mirrors <c>PianistWin32</c> in
/// <c>modules/extended/pianist/pianist.ps1</c> so that any window the picker
/// surfaces is also discoverable by Pianist's <c>WaitWin</c> / <c>AppFocus</c>
/// at run time (substring match on title bar).
/// </summary>
internal static class WindowEnumNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}

/// <summary>One enumerated top-level window.</summary>
public sealed record WindowInfo(string Title, string ProcessName, uint ProcessId, IntPtr Handle);

/// <summary>
/// Enumerates visible top-level windows and filters out shell / IME background
/// noise. Used by <c>PianistWindowPickerDialog</c>; stateless and safe to call
/// from any thread that owns no UI.
/// </summary>
public static class WindowEnumerator
{
    /// <summary>
    /// Background / shell windows that are technically visible top-level windows
    /// but are never operation targets for Pianist's WaitWin / AppFocus.
    /// </summary>
    private static readonly HashSet<string> NoiseTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Manager",
        "Windows 入力エクスペリエンス",
        "MSCTFIME UI",
        "Default IME",
        "Microsoft Text Input Application",
    };

    public static List<WindowInfo> EnumerateVisibleWindows()
    {
        var result = new List<WindowInfo>();

        WindowEnumNative.EnumWindows((hWnd, _) =>
        {
            if (!WindowEnumNative.IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(512);
            var len = WindowEnumNative.GetWindowTextW(hWnd, sb, sb.Capacity);
            if (len == 0) return true;

            var title = sb.ToString();
            WindowEnumNative.GetWindowThreadProcessId(hWnd, out var pid);

            string procName = "(unknown)";
            try
            {
                using var p = Process.GetProcessById((int)pid);
                procName = p.ProcessName + ".exe";
            }
            catch
            {
                // process exited between enumeration and GetProcessById — keep "(unknown)"
            }

            result.Add(new WindowInfo(title, procName, pid, hWnd));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>
    /// Drops shell / IME background noise and any window owned by
    /// <paramref name="selfPid"/> (Studio itself + the picker dialog itself).
    /// </summary>
    public static IEnumerable<WindowInfo> FilterNoise(IEnumerable<WindowInfo> windows, uint selfPid)
        => windows.Where(w => !NoiseTitles.Contains(w.Title) && w.ProcessId != selfPid);
}

/// <summary>
/// Splits a window title into substring candidates suitable for Pianist's
/// substring-match WaitWin / AppFocus Value column. The first candidate is
/// always the full title; subsequent candidates are produced by splitting on
/// hyphens, full-width / half-width colons, and runs of two-or-more spaces.
///
/// All candidates are trimmed; empties and duplicates are removed while
/// preserving first-occurrence order.
/// </summary>
public static class WindowTitleSplitter
{
    private static readonly char[] HyphenSeps  = new[] { '-' };
    private static readonly char[] ColonSeps   = new[] { ':', '：' };
    private static readonly string[] WideSpace = new[] { "  " };

    public static IReadOnlyList<string> GetCandidates(string title)
    {
        var ordered = new List<string>();
        var seen    = new HashSet<string>(StringComparer.Ordinal);

        void Add(string candidate)
        {
            var trimmed = candidate.Trim();
            if (trimmed.Length == 0) return;
            if (!seen.Add(trimmed))   return;
            ordered.Add(trimmed);
        }

        if (string.IsNullOrWhiteSpace(title)) return ordered;

        Add(title);

        foreach (var part in title.Split(HyphenSeps,        StringSplitOptions.None)) Add(part);
        foreach (var part in title.Split(ColonSeps,         StringSplitOptions.None)) Add(part);
        foreach (var part in title.Split(WideSpace, StringSplitOptions.RemoveEmptyEntries)) Add(part);

        return ordered;
    }
}
