using FabriqStudio.Models;

namespace FabriqStudio.Services;

public interface IAutokeyService
{
    /// <summary>
    /// 指定パスの recipe.csv を読み込んで RecipeRow リストを返す。
    /// filePath は絶対パス。
    /// </summary>
    Task<IReadOnlyList<RecipeRow>> LoadRecipeAsync(string filePath);

    /// <summary>
    /// RecipeRow リストを指定パスの recipe.csv に保存する。
    /// Step を 1 始まりの連番で振り直してから書き込む。
    /// filePath は絶対パス。
    /// </summary>
    Task SaveRecipeAsync(string filePath, IEnumerable<RecipeRow> rows);

    /// <summary>
    /// modules/extended/{moduleName}/ 以下に 3 ファイルをエクスポートする。
    /// <para>
    ///   生成ファイル:
    ///   <list type="bullet">
    ///     <item><description>{moduleName}_autokey_config.ps1 — テンプレートのコピー</description></item>
    ///     <item><description>module.csv — モジュールメタデータ</description></item>
    ///     <item><description>recipe.csv — ユーザーのレシピ</description></item>
    ///   </list>
    /// </para>
    /// </summary>
    /// <param name="moduleName">モジュール名（フォルダ名・スクリプト名に使用）</param>
    /// <param name="rows">レシピ行</param>
    /// <param name="overwrite">true の場合、既存ディレクトリを削除して上書き</param>
    /// <returns>出力先フォルダの絶対パス</returns>
    /// <exception cref="ArgumentException">名前が空または禁則文字を含む</exception>
    /// <exception cref="InvalidOperationException">overwrite=false で同名ディレクトリが既に存在する</exception>
    Task<string> ExportModuleAsync(string moduleName, IEnumerable<RecipeRow> rows, bool overwrite = false);

    /// <summary>
    /// レシピを一時ディレクトリにコピーして別 PowerShell プロセスでテスト実行する。
    /// </summary>
    Task TestRunAsync(IEnumerable<RecipeRow> rows);
}
