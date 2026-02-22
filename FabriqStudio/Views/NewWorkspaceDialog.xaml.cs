using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FabriqStudio.Views;

public partial class NewWorkspaceDialog : Window
{
    /// <summary>ユーザーが入力したフォルダ名。ShowDialog() == true の場合のみ有効。</summary>
    public string FolderName { get; private set; } = "";

    public NewWorkspaceDialog(string parentPath)
    {
        InitializeComponent();
        ParentPathText.Text = parentPath;
        Loaded += (_, _) =>
        {
            FolderNameBox.Focus();
            FolderNameBox.SelectAll();
        };
    }

    /// <summary>
    /// フォルダ選択ダイアログと組み合わせて呼び出すファクトリメソッド。
    /// </summary>
    /// <param name="parentPath">選択された作成先フォルダのパス（表示用）</param>
    /// <param name="owner">オーナーウィンドウ（省略時は MainWindow）</param>
    /// <returns>入力されたフォルダ名。キャンセル時は null。</returns>
    public static string? Show(string parentPath, Window? owner = null)
    {
        var dialog = new NewWorkspaceDialog(parentPath)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void FolderNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = FolderNameBox.Text.Trim();
        OkBtn.IsEnabled = text.Length > 0
            && text.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        FolderName  = FolderNameBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
