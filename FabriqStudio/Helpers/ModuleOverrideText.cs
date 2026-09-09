using FabriqStudio.Services;

namespace FabriqStudio.Helpers;

/// <summary>
/// モジュール本体の編集画面（ModuleDetail / AppConfig）に出す、PDF 上書きの注意文。
/// fabriq は実行時に採用元を必ず表示するが、「本体を編集したのに PDF が勝って効かない」ことは
/// 編集時点では分からないので、Studio 側で前倒しに知らせる。
/// </summary>
public static class ModuleOverrideText
{
    public static string Summary(IReadOnlyList<ModuleOverride> overrides)
    {
        if (overrides.Count == 0) return "";

        var items = overrides.Select(o =>
        {
            var parts = new List<string>();
            if (o.Csvs.Count > 0)    parts.Add($"CSV {o.Csvs.Count} 件");
            if (o.Folders.Count > 0) parts.Add($"フォルダ: {string.Join(", ", o.Folders)}");
            return $"{o.DataSet}（{string.Join(" / ", parts)}）";
        });

        return $"⚠ このモジュールは {overrides.Count} 個のプロファイルのデータフォルダ（profiles/<名>/modules/）で上書きされています: "
               + string.Join("、", items)
               + "。ここで編集するのは本体側なので、それらのプロファイルの実行には反映されません。";
    }

    public static string? CsvNote(IReadOnlyList<ModuleOverride> overrides, string? csvName)
    {
        if (string.IsNullOrEmpty(csvName)) return null;

        var names = overrides
            .Where(o => o.Csvs.Any(c => c.Equals(csvName, StringComparison.OrdinalIgnoreCase)))
            .Select(o => o.DataSet)
            .ToList();
        if (names.Count == 0) return null;

        return $"表示中の {csvName} は {string.Join("、", names)} の実行では PDF 側のファイルが使われます（本体側のこのファイルは読まれません）。";
    }
}
