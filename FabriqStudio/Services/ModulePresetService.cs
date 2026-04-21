using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// モジュールごとの preset.csv を読み込み、列名ごとの候補リストへ整形するサービス。
/// <para>
/// preset.csv が存在しない／破損している場合は空辞書を返す（graceful degradation）。
/// fabriq 本体側の動作には一切影響させない。
/// </para>
/// </summary>
public class ModulePresetService : IModulePresetService
{
    /// <summary>preset.csv のファイル名（全モジュール共通）。</summary>
    public const string PresetFileName = "preset.csv";

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadAsync(string moduleDirAbsolutePath)
    {
        var empty = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(moduleDirAbsolutePath))
            return empty;

        var csvPath = Path.Combine(moduleDirAbsolutePath, PresetFileName);
        if (!File.Exists(csvPath))
            return empty;

        try
        {
            return await Task.Run(() => LoadSync(csvPath));
        }
        catch
        {
            // ファイル破損・ロック等は無視して空にフォールバック
            return empty;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadSync(string csvPath)
    {
        // BOM 付き UTF-8 / BOM なし UTF-8 のどちらも読めるよう detectEncodingFromByteOrderMarks を有効化
        using var reader = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // ヘッダー大小不一致を許容（AI 生成 CSV のブレに耐える）
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.Trim(),
            MissingFieldFound      = null,
            HeaderValidated        = null,
        };

        using var csv = new CsvReader(reader, config);
        var entries = csv.GetRecords<ModulePresetEntry>().ToList();

        // Column をキーに Value を並び順そのままで集約
        // 同一 (Column, Value) の重複は初出を優先して排除（CSV 書き間違い対策）
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var seen   = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var column = entry.Column?.Trim();
            if (string.IsNullOrEmpty(column)) continue;

            // Value は空文字も有効な候補として許容（例: optional な列のクリア用）
            var value = entry.Value ?? "";

            if (!result.TryGetValue(column, out var list))
            {
                list            = new List<string>();
                result[column]  = list;
                seen[column]    = new HashSet<string>(StringComparer.Ordinal);
            }

            if (seen[column].Add(value))
                list.Add(value);
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly(),
            StringComparer.OrdinalIgnoreCase);
    }
}
