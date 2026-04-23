using System.IO;
using System.Windows;

namespace FabriqStudio.Views;

/// <summary>
/// fabriq バックアップダイアログ。
/// 親フォルダ選択 + メモ入力のシンプル構成。
/// </summary>
public partial class FabriqBackupDialog : Window
{
    public string ParentFolder { get; private set; } = "";
    public string Memo         { get; private set; } = "";

    private FabriqBackupDialog(string sourceRoot)
    {
        InitializeComponent();
        SummaryText.Text =
            $"コピー元: {sourceRoot}\n" +
            "PS1 / バージョン管理ファイル / 各種ドキュメントを除外しつつ、" +
            "fabriq のディレクトリ構造を再現してコピーします。";
    }

    /// <summary>
    /// ダイアログを表示するファクトリメソッド。
    /// </summary>
    public static FabriqBackupDialog? Show(string sourceRoot, Window? owner = null)
    {
        var dialog = new FabriqBackupDialog(sourceRoot)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog : null;
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "バックアップ先の親フォルダを選択",
        };
        if (picker.ShowDialog(this) != true) return;

        FolderPathBox.Text = picker.FolderName;
        OkBtn.IsEnabled    = Directory.Exists(FolderPathBox.Text);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        ParentFolder = FolderPathBox.Text.Trim();
        Memo         = MemoBox.Text ?? "";
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
