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

    public async Task WriteAsync<T>(string relativePath, IEnumerable<T> records)
    {
        var fullPath = Path.Combine(_settings.FabriqRootPath, relativePath);

        await Task.Run(() =>
        {
            // BOM付き UTF-8 で書き込む（PowerShell 5.1 の Import-Csv が BOM を手がかりに
            // エンコーディングを自動判定するため、日本語環境での文字化けを防ぐ）
            using var writer = new StreamWriter(fullPath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteRecords(records);
        });
    }
}
