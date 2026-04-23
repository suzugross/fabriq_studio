namespace FabriqStudio.Models;

/// <summary>
/// fabriq バックアップの入力パラメータ。
/// </summary>
/// <param name="SourceRoot">コピー元の fabriq ルート（ワークスペース絶対パス）</param>
/// <param name="ParentFolder">ユーザーが選択した出力先の親フォルダ。この下にタイムスタンプ付きフォルダが作られる</param>
/// <param name="Memo">USER_MEMO.txt に書き込むユーザーメモ（空可）</param>
public record FabriqBackupRequest(
    string SourceRoot,
    string ParentFolder,
    string Memo);

/// <summary>
/// fabriq バックアップの実行結果。
/// </summary>
/// <param name="BackupFolderPath">生成されたバックアップフォルダの絶対パス</param>
/// <param name="CopiedFileCount">コピーされたファイル総数（素材フォルダ含む）</param>
/// <param name="ExcludedFileCount">ポリシーで除外されたファイル数（素材フォルダ内は除く）</param>
/// <param name="TotalBytes">コピーされた合計バイト数</param>
/// <param name="Errors">コピー失敗などのエラーメッセージ</param>
public record FabriqBackupResult(
    string BackupFolderPath,
    int CopiedFileCount,
    int ExcludedFileCount,
    long TotalBytes,
    IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}
