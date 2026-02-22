using System.IO;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class ProfileService : IProfileService
{
    private readonly IAppSettingsService _settings;
    private readonly ICsvService         _csvService;

    public ProfileService(IAppSettingsService settings, ICsvService csvService)
    {
        _settings   = settings;
        _csvService  = csvService;
    }

    public Task<IReadOnlyList<ProfileEntry>> GetProfilesAsync()
    {
        // ディレクトリスキャンは高速（数ms以下）なため Task.Run は不要。
        // Task.Run を使うと _wpftmp ビルドプロジェクトで戻り型推論が失敗するため避ける。
        var profilesDir = Path.Combine(_settings.FabriqRootPath, "profiles");

        if (!Directory.Exists(profilesDir))
            return Task.FromResult<IReadOnlyList<ProfileEntry>>(Array.Empty<ProfileEntry>());

        // profiles/ 直下の .csv のみ（easy_template/ 等のサブディレクトリは除外）
        IReadOnlyList<ProfileEntry> result = Directory
            .GetFiles(profilesDir, "*.csv", SearchOption.TopDirectoryOnly)
            .Select(f => new ProfileEntry
            {
                Name     = Path.GetFileNameWithoutExtension(f),
                FilePath = f
            })
            .OrderBy(p => p.Name)
            .ToArray();

        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<ProfileScriptEntry>> GetProfileModulesAsync(ProfileEntry profile)
    {
        // ProfileEntry.FilePath は絶対パスのため、FabriqRootPath からの相対パスに変換して
        // 既存の ICsvService.ReadAsync を再利用する。
        var relativePath = Path.GetRelativePath(_settings.FabriqRootPath, profile.FilePath);
        var modules      = await _csvService.ReadAsync<ProfileScriptEntry>(relativePath);

        // ファイルの記述順ではなく Order カラムで確実にソートして返す
        return modules.OrderBy(m => m.Order).ToList();
    }
}
