using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;

namespace FabriqStudio.Services;

public class FileService : IFileService
{
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public async Task<string?> ReadTextAsync(string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return null;

        return await File.ReadAllTextAsync(absolutePath);
    }

    public async Task<DataTable> ReadCsvAsDataTableAsync(string absolutePath)
    {
        var table = new DataTable();

        if (!File.Exists(absolutePath))
            return table;

        using var reader = new StreamReader(absolutePath);
        using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);

        await csv.ReadAsync();
        csv.ReadHeader();

        if (csv.HeaderRecord is not null)
        {
            foreach (var header in csv.HeaderRecord)
                table.Columns.Add(header);
        }

        while (await csv.ReadAsync())
        {
            var row = table.NewRow();
            foreach (DataColumn col in table.Columns)
                row[col] = csv.GetField(col.ColumnName) ?? "";
            table.Rows.Add(row);
        }

        return table;
    }

    public async Task WriteTextAsync(string absolutePath, string content)
        => await File.WriteAllTextAsync(absolutePath, content, Utf8Bom);

    public async Task<List<string>> LoadLinesFromFileAsync(string absolutePath)
    {
        var lines = await File.ReadAllLinesAsync(absolutePath);
        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct()
            .ToList();
    }

    public Task<List<T>> LoadCsvAsModelAsync<T>(string absolutePath)
    {
        return Task.Run(() =>
        {
            using var reader = new StreamReader(absolutePath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
            return csv.GetRecords<T>().ToList();
        });
    }

    public async Task WriteCsvFromDataTableAsync(string absolutePath, DataTable table)
    {
        using var writer = new StreamWriter(absolutePath, append: false, encoding: Utf8Bom);
        using var csv    = new CsvWriter(writer, CultureInfo.InvariantCulture);

        // ヘッダー行
        foreach (DataColumn col in table.Columns)
            csv.WriteField(col.ColumnName);
        await csv.NextRecordAsync();

        // データ行（DataRowState.Deleted の行はスキップ — row.Delete() 後に保存しても例外にならない）
        foreach (DataRow row in table.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            foreach (DataColumn col in table.Columns)
                csv.WriteField(row[col]?.ToString() ?? "");
            await csv.NextRecordAsync();
        }
    }
}
