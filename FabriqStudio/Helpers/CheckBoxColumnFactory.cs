using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FabriqStudio.Converters;

namespace FabriqStudio.Helpers;

/// <summary>
/// bool 意味を持つ文字列列（"0"/"1"）を <see cref="DataGridTemplateColumn"/>（CheckBox UI）として
/// 生成するファクトリ。
/// <list type="bullet">
///   <item>セル値は CSV 上の <c>DataRowView</c> に string で格納されるため、
///         <see cref="StringOneToBoolConverter"/> 経由で CheckBox.IsChecked に双方向バインドする</item>
///   <item>ロック中は <c>IsHitTestVisible=False</c> で操作を無効化
///         （プロファイル編集画面と同じ方式で視覚統一）</item>
///   <item>Cell/Editing のテンプレートを分けず、常に CheckBox を表示する
///         （DataGrid の編集モード遷移を介さないため BeginningEdit ハンドラとも干渉しない）</item>
/// </list>
/// </summary>
public static class CheckBoxColumnFactory
{
    public static DataGridTemplateColumn Build(string columnName)
    {
        var cbFactory = new FrameworkElementFactory(typeof(CheckBox));
        cbFactory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cbFactory.SetValue(CheckBox.VerticalAlignmentProperty,   VerticalAlignment.Center);

        // セル値 (string) ↔ IsChecked (bool)
        cbFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding(columnName)
        {
            Mode                = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            Converter           = new StringOneToBoolConverter(),
        });

        // 親 DataGrid の DataContext.IsLocked でロック時は操作不可にする
        cbFactory.SetBinding(UIElement.IsHitTestVisibleProperty, new Binding("DataContext.IsLocked")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
            Converter      = new InverseBooleanConverter(),
        });

        var template = new DataTemplate { VisualTree = cbFactory };
        template.Seal();

        return new DataGridTemplateColumn
        {
            Header         = columnName,
            CellTemplate   = template,
            SortMemberPath = columnName,
        };
    }
}
