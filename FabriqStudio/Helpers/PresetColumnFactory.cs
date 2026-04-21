using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FabriqStudio.Helpers;

/// <summary>
/// preset.csv 対応列を <see cref="DataGridTemplateColumn"/> として生成するファクトリ。
/// <list type="bullet">
///   <item>非編集時: TextBlock で生値を表示（ENC: 値も含めそのまま）</item>
///   <item>編集時: IsEditable=True の ComboBox — プリセット選択・自由入力とも
///         <c>ComboBox.Text</c> 経由の単一コミット経路で処理する</item>
/// </list>
/// <para>
/// SelectedItem / SelectedValue は意図的にバインドしない（Text と競合して値が欠損する既知の
/// WPF 落とし穴を回避する）。
/// </para>
/// </summary>
public static class PresetColumnFactory
{
    public static DataGridTemplateColumn Build(string columnName, IReadOnlyList<string> presets)
    {
        // CellTemplate（非編集時）
        var cellFactory = new FrameworkElementFactory(typeof(TextBlock));
        cellFactory.SetBinding(TextBlock.TextProperty, new Binding(columnName));
        cellFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0));
        cellFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        var cellTemplate = new DataTemplate { VisualTree = cellFactory };
        cellTemplate.Seal();

        // CellEditingTemplate（編集時）
        var comboFactory = new FrameworkElementFactory(typeof(ComboBox));
        comboFactory.SetValue(ComboBox.IsEditableProperty, true);
        comboFactory.SetValue(ComboBox.ItemsSourceProperty, presets);
        comboFactory.SetBinding(ComboBox.TextProperty, new Binding(columnName)
        {
            Mode                = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
        });

        var editTemplate = new DataTemplate { VisualTree = comboFactory };
        editTemplate.Seal();

        return new DataGridTemplateColumn
        {
            Header              = columnName,
            CellTemplate        = cellTemplate,
            CellEditingTemplate = editTemplate,
            SortMemberPath      = columnName,
        };
    }
}
