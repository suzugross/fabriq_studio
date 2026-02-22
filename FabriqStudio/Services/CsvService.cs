using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;

namespace FabriqStudio.Services;

public class CsvService : ICsvService
{
    private readonly IAppSettingsService _settings;

    public CsvService(IAppSettingsService settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<T>> ReadAsync<T>(string relativePath)
    {
        var fullPath = Path.Combine(_settings.FabriqRootPath, relativePath);

        return await Task.Run(() =>
        {
            // _wpftmp ビルドプロジェクトでの IReaderConfiguration オーバーロード解決問題を回避するため
            // CultureInfo を直接渡す（CsvHelper デフォルト設定で動作）
            using var reader = new StreamReader(fullPath, Encoding.UTF8);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            return (IReadOnlyList<T>)csv.GetRecords<T>().ToList();
        });
    }
}
