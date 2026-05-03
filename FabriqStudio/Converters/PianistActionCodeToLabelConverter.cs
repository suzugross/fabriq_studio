using System.Globalization;
using System.Windows.Data;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Converters;

/// <summary>
/// PianistStep.Action（英名 Code）を <see cref="PianistProfileEditorViewModel.ActionOptions"/> から
/// 引いて日本語 Label に変換する。マッチしない場合は raw Code を返す（未知 Action の可視化）。
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public class PianistActionCodeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var code = value as string ?? "";
        var match = PianistProfileEditorViewModel.ActionOptions
            .FirstOrDefault(o => string.Equals(o.Code, code, StringComparison.Ordinal));
        return match?.Label ?? code;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
