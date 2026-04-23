using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// 端末一覧をタイムスタンプ付きフォルダへエクスポートするサービス。
/// <para>
/// 出力内容: &lt;ParentFolder&gt;/hostlist_export_yyyyMMdd_HHmmss/
///   - hostlist.csv     データ本体（Decrypt=true なら ENC: を復号済）
///   - README.txt       タイムスタンプ・件数・暗号化状態・ユーザーメモ
/// </para>
/// </summary>
public interface IHostListExportService
{
    Task<HostListExportResult> ExportAsync(HostListExportRequest request);
}
