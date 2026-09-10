using System.Windows;

namespace FabriqStudio.Helpers;

/// <summary>ファイル選択ダイアログ（複数選択）。.lnk はリンク先に解決せず、そのものを返す。</summary>
public static class FilePicker
{
    public const string LinkFilter = "ショートカット・実行ファイル|*.lnk;*.exe|すべてのファイル|*.*";

    /// <summary>スタートメニュー（全ユーザー）のプログラム フォルダ。ピン留めの候補はだいたいここにある。</summary>
    public static string StartMenuPrograms => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

    public static IReadOnlyList<string>? PickMany(string filter, string? initialDir, string title)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect      = true,
            DereferenceLinks = false,
            Filter           = filter,
            InitialDirectory = initialDir ?? "",
            Title            = title,
        };
        var owner = Application.Current?.MainWindow;
        var ok    = owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
        return ok == true ? dlg.FileNames : null;
    }
}
