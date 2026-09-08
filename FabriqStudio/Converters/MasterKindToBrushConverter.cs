using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FabriqStudio.Models.Master;

namespace FabriqStudio.Converters;

/// <summary>
/// マスタ設計画面のバッジ色。<see cref="MasterItemKinds"/> → Brush。
/// parameter に "fg" を渡すと文字色、省略時は背景色を返す。
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public class MasterKindToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, (string Bg, string Fg)> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        [MasterItemKinds.Module] = ("#E6F4EC", "#1E7F4F"),
        [MasterItemKinds.Dict]   = ("#E4EDFA", "#2B63C6"),
        [MasterItemKinds.Manual] = ("#ECEEF2", "#6B7280"),
        [MasterItemKinds.Fabriq] = ("#FBE9E1", "#B5451B"),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = value?.ToString() ?? MasterItemKinds.Module;
        if (!Palette.TryGetValue(kind, out var colors))
            colors = Palette[MasterItemKinds.Module];

        var hex = string.Equals(parameter?.ToString(), "fg", StringComparison.OrdinalIgnoreCase) ? colors.Fg : colors.Bg;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
