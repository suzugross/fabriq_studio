using System.IO;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class ProfileService : IProfileService
{
    private readonly IAppSettingsService _settings;

    public ProfileService(IAppSettingsService settings)
    {
        _settings = settings;
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
}
