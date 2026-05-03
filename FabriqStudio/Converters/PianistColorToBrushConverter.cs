using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FabriqStudio.Converters;

/// <summary>
/// Pianist の Phase Color 列（英名 9 色固定）を WPF Brush に変換する。
/// CSV には Blue / Green / ... の英名を保持し、UI ではこの converter で色見本を描画する。
/// 未定義値や空文字は <see cref="Brushes.Transparent"/> を返す（行スタイルが壊れないように）。
///
/// パレットは <c>pianist_studio_editor_prompt_draft.md §4.1</c> の 9 色固定。
/// 数値は視認性重視で WPF 既定の中明度に揃えた（pianist.ps1 v1.x には実行時の色定数が無く、
/// fabriq 本体側のスタイル規定もないため Studio 単独で決めて良い領域）。
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public class PianistColorToBrushConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, Brush> Palette =
        new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase)
        {
            ["Blue"]   = MakeBrush(0x1E, 0x90, 0xFF),
            ["Green"]  = MakeBrush(0x2C, 0xA0, 0x2C),
            ["Yellow"] = MakeBrush(0xDA, 0xA5, 0x20),
            ["Orange"] = MakeBrush(0xFF, 0x8C, 0x00),
            ["Red"]    = MakeBrush(0xD6, 0x27, 0x28),
            ["Purple"] = MakeBrush(0x94, 0x67, 0xBD),
            ["Cyan"]   = MakeBrush(0x17, 0xBE, 0xCF),
            ["Pink"]   = MakeBrush(0xE3, 0x77, 0xC2),
            ["Gray"]   = MakeBrush(0x7F, 0x7F, 0x7F),
        };

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string ?? "";
        return Palette.TryGetValue(name, out var brush) ? brush : Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
