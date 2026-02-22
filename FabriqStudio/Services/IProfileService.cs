using FabriqStudio.Models;

namespace FabriqStudio.Services;

public interface IProfileService
{
    /// <summary>
    /// profiles/ ディレクトリ直下の .csv ファイルをプロファイル一覧として返す。
    /// ファイル名（拡張子なし）をプロファイル名とする。
    /// </summary>
    Task<IReadOnlyList<ProfileEntry>> GetProfilesAsync();
}
