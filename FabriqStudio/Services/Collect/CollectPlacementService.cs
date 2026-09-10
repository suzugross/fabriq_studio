using System.Data;
using System.IO;
using FabriqStudio.Models;

namespace FabriqStudio.Services.Collect;

public sealed class CollectPlacementService : ICollectPlacementService
{
    private readonly IModuleDataResolver _resolver;
    private readonly IProfileDataService _profileData;
    private readonly IFileService        _file;
    private readonly IProfileService     _profiles;
    private readonly IModuleService      _modules;

    public CollectPlacementService(
        IModuleDataResolver resolver,
        IProfileDataService profileData,
        IFileService        file,
        IProfileService     profiles,
        IModuleService      modules)
    {
        _resolver    = resolver;
        _profileData = profileData;
        _file        = file;
        _profiles    = profiles;
        _modules     = modules;
    }

    public async Task<bool> EnsureCsvAsync(string profile, string moduleDir, string csvName)
    {
        var rel = ModuleRel(moduleDir, csvName);
        if (_resolver.ResolveRead(rel, profile).Source == ModuleDataSource.Profile) return false;
        var copied = await _profileData.MaterializeCsvAsync(profile, moduleDir, csvName);
        if (copied.Count == 0 && _resolver.ResolveRead(rel, profile).Source != ModuleDataSource.Profile)
            throw new InvalidOperationException($"{moduleDir}/{csvName} が本体にありません");
        return copied.Count > 0;
    }

    public Task<DataTable> ReadCsvAsync(string profile, string moduleDir, string csvName)
        => _file.ReadCsvAsDataTableAsync(_resolver.ResolveRead(ModuleRel(moduleDir, csvName), profile).AbsPath);

    public async Task<CsvAppendResult> AppendRowsAsync(
        string profile, string moduleDir, string csvName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        string? uniqueColumn = null, string? numberColumn = null, int numberStep = 10)
    {
        var materialized = await EnsureCsvAsync(profile, moduleDir, csvName);
        var path  = _resolver.ResolveWrite(ModuleRel(moduleDir, csvName), profile).AbsPath;
        var table = await _file.ReadCsvAsDataTableAsync(path);
        if (table.Columns.Count == 0)
            throw new InvalidOperationException($"{csvName} にヘッダーがありません");

        var existing = uniqueColumn is not null && table.Columns.Contains(uniqueColumn)
            ? new HashSet<string>(table.Rows.Cast<DataRow>().Select(r => Cell(r, uniqueColumn)), StringComparer.OrdinalIgnoreCase)
            : null;
        var number = numberColumn is not null && table.Columns.Contains(numberColumn) ? MaxNumber(table, numberColumn) : (int?)null;

        var added = 0; var skipped = 0;
        foreach (var src in rows)
        {
            if (existing is not null && uniqueColumn is not null)
            {
                var key = Get(src, uniqueColumn).Trim();
                if (key.Length > 0 && !existing.Add(key)) { skipped++; continue; }
            }

            var row = table.NewRow();
            foreach (DataColumn col in table.Columns)
                row[col] = Get(src, col.ColumnName);
            if (number is not null && numberColumn is not null)
            {
                number += numberStep;
                row[numberColumn] = number.Value.ToString();
            }
            table.Rows.Add(row);
            added++;
        }

        if (added > 0) await _file.WriteCsvFromDataTableAsync(path, table);
        return new CsvAppendResult(added, skipped, materialized);
    }

    public string AssetPath(string profile, string moduleDir, string relInModule)
        => _resolver.ResolveWrite(ModuleRel(moduleDir, relInModule), profile).AbsPath;

    public async Task<bool> EnsureProfileRowAsync(string profile, string moduleDir, string scriptFile)
    {
        var entry = (await _profiles.GetProfilesAsync()).FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"プロファイル {profile} がありません");
        var modules = (await _profiles.GetProfileModulesAsync(entry)).ToList();

        var suffix = $"/{ModuleName(moduleDir)}/{scriptFile}";
        if (modules.Any(m => m.ScriptPath.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return false;

        var master = (await _modules.GetAllModulesAsync()).FirstOrDefault(m =>
                         m.ModuleDir.Equals(ModuleName(moduleDir), StringComparison.OrdinalIgnoreCase)
                         && m.Script.Equals(scriptFile, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException($"{moduleDir}/{scriptFile} が本体の module.csv にありません");

        modules.Add(new ProfileScriptEntry
        {
            Order       = modules.Count > 0 ? modules.Max(m => m.Order) + 10 : 10,
            ScriptPath  = $"{master.Kind}/{master.ModuleDir}/{master.Script}",   // ProfileDetail の追加と同じ形
            Enabled     = "1",
            Description = master.MenuName,
        });
        await _profiles.SaveProfileModulesAsync(entry, modules);
        return true;
    }

    // ── 内部 ─────────────────────────────────────────────────────

    private string ModuleRel(string moduleDir, string rel)
        => _resolver.ModuleRelPath(moduleDir, rel)
           ?? throw new InvalidOperationException($"モジュール {moduleDir} が本体にありません");

    private static string ModuleName(string moduleDir)
        => Path.GetFileName((moduleDir ?? "").Trim().TrimEnd('/', '\\'));

    private static string Cell(DataRow r, string col) => (r[col]?.ToString() ?? "").Trim();

    private static string Get(IReadOnlyDictionary<string, string> src, string column)
    {
        foreach (var kv in src)
            if (kv.Key.Equals(column, StringComparison.OrdinalIgnoreCase)) return kv.Value ?? "";
        return "";
    }

    private static int MaxNumber(DataTable table, string column)
    {
        var max = 0;
        foreach (DataRow r in table.Rows)
            if (int.TryParse(Cell(r, column), out var n) && n > max) max = n;
        return max;
    }
}
