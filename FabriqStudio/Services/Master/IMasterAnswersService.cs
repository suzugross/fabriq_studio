using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// 回答ファイル <c>profiles/&lt;マスタ名&gt;.master.json</c> の読み書き。
/// </summary>
public interface IMasterAnswersService
{
    /// <summary>ワークスペース内に保存済みのマスタ名一覧（昇順）。</summary>
    Task<IReadOnlyList<string>> ListMasterNamesAsync();

    /// <summary>回答を読み込む。存在しなければ null。</summary>
    Task<MasterAnswers?> LoadAsync(string masterName);

    /// <summary>回答を保存する（UpdatedAt を更新）。</summary>
    Task SaveAsync(MasterAnswers answers);

    /// <summary>回答ファイルの絶対パス。</summary>
    string GetAnswersPath(string masterName);

    /// <summary>回答ファイルが存在するか。</summary>
    bool Exists(string masterName);
}
