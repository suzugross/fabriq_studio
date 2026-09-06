using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FabriqStudio.Models.Gpo;

namespace FabriqStudio.Services.Gpo;

/// <summary>
/// GPO 辞書サービスの実装。
/// <para>
/// ADMX の場所: &lt;exe&gt;\gpo_collection\settings.json の admxPath → &lt;exe&gt;\gpo_collection\PolicyDefinitions（同梱）
/// → %SystemRoot%\PolicyDefinitions の順に採用する。
/// お気に入り層: &lt;exe&gt;\master_template\gpo_favorites.json。
/// </para>
/// </summary>
public sealed class GpoCatalogService : IGpoCatalogService
{
    public static readonly string[] Languages = ["ja-JP", "en-US"];

    private static readonly string DataDir       = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpo_collection");
    private static readonly string SettingsPath  = Path.Combine(DataDir, "settings.json");
    private static readonly string BundledPath   = Path.Combine(DataDir, "PolicyDefinitions");
    private static readonly string FavoritesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "master_template", "gpo_favorites.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        WriteIndented               = true,
    };

    private readonly object _gate = new();
    private Task? _loadTask;

    public GpoCatalog? Catalog   { get; private set; }
    public bool        IsLoaded  => Catalog is not null;
    public bool        IsLoading { get; private set; }
    public string?     LoadError { get; private set; }

    public string SourcePath        { get; private set; }
    public string DefaultSourcePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "PolicyDefinitions");

    public IReadOnlyList<GpoFavorite> Favorites { get; private set; } = [];

    public event EventHandler? CatalogChanged;

    public GpoCatalogService()
    {
        SourcePath = ResolveSourcePath();
    }

    // ── 読み込み ───────────────────────────────────────────────────

    public Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (Catalog is not null) return Task.CompletedTask;
            return _loadTask ??= LoadCoreAsync(SourcePath);
        }
    }

    public Task ReloadAsync(string? sourcePath = null)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            SourcePath = sourcePath.Trim();
            SaveSettings();
        }
        Task task;
        lock (_gate)
        {
            task = _loadTask ??= LoadCoreAsync(SourcePath);
        }
        return task;
    }

    private async Task LoadCoreAsync(string path)
    {
        IsLoading = true;
        LoadError = null;
        CatalogChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var favorites = LoadFavorites();
            var catalog   = await Task.Run(() => AdmxCatalogLoader.Load(path, Languages)).ConfigureAwait(false);
            ApplyFavorites(catalog, favorites);
            Favorites = favorites;
            Catalog   = catalog;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            Catalog   = null;
        }
        finally
        {
            IsLoading = false;
            lock (_gate) _loadTask = null;
            CatalogChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── 参照 ──────────────────────────────────────────────────────

    public GpoPolicy? FindPolicy(string? id) => Catalog?.FindPolicy(id);

    public GpoSearchResult Search(GpoSearchQuery query)
    {
        var catalog = Catalog;
        if (catalog is null) return new GpoSearchResult();

        var tokens = (query.Text ?? "")
            .Split([' ', '　', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        IEnumerable<GpoPolicy> src = catalog.Policies;
        if (query.Scope == GpoPolicyClass.Machine)   src = src.Where(p => p.Class != GpoPolicyClass.User);
        else if (query.Scope == GpoPolicyClass.User) src = src.Where(p => p.Class != GpoPolicyClass.Machine);
        if (!string.IsNullOrEmpty(query.TopCategory)) src = src.Where(p => p.TopCategory == query.TopCategory);
        if (query.FavoritesOnly)                      src = src.Where(p => p.IsFavorite);

        var scored = new List<(GpoPolicy Policy, int Score)>();
        foreach (var p in src)
        {
            if (tokens.Length == 0)
            {
                scored.Add((p, p.IsFavorite ? 0 : 1));
                continue;
            }
            var ok = true;
            var score = 0;
            foreach (var t in tokens)
            {
                if (p.DisplayNameLower.Contains(t, StringComparison.Ordinal)) continue;
                if (p.NameLower.Contains(t, StringComparison.Ordinal) || p.DisplayNameEnLower.Contains(t, StringComparison.Ordinal)) { score += 1; continue; }
                if (p.SearchText.Contains(t, StringComparison.Ordinal)) { score += 3; continue; }
                ok = false;
                break;
            }
            if (ok) scored.Add((p, score));
        }

        var items = scored
            .OrderBy(x => x.Score)
            .ThenByDescending(x => x.Policy.IsFavorite)
            .ThenBy(x => x.Policy.CategoryPath, StringComparer.CurrentCulture)
            .ThenBy(x => x.Policy.DisplayName, StringComparer.CurrentCulture)
            .Select(x => x.Policy)
            .Take(Math.Max(1, query.Limit))
            .ToList();

        return new GpoSearchResult { Items = items, TotalMatches = scored.Count };
    }

    // ── お気に入り層 ───────────────────────────────────────────────

    private static List<GpoFavorite> LoadFavorites()
    {
        try
        {
            if (!File.Exists(FavoritesPath)) return [];
            var json = File.ReadAllText(FavoritesPath);
            var file = JsonSerializer.Deserialize<GpoFavoritesFile>(json, JsonOptions);
            return file?.Favorites?.Where(f => !string.IsNullOrWhiteSpace(f.Id)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void ApplyFavorites(GpoCatalog catalog, List<GpoFavorite> favorites)
    {
        foreach (var f in favorites)
        {
            var p = catalog.FindPolicy(f.Id);
            if (p is null)
            {
                catalog.Errors.Add($"お気に入り {f.Id} は ADMX に見つかりません。");
                continue;
            }
            p.IsFavorite    = true;
            p.FavoriteGroup = f.Group;
            p.FavoriteNote  = f.Note;
            p.Favorite      = f;
        }
    }

    // ── ADMX の場所 ───────────────────────────────────────────────

    private sealed class Settings
    {
        [JsonPropertyName("admxPath")] public string? AdmxPath { get; set; }
    }

    private string ResolveSourcePath()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (!string.IsNullOrWhiteSpace(s?.AdmxPath)) return s.AdmxPath.Trim();
            }
        }
        catch
        {
            // 設定ファイル破損は既定へフォールバック
        }
        if (Directory.Exists(BundledPath)) return BundledPath;
        return DefaultSourcePath;
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var s = new Settings { AdmxPath = SourcePath.Equals(DefaultSourcePath, StringComparison.OrdinalIgnoreCase) ? null : SourcePath };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOptions));
        }
        catch
        {
            // 記憶できなくても今回の読み込みは続ける
        }
    }
}
