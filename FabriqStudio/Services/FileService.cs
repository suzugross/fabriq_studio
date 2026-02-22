using System.Data;
using System.Globalization;
using System.IO;
using CsvHelper;

namespace FabriqStudio.Services;

public class FileService : IFileService
{
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
}
