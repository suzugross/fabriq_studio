using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FabriqStudio.Converters;

/// <summary>
/// null または空文字 → Collapsed、それ以外 → Visible。
/// 文字列以外のオブジェクトの場合は null → Collapsed、非 null → Visible。
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
            return string.IsNullOrEmpty(str) ? Visibility.Collapsed : Visibility.Visible;
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
