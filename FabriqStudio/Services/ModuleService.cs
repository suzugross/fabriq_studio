using System.IO;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class ModuleService : IModuleService
{
    private readonly IAppSettingsService _settings;
    private readonly ICsvService         _csvService;

    public ModuleService(IAppSettingsService settings, ICsvService csvService)
    {
        _settings   = settings;
        _csvService = csvService;
    }

    public async Task<IReadOnlyList<ModuleMasterEntry>> GetAllModulesAsync()
    {
        var kinds  = new[] { "standard", "extended" };
        var result = new List<ModuleMasterEntry>();

        foreach (var kind in kinds)
        {
            var kindDir = Path.Combine(_settings.FabriqRootPath, "modules", kind);
            if (!Directory.Exists(kindDir))
                continue;

            foreach (var moduleDir in Directory.GetDirectories(kindDir).OrderBy(d => d))
            {
                var moduleCsvPath = Path.Combine(moduleDir, "module.csv");
                if (!File.Exists(moduleCsvPath))
                    continue;

                var relativePath = Path.GetRelativePath(_settings.FabriqRootPath, moduleCsvPath);
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
