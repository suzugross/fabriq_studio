using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FabriqStudio.Models;

namespace FabriqStudio.Converters;

/// <summary>
/// <see cref="PianistValidationIssue.Severity"/> をアイコン用 Brush へ変換する。
/// ConverterParameter で "icon" を指定するとアイコン文字を、それ以外は Brush を返す。
/// </summary>
[ValueConversion(typeof(PianistValidationIssue.Severity), typeof(object))]
public class PianistValidationSeverityConverter : IValueConverter
{
    private static readonly Brush ErrorBrush   = MakeBrush(0xC0, 0x40, 0x40);
    private static readonly Brush WarningBrush = MakeBrush(0xC0, 0x80, 0x20);
    private static readonly Brush InfoBrush    = MakeBrush(0x60, 0x80, 0xC0);

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PianistValidationIssue.Severity sev) return Brushes.Transparent;
        var asIcon = parameter as string == "icon";

        return (sev, asIcon) switch
        {
            (PianistValidationIssue.Severity.Error,   true) => "✖",
            (PianistValidationIssue.Severity.Warning, true) => "⚠",
            (PianistValidationIssue.Severity.Info,    true) => "ℹ",
            (PianistValidationIssue.Severity.Error,   false) => ErrorBrush,
            (PianistValidationIssue.Severity.Warning, false) => WarningBrush,
            (PianistValidationIssue.Severity.Info,    false) => InfoBrush,
            _ => (object)Brushes.Transparent,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
