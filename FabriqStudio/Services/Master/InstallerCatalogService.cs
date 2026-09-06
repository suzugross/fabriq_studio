using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

public sealed class InstallerCatalogService : IInstallerCatalogService
{
    private static readonly string CatalogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "master_template", "installer_catalog.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    /// <summary>exe 内の文字列マーカーからインストーラ種別を推定する（先頭 4 MB のみ走査）。</summary>
    private static readonly (string Marker, string Source, string Args)[] Technologies =
    [
        ("Inno Setup",     "Inno Setup",    "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"),
        ("Nullsoft",       "NSIS",          "/S"),
        ("NullsoftInst",   "NSIS",          "/S"),
        ("InstallShield",  "InstallShield", "/s /v\"/qn /norestart\""),
        ("wixburn",        "WiX Burn",      "/install /quiet /norestart"),
        ("Advanced Installer", "Advanced Installer", "/exenoui /qn"),
    ];

    private const int ScanBytes = 4 * 1024 * 1024;

    private List<CatalogEntry> _entries = [];
    private bool _loaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(CatalogPath)) return;
            await using var stream = File.OpenRead(CatalogPath);
            var data = await JsonSerializer.DeserializeAsync<CatalogFile>(stream, JsonOptions);
            _entries = data?.Entries ?? [];
            foreach (var e in _entries)
            {
                try { e.Regex = new Regex(e.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                catch { e.Regex = null; }
            }
        }
        catch
        {
            _entries = [];   // 辞書が壊れていても種別判定だけで動く
        }
    }

    public InstallerSuggestion Suggest(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var ext      = Path.GetExtension(fileName).ToLowerInvariant();
        var version  = ReadVersion(filePath, out var productName);

        // 1) 辞書（ファイル名パターン）
        var hit = _entries.FirstOrDefault(e => e.Regex is not null && e.Regex.IsMatch(fileName));
        if (hit is not null)
        {
            return new InstallerSuggestion
            {
                AppName     = string.IsNullOrWhiteSpace(hit.AppName) ? FallbackName(productName, fileName) : hit.AppName,
                Type        = string.IsNullOrWhiteSpace(hit.Type) ? TypeFromExtension(ext) : hit.Type,
                SilentArgs  = hit.SilentArgs ?? "",
                Version     = version,
                Source      = $"カタログ: {hit.AppName}",
                NeedsReview = hit.NeedsReview,
            };
        }

        // 2) 拡張子で決まるもの
        if (ext == ".msi")
            return new InstallerSuggestion
            {
                AppName = FallbackName(productName, fileName), Type = "msi",
                SilentArgs = "/qn /norestart", Version = version, Source = "MSI（汎用）",
            };
        if (ext == ".bat" || ext == ".cmd")
            return new InstallerSuggestion
            {
                AppName = Path.GetFileNameWithoutExtension(fileName), Type = "bat",
                SilentArgs = "", Version = version, Source = "バッチ",
            };

        // 3) exe のインストーラ種別
        var tech = DetectTechnology(filePath);
        if (tech is { } t)
            return new InstallerSuggestion
            {
                AppName = FallbackName(productName, fileName), Type = "exe",
                SilentArgs = t.Args, Version = version, Source = $"{t.Source}（種別判定）", NeedsReview = true,
            };

        return new InstallerSuggestion
        {
            AppName = FallbackName(productName, fileName), Type = "exe",
            SilentArgs = "", Version = version, Source = "要確認", NeedsReview = true,
        };
    }

    private static string TypeFromExtension(string ext) => ext switch
    {
        ".msi" => "msi",
        ".bat" or ".cmd" => "bat",
        _ => "exe",
    };

    private static string FallbackName(string productName, string fileName)
    {
        var p = productName.Trim();
        if (p.Length > 0 && !p.Equals("Setup", StringComparison.OrdinalIgnoreCase)
                         && !p.Equals("Installer", StringComparison.OrdinalIgnoreCase))
            return p;
        return Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>exe / msi の VERSIONINFO から製品名とバージョンを読む（読めなければ空）。</summary>
    private static string ReadVersion(string filePath, out string productName)
    {
        productName = "";
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            productName = vi.ProductName ?? "";
            return (vi.ProductVersion ?? vi.FileVersion ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static (string Source, string Args)? DetectTechnology(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var len = (int)Math.Min(fs.Length, ScanBytes);
            var buf = new byte[len];
            var read = 0;
            while (read < len)
            {
                var n = fs.Read(buf, read, len - read);
                if (n <= 0) break;
                read += n;
            }
            var text = Encoding.Latin1.GetString(buf, 0, read);
            foreach (var (marker, source, args) in Technologies)
                if (text.Contains(marker, StringComparison.Ordinal))
                    return (source, args);
        }
        catch
        {
            // 読めないファイルは判定不能
        }
        return null;
    }

    private sealed class CatalogFile
    {
        [JsonPropertyName("entries")] public List<CatalogEntry> Entries { get; set; } = [];
    }

    private sealed class CatalogEntry
    {
        [JsonPropertyName("pattern")]     public string  Pattern     { get; set; } = "";
        [JsonPropertyName("appName")]     public string  AppName     { get; set; } = "";
        [JsonPropertyName("type")]        public string? Type        { get; set; }
        [JsonPropertyName("silentArgs")]  public string? SilentArgs  { get; set; }
        [JsonPropertyName("note")]        public string? Note        { get; set; }
        [JsonPropertyName("needsReview")] public bool    NeedsReview { get; set; }
        [JsonIgnore] public Regex? Regex { get; set; }
    }
}
