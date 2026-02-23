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

    /// <summary>
    /// テキストファイルを BOM 付き UTF-8 で書き込む。
    /// </summary>
    Task WriteTextAsync(string absolutePath, string content);

    /// <summary>
    /// DataTable の内容を BOM 付き UTF-8 CSV として書き込む（汎用CSV保存用）。
    /// </summary>
    Task WriteCsvFromDataTableAsync(string absolutePath, DataTable table);

    /// <summary>
    /// テキストファイルを1行1項目として読み込み、空行・重複を除外したリストを返す。
    /// ファイルインポート用。絶対パスを受け取る。
    /// </summary>
    Task<List<string>> LoadLinesFromFileAsync(string absolutePath);

    /// <summary>
    /// CSV ファイルを CsvHelper でモデルにマッピングして読み込む（絶対パス）。
    /// 外部ファイルインポート用。
    /// </summary>
    Task<List<T>> LoadCsvAsModelAsync<T>(string absolutePath);
}
