using System.Windows;

namespace FabriqStudio.Views;

/// <summary>
/// MessageBox で表示しきれない長文を ScrollViewer 付きで表示するためのシンプルダイアログ。
/// Dry-run / Apply 結果レポートなどに使用。
/// </summary>
public partial class ReportDialog : Window
{
    private ReportDialog(string title, string body)
    {
        InitializeComponent();
        Title        = title;
        BodyBox.Text = body;
    }

    public static void Show(string title, string body, Window? owner = null)
    {
        var dialog = new ReportDialog(title, body)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(BodyBox.Text); }
        catch { /* クリップボードアクセス失敗は無視 */ }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
        => Close();
}
