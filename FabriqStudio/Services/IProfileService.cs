using FabriqStudio.Models;

namespace FabriqStudio.Services;

public interface IProfileService
{
    /// <summary>
    /// profiles/ ディレクトリ直下の .csv ファイルをプロファイル一覧として返す。
    /// ファイル名（拡張子なし）をプロファイル名とする。
    /// </summary>
    Task<IReadOnlyList<ProfileEntry>> GetProfilesAsync();

    /// <summary>
    /// 指定プロファイルの実行モジュールリストを Order 順で返す。
    /// </summary>
    /// <param name="profile">対象プロファイル（FilePath を使用）</param>
    Task<IReadOnlyList<ProfileScriptEntry>> GetProfileModulesAsync(ProfileEntry profile);

    /// <summary>
    /// 指定プロファイルのモジュールリストをCSVに上書き保存する。
    /// 渡した順番で Order を 10 刻みで振り直してから書き込む。
    /// </summary>
    Task SaveProfileModulesAsync(ProfileEntry profile, IEnumerable<ProfileScriptEntry> modules);

    /// <summary>
    /// 指定した名前で新しい空のプロファイル CSV を作成し、その ProfileEntry を返す。
    /// </summary>
    /// <param name="profileName">プロファイル名（拡張子なしでも可）</param>
    /// <exception cref="ArgumentException">名前が空、または OS 禁則文字を含む場合</exception>
    /// <exception cref="InvalidOperationException">同名ファイルが既に存在する場合</exception>
    Task<ProfileEntry> CreateProfileAsync(string profileName);
}
