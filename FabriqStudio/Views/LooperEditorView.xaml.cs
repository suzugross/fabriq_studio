using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FabriqStudio.Models;

namespace FabriqStudio.Views;

public partial class LooperEditorView : UserControl
{
    public LooperEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ScriptPath 列のセルをダブルクリックしたとき、ファイルダイアログを表示する。
    /// </summary>
    private void DataGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null) return;

        var row = FindVisualParent<DataGridRow>(cell);
        if (row?.DataContext is not LooperEntry entry) return;

        // ScriptPath 列か確認
        if (cell.Column?.Header?.ToString() != "ScriptPath") return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "スクリプトファイルを選択",
            Filter = "PowerShell スクリプト (*.ps1)|*.ps1|すべてのファイル (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            entry.ScriptPath = dialog.FileName;
            e.Handled = true;
        }
    }

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
