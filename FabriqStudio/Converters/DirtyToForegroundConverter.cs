using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FabriqStudio.Converters;

/// <summary>
/// 現在値とスナップショット値を比較し、異なれば Red、同じなら Black を返す IMultiValueConverter。
/// HostDetailView の TextBox.Foreground バインドで per-field Dirty ハイライトに使用する。
///
/// 使用例:
///   &lt;TextBox.Foreground&gt;
///     &lt;MultiBinding Converter="{StaticResource DirtyToForegroundConverter}"&gt;
///       &lt;Binding Path="Host.AdminID" /&gt;
///       &lt;Binding Path="OriginalHost.AdminID" /&gt;
///     &lt;/MultiBinding&gt;
///   &lt;/TextBox.Foreground&gt;
/// </summary>
public class DirtyToForegroundConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2
            || values[0] == DependencyProperty.UnsetValue
            || values[1] == DependencyProperty.UnsetValue)
            return Brushes.Black;

        var isDirty = !string.Equals(
            values[0]?.ToString(),
            values[1]?.ToString(),
            StringComparison.Ordinal);

        return isDirty ? Brushes.Red : Brushes.Black;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
