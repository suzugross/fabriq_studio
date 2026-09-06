using System.IO;
using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

public sealed class MasterAssetService : IMasterAssetService
{
    private readonly IMasterTargetResolver         _resolver;
    private readonly IInstallerCatalogService      _catalog;
    private readonly IPrinterDriverDetectorService _printerDetector;

    public MasterAssetService(
        IMasterTargetResolver         resolver,
        IInstallerCatalogService      catalog,
        IPrinterDriverDetectorService printerDetector)
    {
        _resolver        = resolver;
        _catalog         = catalog;
        _printerDetector = printerDetector;
    }

    public async Task<AssetDropResult> ImportAsync(
        MasterDropSpec spec, IReadOnlyList<string> paths, Func<string, bool> confirmOverwrite)
    {
        var result = new AssetDropResult();

        var moduleDir = _resolver.FindModuleDir(spec.Module);
        if (moduleDir is null)
        {
            result.Errors.Add($"モジュール {spec.Module} がワークスペースにありません。");
            return result;
        }

        var targetDir = Path.Combine(moduleDir, spec.SubDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(targetDir);
        result.TargetRelPath = _resolver.ToRelative(targetDir);

        await _catalog.EnsureLoadedAsync();

        foreach (var raw in paths)
        {
            var path = raw.Trim();
            try
            {
                if (Directory.Exists(path))
                {
                    if (!spec.Folders)
                    {
                        result.Skipped.Add($"{Path.GetFileName(path)}（フォルダは受け付けません）");
                        continue;
                    }
                    await ImportFolderAsync(spec, path, targetDir, confirmOverwrite, result);
                    continue;
                }

                if (!File.Exists(path))
                {
                    result.Errors.Add($"{path} が見つかりません。");
                    continue;
                }

                // .inf を直接ドロップされたら、ドライバ一式であるフォルダごと取り込む
                if (spec.Kind == MasterDropKinds.PrinterDriver
                    && Path.GetExtension(path).Equals(".inf", StringComparison.OrdinalIgnoreCase)
                    && spec.Folders)
                {
                    var parent = Path.GetDirectoryName(path);
                    if (parent is not null)
                    {
                        await ImportFolderAsync(spec, parent, targetDir, confirmOverwrite, result);
                        continue;
                    }
                }

                if (!spec.AcceptsExtension(path))
                {
                    result.Skipped.Add($"{Path.GetFileName(path)}（対象外の拡張子。受け付けるのは {string.Join(" ", spec.Extensions)}）");
                    continue;
                }

                var destName = string.IsNullOrWhiteSpace(spec.FixedName) ? Path.GetFileName(path) : spec.FixedName!;
                var dest     = Path.Combine(targetDir, destName);

                if (File.Exists(dest) && !SamePath(dest, path) && !confirmOverwrite(destName))
                {
                    result.Skipped.Add($"{destName}（上書きをキャンセル）");
                    continue;
                }

                if (!SamePath(dest, path))
                    await Task.Run(() => File.Copy(path, dest, overwrite: true));

                result.Entries.Add(spec.Kind switch
                {
                    MasterDropKinds.Installer => BuildInstallerEntry(dest, destName),
                    MasterDropKinds.PrinterDriver => new AssetDropEntry
                    {
                        FileName = destName,
                        Source   = "EXE/ZIP（fabriq が実行時に自動展開。DriverName は INF を確認して入力）",
                    },
                    _ => new AssetDropEntry { FileName = destName },
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return result;
    }

    private AssetDropEntry BuildInstallerEntry(string destPath, string destName)
    {
        var s = _catalog.Suggest(destPath);
        return new AssetDropEntry
        {
            FileName   = destName,
            AppName    = s.AppName,
            Type       = s.Type,
            SilentArgs = s.SilentArgs,
            Version    = s.Version,
            Source     = s.NeedsReview && !s.Source.StartsWith("カタログ", StringComparison.Ordinal)
                ? $"{s.Source} → 要確認"
                : s.Source,
        };
    }

    private async Task ImportFolderAsync(
        MasterDropSpec spec, string sourceDir, string targetDir,
        Func<string, bool> confirmOverwrite, AssetDropResult result)
    {
        var name = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dest = Path.Combine(targetDir, name);

        if (Directory.Exists(dest) && !SamePath(dest, sourceDir) && !confirmOverwrite(name + @"\"))
        {
            result.Skipped.Add($"{name}\\（上書きをキャンセル）");
            return;
        }

        if (!SamePath(dest, sourceDir))
            await Task.Run(() => CopyDirectory(sourceDir, dest));

        var entry = new AssetDropEntry { FileName = name, IsFolder = true };

        if (spec.Kind == MasterDropKinds.PrinterDriver)
        {
            try
            {
                var drivers = await _printerDetector.ScanAsync(dest);
                var names   = drivers.Select(d => d.DriverName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                entry = new AssetDropEntry
                {
                    FileName    = name,
                    IsFolder    = true,
                    DriverNames = names,
                    Source      = names.Count > 0 ? $"INF から {names.Count} 件のモデル名を検出" : "INF が見つかりません（DriverName を手入力）",
                };
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{name}: INF の解析に失敗 ({ex.Message})");
            }
        }

        result.Entries.Add(entry);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
