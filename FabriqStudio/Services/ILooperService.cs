using FabriqStudio.Models;

namespace FabriqStudio.Services;

public interface ILooperService
{
    /// <summary>
    /// 指定パスの looper_list.csv を読み込んで LooperEntry リストを返す。
    /// filePath は絶対パス。
    /// </summary>
    Task<IReadOnlyList<LooperEntry>> LoadLooperListAsync(string filePath);

    /// <summary>
    /// LooperEntry リストを指定パスの looper_list.csv に保存する。
    /// filePath は絶対パス。
    /// </summary>
    Task SaveLooperListAsync(string filePath, IEnumerable<LooperEntry> entries);

    /// <summary>
    /// modules/extended/{moduleName}/ 以下にループモジュールをエクスポートする。
    /// <para>
    ///   生成ファイル:
    ///   <list type="bullet">
    ///     <item><description>script_looper.ps1 — テンプレートのコピー</description></item>
    ///     <item><description>module.csv — モジュールメタデータ</description></item>
    ///     <item><description>looper_list.csv — ユーザーのループ設定</description></item>
    ///     <item><description>Guide.txt — 操作ガイド</description></item>
    ///   </list>
    /// </para>
    /// </summary>
    /// <param name="moduleName">モジュール名（フォルダ名に使用）</param>
    /// <param name="entries">ループエントリ</param>
    /// <param name="overwrite">true の場合、既存ディレクトリを削除して上書き</param>
    /// <returns>出力先フォルダの絶対パス</returns>
    /// <exception cref="ArgumentException">名前が空または禁則文字を含む</exception>
    /// <exception cref="InvalidOperationException">overwrite=false で同名ディレクトリが既に存在する</exception>
    Task<string> ExportModuleAsync(string moduleName, IEnumerable<LooperEntry> entries, bool overwrite = false);

    /// <summary>
    /// ループ設定を一時ディレクトリにコピーし、カーネルをドットソースした PowerShell プロセスでテスト実行する。
    /// カーネルは Dual Resolution（ワークスペース → テンプレートフォールバック）で解決する。
    /// 作業ディレクトリをワークスペースルートに設定するため、looper_list.csv 内の相対パスも解決される。
    /// </summary>
    /// <returns>実行ログ（標準出力 + 標準エラー出力）</returns>
    Task<string> TestRunAsync(IEnumerable<LooperEntry> entries);
}
