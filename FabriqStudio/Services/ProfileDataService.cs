using System.IO;
using System.Text.RegularExpressions;

namespace FabriqStudio.Services;

public sealed class ProfileDataService : IProfileDataService
{
    private static readonly Regex RegListPattern = new(@"^reg_(hklm|hkcu)_list.*\.csv$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IModuleDataResolver _resolver;

    public ProfileDataService(IModuleDataResolver resolver)
    {
        _resolver = resolver;
    }

    public bool HasDataFolder(string profileName)
        => !string.IsNullOrWhiteSpace(profileName) && Directory.Exists(DataFolderPath(profileName));

    public string DataFolderPath(string profileName) => _resolver.DataSetRoot(profileName.Trim());

    public Task<IReadOnlyList<string>> MaterializeModuleAsync(string profileName, string moduleDir)
        => Task.Run(() => Materialize(profileName, moduleDir, only: null));

    public Task<IReadOnlyList<string>> MaterializeCsvAsync(string profileName, string moduleDir, string csvName)
        => Task.Run(() => Materialize(profileName, moduleDir, only: csvName));

    /// <summary>
    /// 取り込み本体。<paramref name="only"/> が null なら本体の設定 CSV 全部、指定ありならその 1 件
    /// （reg_* ならモジュールの reg_* 全部）。PDF に既にあるものは触らない。
    /// </summary>
    private IReadOnlyList<string> Materialize(string profileName, string moduleDir, string? only)
    {
        var copied  = new List<string>();
        var bodyRel = _resolver.ModuleRelPath(moduleDir);
        if (bodyRel is null) return copied;                      // 本体にモジュールが無い

        var bodyDir = _resolver.ResolveRead(bodyRel, null).AbsPath;
        if (!Directory.Exists(bodyDir)) return copied;

        var pdfModuleDir = _resolver.ResolveWrite(bodyRel, profileName).AbsPath;
        var pdfHasReg    = Directory.Exists(pdfModuleDir)
                           && Directory.EnumerateFiles(pdfModuleDir).Any(f => RegListPattern.IsMatch(Path.GetFileName(f)));

        var wantReg = only is not null && RegListPattern.IsMatch(only);
        foreach (var src in BodyCsvs(bodyDir))
        {
            var name  = Path.GetFileName(src);
            var isReg = RegListPattern.IsMatch(name);

            if (only is not null)
            {
                // 1 件指定: 通常 CSV はその名前だけ、reg_* は同モジュールの reg_* 全部
                if (wantReg ? !isReg : !name.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            }

            // PDF に reg_* が 1 件でもあれば本体の reg_* は読まれない状態なので、取り込むと挙動が変わる → 触らない
            if (isReg && pdfHasReg) continue;

            var dest = Path.Combine(pdfModuleDir, name);
            if (File.Exists(dest)) continue;                      // 既に PDF 側にある

            Directory.CreateDirectory(pdfModuleDir);              // コピーする瞬間だけ作る
            File.Copy(src, dest, overwrite: false);               // as-is（エンコーディング・改行もそのまま）
            copied.Add(name);
        }
        return copied;
    }

    public IReadOnlyList<string> FindMissingModules(string profileName, IEnumerable<string> moduleDirs)
    {
        var result = new List<string>();
        foreach (var dir in Distinct(moduleDirs))
        {
            var bodyRel = _resolver.ModuleRelPath(dir);
            if (bodyRel is null) continue;
            var bodyDir = _resolver.ResolveRead(bodyRel, null).AbsPath;
            if (!Directory.Exists(bodyDir)) continue;

            var pdfDir    = _resolver.ResolveWrite(bodyRel, profileName).AbsPath;
            var pdfNames  = Directory.Exists(pdfDir)
                ? new HashSet<string>(Directory.EnumerateFiles(pdfDir).Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pdfHasReg = pdfNames.Any(n => RegListPattern.IsMatch(n));

            var missing = BodyCsvs(bodyDir).Select(Path.GetFileName).OfType<string>()
                .Any(n => !pdfNames.Contains(n) && !(pdfHasReg && RegListPattern.IsMatch(n)));
            if (missing) result.Add(dir);
        }
        return result;
    }

    public IReadOnlyList<string> FindUnusedModules(string profileName, IEnumerable<string> usedModuleDirs)
    {
        var used = new HashSet<string>(Distinct(usedModuleDirs), StringComparer.OrdinalIgnoreCase);
        return _resolver.ListDataSetModules(profileName)
            .Where(n => !used.Contains(n))
            .ToList();
    }

    public void DeleteModuleData(string profileName, string moduleDir)
    {
        var name = Path.GetFileName(moduleDir.Trim().TrimEnd('/', '\\'));
        if (name.Length == 0) return;
        var dir = _resolver.DataSetModuleDir(profileName, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ── 内部 ─────────────────────────────────────────────────────

    /// <summary>本体側の設定 CSV（フレームワーク資産と拡張子違いを除く）。</summary>
    private IEnumerable<string> BodyCsvs(string bodyDir)
        => Directory.EnumerateFiles(bodyDir, "*.csv")
            .Where(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                        && !_resolver.IsFrameworkAsset(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Distinct(IEnumerable<string> dirs)
        => dirs.Select(d => Path.GetFileName((d ?? "").Trim().TrimEnd('/', '\\')))
               .Where(d => d.Length > 0)
               .Distinct(StringComparer.OrdinalIgnoreCase);
}
