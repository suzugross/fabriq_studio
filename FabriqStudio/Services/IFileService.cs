using System.Data;

namespace FabriqStudio.Services;

public interface IFileService
{
    /// <summary>
    /// テキストファイルを読み込む。ファイルが存在しない場合は null を返す。
    /// </summary>
    Task<string?> ReadTextAsync(string absolutePath);

    /// <summary>
    /// CSV ファイルを DataTable として読み込む（カラム構造が不定な汎用CSV向け）。
    /// ファイルが存在しない場合は空の DataTable を返す。
    /// </summary>
    Task<DataTable> ReadCsvAsDataTableAsync(string absolutePath);
}
