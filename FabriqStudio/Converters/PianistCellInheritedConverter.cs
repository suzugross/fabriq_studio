using System.Globalization;
using System.Windows.Data;

namespace FabriqStudio.Converters;

/// <summary>
/// values.csv grid のセルが「継承表示中」かを判定する MultiValueConverter（§5.2.C）。
/// true のとき DataTrigger 経由で Foreground=#999999 + FontStyle=Italic を当てる。
///
/// 入力は <see cref="PianistCellDisplayConverter"/> と同じ。
///
/// 戻り値（bool）:
///   - 自セルが非空                 → false（自前の値を持っている）
///   - 自身が `*` 行                → false（継承元自身は継承しない）
///   - `*` 行の値が空               → false（実質「未定義」で表示するものが無い）
///   - それ以外（自セル空 + `*` 行に値あり） → true
/// </summary>
public class PianistCellInheritedConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3) return false;

        var own    = values[0] as string ?? "";
        var star   = values[1] as string ?? "";
        var isStar = values[2] is bool b && b;

        if (!string.IsNullOrEmpty(own)) return false;
        if (isStar)                     return false;
        return !string.IsNullOrEmpty(star);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
