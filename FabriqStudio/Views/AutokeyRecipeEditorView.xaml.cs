using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FabriqStudio.Models;

namespace FabriqStudio.Views;

public partial class AutokeyRecipeEditorView : UserControl
{
    public AutokeyRecipeEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Value 列のセルをダブルクリックしたとき、Action が "Open" ならファイルダイアログを表示する。
    /// </summary>
    private void DataGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // クリック元から DataGridCell を探す
        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null) return;

        // 行の DataContext から RecipeRow を取得
        var row = FindVisualParent<DataGridRow>(cell);
        if (row?.DataContext is not RecipeRow recipeRow) return;

        // Action が Open でないなら通常のセル編集に任せる
        if (recipeRow.Action != ActionType.Open) return;

        // Value 列（Header == "Value / パラメータ"）か確認
        if (cell.Column?.Header?.ToString()?.StartsWith("Value") != true) return;

        // ファイルダイアログを表示
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "ファイル / アプリを選択",
            Filter = "すべてのファイル (*.*)|*.*|実行ファイル (*.exe)|*.exe|" +
                     "ショートカット (*.lnk)|*.lnk"
        };

        if (dialog.ShowDialog() == true)
        {
            recipeRow.Value = dialog.FileName;
            e.Handled = true;  // セル編集モードへの移行を抑制
        }
    }

    // ── ビジュアルツリー探索ヘルパー ──────────────────────────────────────

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T target) return target;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
