namespace FabriqStudio.Models;

/// <summary>
/// 端末一覧エクスポートの入力パラメータ。
/// </summary>
/// <param name="Hosts">エクスポート対象の HostEntry 一覧（UI 側の現在状態を渡す）</param>
/// <param name="ParentFolder">ユーザーが選択した親フォルダ。この下にタイムスタンプ付きサブフォルダが作られる</param>
/// <param name="Memo">README.txt に書き込むユーザーメモ（空文字可）</param>
/// <param name="Decrypt">true の場合、ENC: プレフィクス付きセルを復号してから CSV に書き出す</param>
public record HostListExportRequest(
    IReadOnlyList<HostEntry> Hosts,
    string ParentFolder,
    string Memo,
    bool Decrypt);

/// <summary>
/// 端末一覧エクスポートの実行結果。
/// </summary>
/// <param name="ExportFolderPath">実際に生成されたエクスポートフォルダの絶対パス</param>
/// <param name="HostCount">出力された端末数</param>
/// <param name="DecryptedCells">復号成功したセル数（Decrypt=false の場合は 0）</param>
/// <param name="RemainingEncCells">出力された CSV に残っている ENC: セル数（復号失敗や Decrypt=false により残ったもの）</param>
/// <param name="Errors">復号失敗などのエラーメッセージ</param>
public record HostListExportResult(
    string ExportFolderPath,
    int HostCount,
    int DecryptedCells,
    int RemainingEncCells,
    IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}
