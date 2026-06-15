using System.Data;
using System.IO;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class ModuleService : IModuleService
{
    private readonly IWorkspaceService _workspace;
    private readonly ICsvService       _csvService;
    private readonly IFileService      _fileService;

    public ModuleService(IWorkspaceService workspace, ICsvService csvService, IFileService fileService)
    {
        _workspace   = workspace;
        _csvService  = csvService;
        _fileService = fileService;
    }

    public async Task<IReadOnlyList<ModuleMasterEntry>> GetAllModulesAsync()
    {
        var root = _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

        var kinds  = new[] { "standard", "extended" };
        var result = new List<ModuleMasterEntry>();

        foreach (var kind in kinds)
        {
            var kindDir = Path.Combine(root, "modules", kind);
            if (!Directory.Exists(kindDir))
                continue;

            foreach (var moduleDir in Directory.GetDirectories(kindDir).OrderBy(d => d))
            {
                var moduleCsvPath = Path.Combine(moduleDir, "module.csv");
                if (!File.Exists(moduleCsvPath))
                    continue;

                var relativePath = Path.GetRelativePath(root, moduleCsvPath);
                var entries      = await _csvService.ReadAsync<ModuleMasterEntry>(relativePath);
                var moduleName   = Path.GetFileName(moduleDir);

                foreach (var entry in entries)
                {
                    entry.ModuleDir = moduleName;
                    entry.Kind      = kind;
                    result.Add(entry);
                }
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetModuleSegmentsAsync()
    {
        var root = _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

        // module.csv（メタ情報）と preset.csv（プリセット値）は設定 CSV ではないため除外する。
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "module.csv",
            ModulePresetService.PresetFileName,
        };

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var kinds  = new[] { "standard", "extended" };

        foreach (var kind in kinds)
        {
            var kindDir = Path.Combine(root, "modules", kind);
            if (!Directory.Exists(kindDir))
                continue;

            foreach (var moduleDir in Directory.GetDirectories(kindDir))
            {
                var moduleName = Path.GetFileName(moduleDir);
                // Ordinal distinct + 昇順（kernel common.ps1 が Segment を Trim 後に
                // case-sensitive 比較するのに合わせる）。
                var segments = new SortedSet<string>(StringComparer.Ordinal);

                foreach (var csvPath in Directory.GetFiles(moduleDir, "*.csv"))
                {
                    if (excluded.Contains(Path.GetFileName(csvPath)))
                        continue;

                    try
                    {
                        var table = await _fileService.ReadCsvAsDataTableAsync(csvPath);
                        if (!table.Columns.Contains("Segment"))
                            continue;

                        foreach (DataRow dr in table.Rows)
                        {
                            var seg = dr["Segment"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(seg))
                                segments.Add(seg);
                        }
                    }
                    catch
                    {
                        // 壊れた／非定型の CSV は候補収集対象から黙ってスキップ（ベストエフォート）。
                    }
                }

                if (segments.Count > 0)
                    result[moduleName] = segments.ToList();
            }
        }

        return result;
    }
}
