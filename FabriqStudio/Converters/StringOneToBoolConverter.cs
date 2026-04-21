using System.Globalization;
using System.Windows.Data;

namespace FabriqStudio.Converters;

/// <summary>
/// CSV セル値（文字列）と CheckBox.IsChecked（bool）の相互変換。
/// <list type="bullet">
///   <item>読み: 前後空白を除いた値が <c>"1"</c> のときのみ true。それ以外（<c>"0"</c>, 空, null, 他）は false</item>
///   <item>書き: true → <c>"1"</c>、false → <c>"0"</c></item>
/// </list>
/// <para>
/// 用途: <c>DataRowView[Enabled]</c>（string）を CheckBox にバインドする際に使用する。
/// fabriq の PowerShell が読む値形式（"0"/"1"）を保ったまま、WPF UI では bool として扱える。
/// </para>
/// </summary>
[ValueConversion(typeof(string), typeof(bool))]
public class StringOneToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals((value?.ToString() ?? string.Empty).Trim(), "1", StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "1" : "0";
}
