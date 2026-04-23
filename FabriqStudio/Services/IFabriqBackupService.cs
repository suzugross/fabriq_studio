using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// fabriq ディレクトリ構造を再現しながら、PS1 やバージョン管理ファイル等を除外した
/// バックアップコピーを作成するサービス。
/// <para>
/// 出力: &lt;ParentFolder&gt;/fabriq_backup_yyyyMMdd_HHmmss/ 配下に fabriq のミラー。
/// ルート直下に USER_MEMO.txt と BACKUP_INFO.txt を配置する。
/// </para>
/// </summary>
public interface IFabriqBackupService
{
    Task<FabriqBackupResult> BackupAsync(FabriqBackupRequest request);
}
