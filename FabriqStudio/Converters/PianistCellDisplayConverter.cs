using System.Globalization;
using System.Windows.Data;

namespace FabriqStudio.Converters;

/// <summary>
/// values.csv grid のセル表示文字列を計算する MultiValueConverter（§5.2.C 継承表示）。
///
/// 入力（順序固定）:
///   [0] string  : 自セル値（<c>row[col]</c> binding）
///   [1] string  : `*` 行の同列値（<c>Table.Star[col]</c> binding）
///   [2] bool    : この行が `*` 行かどうか（<c>row.IsStar</c> binding）
///
/// 戻り値:
///   - 自セルが非空 → 自セル値（編集された値）
///   - 自セルが空かつ自身が `*` 行 → 空（`*` 行は継承元なので継承表示しない）
///   - 自セルが空かつ非 `*` 行 → `*` 行の値（dim italic 描画は <see cref="PianistCellInheritedConverter"/>
///     による Style trigger 側で適用）
/// </summary>
public class PianistCellDisplayConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3) return "";

        var own    = values[0] as string ?? "";
        var star   = values[1] as string ?? "";
        var isStar = values[2] is bool b && b;

        if (!string.IsNullOrEmpty(own)) return own;
        if (isStar)                     return "";
        return star;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
