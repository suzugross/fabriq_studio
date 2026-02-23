using System.Globalization;
using System.Windows.Data;

namespace FabriqStudio.Converters;

/// <summary>
/// bool を反転する。ComboBox の IsEnabled="{Binding IsLocked, Converter=...}" で
/// IsLocked=true のとき IsEnabled=false とするために使用する。
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
