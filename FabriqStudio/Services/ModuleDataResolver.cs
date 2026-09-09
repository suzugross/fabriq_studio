using System.IO;
using System.Text.RegularExpressions;

namespace FabriqStudio.Services;

/// <summary>
/// <see cref="IModuleDataResolver"/> の実装。fabriq 側 kernel/common.ps1 の
/// Get-FabriqOverlayCandidate / Resolve-ModuleDataPath / Get-ModuleDataFiles と同じ規則。
/// </summary>
public sealed class ModuleDataResolver : IModuleDataResolver
{
    private static readonly string[] Tiers = ["standard", "extended"];

    /// <summary>複数 CSV 列挙（モジュール単位 all-or-nothing）の対象。fabriq の reg_hklm_delete 等が Get-ChildItem する形。</summary>
    private static readonly Regex RegListPattern = new(@"^reg_(hklm|hkcu)_list.*\.csv$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>フレームワーク資産（PDF に置かれても無視される。ブリーフ §4）。</summary>
    private static readonly HashSet<string> FrameworkFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "module.csv", "preset.csv", "VERSION", "REQUIRES_KERNEL", "Guide.txt", "test.psd1",
    };

    private readonly IWorkspaceService _workspace;

    public ModuleDataResolver(IWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public string RootPath => _workspace.RootPath
        ?? throw new InvalidOperationException("ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    public string DataSetRoot(string dataSet) => Path.GetFullPath(Path.Combine(RootPath, "profiles", dataSet.Trim()));

    public string DataSetModuleDir(string dataSet, string moduleDir)
        => Path.Combine(DataSetRoot(dataSet), "modules", ModuleName(moduleDir));

    public IReadOnlyList<string> ListDataSetModules(string dataSet)
    {
        var dir = Path.Combine(DataSetRoot(dataSet), "modules");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName).OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── パス組み立て ─────────────────────────────────────────────

    public string? ModuleRelPath(string moduleDir, string rel = "")
    {
        var name = ModuleName(moduleDir);
        foreach (var tier in Tiers)
        {
            if (Directory.Exists(Path.Combine(RootPath, "modules", tier, name)))
                return ModuleRelPath(tier, name, rel);
        }
        return null;
    }

    public string ModuleRelPath(string kind, string moduleDir, string rel)
    {
        var head = $"modules/{kind}/{ModuleName(moduleDir)}";
        var tail = Normalize(rel);
        return tail.Length == 0 ? head : $"{head}/{tail}";
    }

    // ── 写像（ブリーフ §2）────────────────────────────────────────

    public string MapToDataSet(string relPath, string? dataSet)
    {
        var rel = Normalize(relPath);
        if (string.IsNullOrWhiteSpace(dataSet)) return rel;

        // "modules" / <tier> / <module> [/ <rel...>] の形でなければ干渉しない
        var parts = rel.Split('/');
        if (parts.Length < 3 || !parts[0].Equals("modules", StringComparison.OrdinalIgnoreCase))
            return rel;

        var module = parts[2];
        var rest   = string.Join('/', parts.Skip(3));
        var mapped = $"profiles/{dataSet.Trim()}/modules/{module}";
        return rest.Length == 0 ? mapped : $"{mapped}/{rest}";
    }

    // ── 読み解決（ブリーフ §3 の粒度）─────────────────────────────

    public ResolvedModulePath ResolveRead(string relPath, string? dataSet)
    {
        var rel     = Normalize(relPath);
        var bodyAbs = Abs(rel);
        if (string.IsNullOrWhiteSpace(dataSet))
            return new ResolvedModulePath(bodyAbs, rel, ModuleDataSource.Identity, null);

        var mapped = MapToDataSet(rel, dataSet);
        if (mapped == rel)
            return new ResolvedModulePath(bodyAbs, rel, ModuleDataSource.Identity, dataSet);

        var pdfAbs = Abs(mapped);
        return IsAdoptedFromProfile(mapped, pdfAbs)
            ? new ResolvedModulePath(pdfAbs, mapped, ModuleDataSource.Profile, dataSet)
            : new ResolvedModulePath(bodyAbs, rel, ModuleDataSource.Fallback, dataSet);
    }

    /// <summary>
    /// PDF 側が採用されるか。
    /// 1) そのパスが PDF に存在する（ファイル／フォルダ）
    /// 2) reg_*_list*.csv は、PDF のモジュールフォルダに 1 件でも一致があれば PDF 側（モジュール単位）
    /// 3) 資材フォルダ配下のパスは、その資材フォルダが PDF にあれば PDF 側（フォルダ単位。ファイルが無くても本体で穴埋めしない）
    /// </summary>
    private static bool IsAdoptedFromProfile(string mappedRel, string pdfAbs)
    {
        if (File.Exists(pdfAbs) || Directory.Exists(pdfAbs)) return true;

        // mapped = profiles/<名>/modules/<module>[/<rest...>]
        var parts = mappedRel.Split('/');
        if (parts.Length <= 4) return false;               // モジュールルートそのものが無い
        var moduleAbs = Path.GetFullPath(Path.Combine(pdfAbs, string.Concat(Enumerable.Repeat(".." + Path.DirectorySeparatorChar, parts.Length - 4))));

        var leaf = parts[^1];
        if (parts.Length == 5 && RegListPattern.IsMatch(leaf))
            return HasRegListFiles(moduleAbs);

        // 資材フォルダ判定: モジュール直下のフォルダから葉の親まで、どれかが PDF に存在すれば採用
        var dir = moduleAbs;
        for (var i = 4; i < parts.Length - 1; i++)
        {
            dir = Path.Combine(dir, parts[i]);
            if (Directory.Exists(dir)) return true;
        }
        return false;
    }

    private static bool HasRegListFiles(string moduleDirAbs)
        => Directory.Exists(moduleDirAbs)
           && Directory.EnumerateFiles(moduleDirAbs).Any(f => RegListPattern.IsMatch(Path.GetFileName(f)));

    // ── 書き解決（ブリーフ §6）─────────────────────────────────────

    public ResolvedModulePath ResolveWrite(string relPath, string? dataSet)
    {
        var rel = Normalize(relPath);
        if (string.IsNullOrWhiteSpace(dataSet))
            return new ResolvedModulePath(Abs(rel), rel, ModuleDataSource.Identity, null);

        var mapped = MapToDataSet(rel, dataSet);
        return mapped == rel
            ? new ResolvedModulePath(Abs(rel), rel, ModuleDataSource.Identity, dataSet)
            : new ResolvedModulePath(Abs(mapped), mapped, ModuleDataSource.Profile, dataSet);   // ディレクトリは作らない
    }

    // ── 合成一覧 ─────────────────────────────────────────────────

    public IReadOnlyList<ModuleDataEntry> EnumerateCsvs(string moduleDir, string? dataSet)
    {
        var name    = ModuleName(moduleDir);
        var bodyRel = ModuleRelPath(name);
        var bodyDir = bodyRel is null ? null : Abs(bodyRel);
        var pdfDir  = string.IsNullOrWhiteSpace(dataSet) ? null : Path.Combine(DataSetRoot(dataSet), "modules", name);

        var result    = new Dictionary<string, ModuleDataEntry>(StringComparer.OrdinalIgnoreCase);
        var pdfHasReg = false;

        if (pdfDir is not null && Directory.Exists(pdfDir))
        {
            foreach (var f in CsvFiles(pdfDir))
            {
                var n = Path.GetFileName(f);
                if (RegListPattern.IsMatch(n)) pdfHasReg = true;
                result[n] = new ModuleDataEntry(n, ModuleDataSource.Profile, f);
            }
        }

        if (bodyDir is not null && Directory.Exists(bodyDir))
        {
            foreach (var f in CsvFiles(bodyDir))
            {
                var n = Path.GetFileName(f);
                if (result.ContainsKey(n)) continue;
                if (pdfHasReg && RegListPattern.IsMatch(n)) continue;   // モジュール単位 all-or-nothing: 本体側とは合成しない
                result[n] = new ModuleDataEntry(n, pdfDir is null ? ModuleDataSource.Identity : ModuleDataSource.Fallback, f);
            }
        }

        return result.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<ModuleOverride> FindOverrides(string moduleDir)
    {
        var list = new List<ModuleOverride>();
        var name = ModuleName(moduleDir);
        if (name.Length == 0 || _workspace.RootPath is null) return list;

        try
        {
            var profilesDir = Path.Combine(RootPath, "profiles");
            if (!Directory.Exists(profilesDir)) return list;

            foreach (var pdf in Directory.GetDirectories(profilesDir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var dir = Path.Combine(pdf, "modules", name);
                if (!Directory.Exists(dir)) continue;

                var csvs = CsvFiles(dir).Select(Path.GetFileName).OfType<string>()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                var folders = Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>()
                    .Where(n => !IsFrameworkAsset(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                if (csvs.Count == 0 && folders.Count == 0) continue;

                list.Add(new ModuleOverride(Path.GetFileName(pdf), csvs, folders));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return list;
    }

    public bool IsFrameworkAsset(string fileName)
        => FrameworkFiles.Contains(fileName)
           || fileName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("tools", StringComparison.OrdinalIgnoreCase);

    // ── 内部ヘルパ ────────────────────────────────────────────────

    /// <summary>設定 CSV（拡張子が厳密に .csv で、フレームワーク資産でないもの）。</summary>
    private IEnumerable<string> CsvFiles(string dir)
        => Directory.EnumerateFiles(dir, "*.csv")
            .Where(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                        && !IsFrameworkAsset(Path.GetFileName(f)));

    /// <summary>"modules/standard/x" でも "x" でもモジュール名 "x" を返す。</summary>
    private static string ModuleName(string moduleDir)
        => Path.GetFileName((moduleDir ?? "").Trim().TrimEnd('/', '\\'));

    private string Abs(string rel)
        => Path.GetFullPath(Path.Combine(RootPath, rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>区切りを '/' に揃え、"." と ".." を畳む（ブリーフ §2: 判定前に正規化する）。</summary>
    private static string Normalize(string path)
    {
        var stack = new List<string>();
        foreach (var seg in (path ?? "").Replace('\\', '/').Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(seg);
        }
        return string.Join('/', stack);
    }
}
