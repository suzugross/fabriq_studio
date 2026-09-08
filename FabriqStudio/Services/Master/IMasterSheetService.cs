using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// マスタ設計の回答から、お客様提出用のパラメータシート（Excel）と作業確認用のチェックリスト（自己完結の HTML）を作る。
/// 生成計画とは独立。文書の組み立て（<see cref="Build"/>）はディスクに触らない。
/// </summary>
public interface IMasterSheetService
{
    /// <summary>
    /// テンプレートと回答を人が読める文書にする。<paramref name="plan"/> からは手動作業リストと、
    /// 実際に書くレジストリ行（項目ごとのキー・値）・グループポリシーの書き込み先を取る。
    /// </summary>
    SheetDocument Build(MasterTemplate template, MasterAnswers answers, MasterPlan plan);

    /// <summary>お客様提出用のパラメータシートを Excel ブック（.xlsx）として書く（パスワード・プロダクトキーも平文）。</summary>
    void SaveParameterSheetXlsx(SheetDocument doc, string path);

    /// <summary>作業確認用のチェックリスト HTML（チェック状態と備考はブラウザーの localStorage に保存）。</summary>
    string ToChecklistHtml(SheetDocument doc);
}
