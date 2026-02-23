using FabriqStudio.Models;

namespace FabriqStudio.Services;

public interface IDigitalGyotaqService
{
    /// <summary>
    /// 指定パスの task_list.csv を読み込んで GyotaqTask リストを返す。
    /// </summary>
    Task<IReadOnlyList<GyotaqTask>> LoadTaskListAsync(string filePath);

    /// <summary>
    /// GyotaqTask リストを指定パスの task_list.csv に保存する。
    /// Enabled は 0/1 として書き込む。
    /// </summary>
    Task SaveTaskListAsync(string filePath, IEnumerable<GyotaqTask> tasks);

    /// <summary>
    /// アプリ同梱のコマンドライブラリ (uri_list.csv) を読み込む。
    /// </summary>
    Task<IReadOnlyList<GyotaqCommand>> LoadCommandLibraryAsync();

    /// <summary>
    /// modules/extended/{moduleName}/ 以下にモジュールをエクスポートする。
    /// <para>
    ///   生成ファイル:
    ///   <list type="bullet">
    ///     <item><description>gyotaq_config.ps1 — テンプレートのコピー</description></item>
    ///     <item><description>module.csv — モジュールメタデータ</description></item>
    ///     <item><description>task_list.csv — ユーザーのタスクリスト</description></item>
    ///     <item><description>README.txt — テンプレートのコピー（存在する場合）</description></item>
    ///   </list>
    /// </para>
    /// </summary>
    /// <param name="moduleName">モジュール名（フォルダ名・メニュー表示名に使用）</param>
    /// <param name="tasks">タスクリスト</param>
    /// <param name="overwrite">true の場合、既存ディレクトリを削除して上書き</param>
    /// <returns>出力先フォルダの絶対パス</returns>
    /// <exception cref="ArgumentException">名前が空または禁則文字を含む</exception>
    /// <exception cref="InvalidOperationException">overwrite=false で同名ディレクトリが既に存在する</exception>
    Task<string> ExportModuleAsync(string moduleName, IEnumerable<GyotaqTask> tasks, bool overwrite = false);
}
