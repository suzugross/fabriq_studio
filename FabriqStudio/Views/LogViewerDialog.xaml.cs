using System.Windows;

namespace FabriqStudio.Views;

public partial class LogViewerDialog : Window
{
    private LogViewerDialog(string title, string logText)
    {
        InitializeComponent();
        Title = title;
        LogTextBox.Text = logText;
    }

    private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LogTextBox.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// ログビューアダイアログを表示するファクトリメソッド。
    /// </summary>
    public static void ShowLog(string title, string logText, Window? owner = null)
    {
        var dialog = new LogViewerDialog(title, logText)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }
}
