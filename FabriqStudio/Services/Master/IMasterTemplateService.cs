using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// マスタ設計テンプレート（exe 同梱 master_template/master_template.json）の読み込み。
/// ワークスペース非依存。
/// </summary>
public interface IMasterTemplateService
{
    /// <summary>テンプレートの絶対パス（表示・診断用）。</summary>
    string TemplatePath { get; }

    /// <summary>
    /// テンプレートを読み込む。ファイル不在・破損時は例外を投げる
    /// （画面側でエラー表示する。空テンプレートで黙って進めない）。
    /// </summary>
    Task<MasterTemplate> LoadAsync();
}
