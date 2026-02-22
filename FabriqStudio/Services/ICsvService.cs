namespace FabriqStudio.Services;

public interface ICsvService
{
    /// <summary>
    /// fabriqRootPath からの相対パスで CSV を読み込み、モデルリストを返す。
    /// </summary>
    /// <typeparam name="T">CSVの各行にマッピングするモデル型</typeparam>
    /// <param name="relativePath">fabriqRootPath からの相対パス (例: "kernel/csv/hostlist.csv")</param>
    Task<IReadOnlyList<T>> ReadAsync<T>(string relativePath);
}
