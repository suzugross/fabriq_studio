using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FabriqStudio.Converters;

/// <summary>
/// プロファイル行の背景色を決定する MultiValueConverter。
///
/// 入力（バインド順）:
///   [0] ScriptPath (string)
///   [1] Group      (string)
///
/// 優先順位:
///   1. ScriptPath が __xxx__ 形式の特殊マーカー → 淡い青 (#DAE8FC)
///   2. Group が非空                              → Group ハッシュに応じたパステル 8 色から選択
///   3. それ以外                                  → Transparent
///
/// 注意: SpecialMarkerToBackgroundConverter の上位互換だが、
///       後者は将来の他箇所参照に備えて残置している。
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class GroupRowBackgroundConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush SystemCommandBrush =
        new(Color.FromRgb(0xDA, 0xE8, 0xFC));

    /// <summary>
    /// Group 値ごとの背景色パレット。特殊マーカー青 (#DAE8FC) と区別しやすい
    /// 控えめなパステル 8 色。色数を超える Group はハッシュ衝突で同色になる
    /// （識別性より一貫性を優先）。
    /// </summary>
    private static readonly SolidColorBrush[] GroupPalette =
    [
        new(Color.FromRgb(0xFF, 0xF4, 0xE0)),  // クリーム
        new(Color.FromRgb(0xFF, 0xE6, 0xE6)),  // ペールローズ
        new(Color.FromRgb(0xE6, 0xFF, 0xE6)),  // ペールミント
        new(Color.FromRgb(0xFF, 0xE6, 0xF2)),  // ペールピンク
        new(Color.FromRgb(0xF0, 0xE6, 0xFF)),  // ラベンダー
        new(Color.FromRgb(0xFF, 0xFA, 0xE6)),  // ペールイエロー
        new(Color.FromRgb(0xE6, 0xFF, 0xF7)),  // アクア
        new(Color.FromRgb(0xF5, 0xE6, 0xFF)),  // モーブ
    ];

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var scriptPath = values.Length > 0 ? values[0] as string ?? "" : "";
        var group      = values.Length > 1 ? values[1] as string ?? "" : "";

        // 1. 特殊マーカー優先
        if (scriptPath.StartsWith("__", StringComparison.Ordinal) &&
            scriptPath.EndsWith("__",   StringComparison.Ordinal))
            return SystemCommandBrush;

        // 2. Group 非空ならハッシュで決定論的に色選択
        if (!string.IsNullOrEmpty(group))
        {
            // GetHashCode は実行ごとに異なる場合があるため Ordinal で安定化。
            // 負値対策として絶対値化してからモジュロ。
            var hash = StringComparer.Ordinal.GetHashCode(group);
            var idx  = (int)((uint)hash % (uint)GroupPalette.Length);
            return GroupPalette[idx];
        }

        // 3. デフォルト
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
