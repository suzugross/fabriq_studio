using FabriqStudio.Models;

namespace FabriqStudio.Services;

public interface IModuleService
{
    /// <summary>
    /// modules/standard/ および modules/extended/ 以下の全モジュールを
    /// それぞれの module.csv から読み込み、マスターリストとして返す。
    /// </summary>
    Task<IReadOnlyList<ModuleMasterEntry>> GetAllModulesAsync();
}
