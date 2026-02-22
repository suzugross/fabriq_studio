using System.IO;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class ModuleService : IModuleService
{
    private readonly IWorkspaceService _workspace;
    private readonly ICsvService       _csvService;

    public ModuleService(IWorkspaceService workspace, ICsvService csvService)
    {
        _workspace  = workspace;
        _csvService = csvService;
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
}
