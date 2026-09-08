using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>既定のアプリ関連付け（AppAssoc.xml）の編集支援: 同梱ひな形、既知 ProgId 辞書、この PC の登録アプリ、Dism エクスポート。</summary>
public interface IAppAssocService
{
    /// <summary>同梱のひな形（master_template/appassoc_base.xml）。</summary>
    string BaseXmlPath { get; }

    Task EnsureLoadedAsync();

    IReadOnlyList<AppAssocApp>      Apps       { get; }
    IReadOnlyList<AppAssocCategory> Categories { get; }

    /// <summary>この PC のレジストリ（RegisteredApplications / OpenWithProgids）から、識別子を扱えるアプリの候補を返す。</summary>
    IReadOnlyList<AppAssocCandidate> LocalCandidates(string identifier);

    /// <summary>
    /// この PC の既定のアプリを Dism でエクスポートする（UAC 昇格の確認が出る）。
    /// 成功したら一時ファイルのパス、キャンセル／失敗なら null。
    /// </summary>
    Task<string?> ExportFromThisPcAsync();
}
