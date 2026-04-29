using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FabriqStudio.Converters;

/// <summary>
/// プロファイル行の ScriptPath 文字列を受け取り、
/// 特殊マーカーの種別に応じた背景 Brush を返す IValueConverter。
///
/// 色分け仕様:
///   __xxx__ 系の特殊マーカー → 淡い青 (#DAE8FC)
///   通常モジュール           → Transparent（DataGrid 既定の白背景）
///
/// 注意: このコンバーターを DataGridRow.RowStyle の Background Setter に使用する場合、
///       DataGrid の AlternatingRowBackground と競合するため、AlternatingRowBackground は
///       当該 DataGrid から削除すること。
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class SpecialMarkerToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush SystemCommandBrush =
        new(Color.FromRgb(0xDA, 0xE8, 0xFC));   // 淡い青

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string scriptPath)
            return Brushes.Transparent;

        // __ で始まり __ で終わる（__RESTART__, __AUTOPILOT__ 等）→ 共通の青色
        if (scriptPath.StartsWith("__", StringComparison.Ordinal) &&
            scriptPath.EndsWith("__",   StringComparison.Ordinal))
            return SystemCommandBrush;

        // 通常モジュール → 既定背景
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
