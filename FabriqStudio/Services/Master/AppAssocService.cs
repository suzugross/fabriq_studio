using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FabriqStudio.Models.Master;
using Microsoft.Win32;

namespace FabriqStudio.Services.Master;

public sealed class AppAssocService : IAppAssocService
{
    private static readonly string TemplateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "master_template");
    private static readonly string DictPath    = Path.Combine(TemplateDir, "appassoc_apps.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    private AppAssocDictionary _dict = new();
    private bool _loaded;

    private readonly object _localGate = new();
    private Dictionary<string, List<AppAssocCandidate>>? _local;

    public string BaseXmlPath { get; } = Path.Combine(TemplateDir, "appassoc_base.xml");

    public IReadOnlyList<AppAssocApp>      Apps       => _dict.Apps;
    public IReadOnlyList<AppAssocCategory> Categories => _dict.Categories;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(DictPath))
            {
                var json = await File.ReadAllTextAsync(DictPath);
                _dict = JsonSerializer.Deserialize<AppAssocDictionary>(json, JsonOptions) ?? new AppAssocDictionary();
                foreach (var app in _dict.Apps)
                    app.ProgIds = new Dictionary<string, string>(app.ProgIds, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _dict = new AppAssocDictionary();
        }
    }

    // ── この PC の登録アプリ ─────────────────────────────────────────

    public IReadOnlyList<AppAssocCandidate> LocalCandidates(string identifier)
    {
        var map = EnsureLocal();
        return map.TryGetValue(identifier, out var list) ? list : [];
    }

    private Dictionary<string, List<AppAssocCandidate>> EnsureLocal()
    {
        lock (_localGate)
        {
            if (_local is not null) return _local;
            var map = new Dictionary<string, List<AppAssocCandidate>>(StringComparer.OrdinalIgnoreCase);
            try { CollectRegisteredApplications(map); } catch { /* 読めない環境では候補なし */ }
            try { CollectOpenWithProgids(map); }        catch { /* 同上 */ }
            _local = map;
            return map;
        }
    }

    /// <summary>HKLM/HKCU\SOFTWARE\RegisteredApplications → Capabilities の FileAssociations / URLAssociations。</summary>
    private static void CollectRegisteredApplications(Dictionary<string, List<AppAssocCandidate>> map)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var reg = hive.OpenSubKey(@"SOFTWARE\RegisteredApplications");
            if (reg is null) continue;

            foreach (var appKey in reg.GetValueNames())
            {
                var capPath = reg.GetValue(appKey) as string;
                if (string.IsNullOrWhiteSpace(capPath)) continue;
                using var cap = hive.OpenSubKey(capPath);
                if (cap is null) continue;

                var appName = ResolveIndirect(cap.GetValue("ApplicationName") as string) ?? appKey;

                foreach (var (sub, _) in new[] { ("FileAssociations", 0), ("URLAssociations", 1) })
                {
                    using var assoc = cap.OpenSubKey(sub);
                    if (assoc is null) continue;
                    foreach (var id in assoc.GetValueNames())
                    {
                        var progId = assoc.GetValue(id) as string;
                        if (string.IsNullOrWhiteSpace(progId)) continue;
                        Add(map, id, appName, progId);
                    }
                }
            }
        }
    }

    /// <summary>HKCR\&lt;ext&gt;\OpenWithProgids（ストア アプリはここにしか出ない）。</summary>
    private static void CollectOpenWithProgids(Dictionary<string, List<AppAssocCandidate>> map)
    {
        using var root = Registry.ClassesRoot;
        foreach (var ext in root.GetSubKeyNames().Where(n => n.StartsWith('.')))
        {
            using var key = root.OpenSubKey(ext + @"\OpenWithProgids");
            if (key is null) continue;
            foreach (var progId in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(progId)) continue;
                var name = ProgIdDisplayName(progId);
                if (name is null) continue;
                Add(map, ext, name, progId);
            }
        }
    }

    /// <summary>
    /// OpenWithProgids の ProgId をアプリ名にする。アプリ名が登録されているもの（Application\ApplicationName）と
    /// ストア アプリ（AppX… はパッケージのリソース名 = 「フォト」等）だけを候補にし、
    /// htmlfile / jpegfile のようなファイル種別だけの ProgId は候補にしない（アプリではないため）。
    /// </summary>
    private static string? ProgIdDisplayName(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(progId);
        if (key is null) return null;

        using var app = key.OpenSubKey("Application");
        var name = ResolveIndirect(app?.GetValue("ApplicationName") as string);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        if (!progId.StartsWith("AppX", StringComparison.OrdinalIgnoreCase)) return null;

        name = ResolveIndirect(key.GetValue("FriendlyTypeName") as string);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        name = key.GetValue("") as string;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static void Add(Dictionary<string, List<AppAssocCandidate>> map, string id, string appName, string progId)
    {
        if (!map.TryGetValue(id, out var list)) map[id] = list = [];
        if (list.Any(c => c.ProgId.Equals(progId, StringComparison.OrdinalIgnoreCase))) return;
        list.Add(new AppAssocCandidate { AppName = appName, ProgId = progId, Source = "この PC" });
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    /// <summary>"@dll,-123" / "@{Package?ms-resource://...}" 形式のリソース文字列を実文字列にする。解決できなければ null。</summary>
    private static string? ResolveIndirect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith('@')) return value.Trim();
        try
        {
            var sb = new StringBuilder(512);
            var expanded = Environment.ExpandEnvironmentVariables(value);
            if (SHLoadIndirectString(expanded, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                return sb.ToString().Trim();
        }
        catch
        {
            // 解決不可
        }
        return null;
    }

    // ── この PC からエクスポート（要管理者権限）─────────────────────

    public async Task<string?> ExportFromThisPcAsync()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"fabriq_appassoc_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
        var psi = new ProcessStartInfo
        {
            FileName        = Path.Combine(Environment.SystemDirectory, "dism.exe"),
            Arguments       = $"/Online /Export-DefaultAppAssociations:\"{temp}\"",
            UseShellExecute = true,
            Verb            = "runas",
            WindowStyle     = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return null;
            await p.WaitForExitAsync();
            return File.Exists(temp) ? temp : null;
        }
        catch (Win32Exception)
        {
            return null;   // UAC でキャンセル
        }
    }
}
