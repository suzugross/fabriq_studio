using System.IO;
using System.Windows;
using FabriqStudio.Services;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

/// <summary>
/// 「Update fabriq from template」ダイアログ。
/// ViewModel は <see cref="FabriqUpdateDialogViewModel"/> に集約し、
/// code-behind はフォルダ/ファイルピッカーと CheckBox 変更時の preflight 再評価のみ担当する。
/// </summary>
public partial class FabriqUpdateDialog : Window
{
    public FabriqUpdateDialogViewModel VM { get; }

    private FabriqUpdateDialog(IFabriqUpdateService service, string? initialTarget)
    {
        InitializeComponent();
        VM          = new FabriqUpdateDialogViewModel(service, initialTarget);
        DataContext = VM;
    }

    public static void Show(IFabriqUpdateService service, string? initialTarget, Window? owner = null)
    {
        var dialog = new FabriqUpdateDialog(service, initialTarget)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    private void BrowseTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Template fabriq のルートフォルダを選択",
        };
        if (picker.ShowDialog(this) == true)
            VM.TemplatePath = picker.FolderName;
    }

    private void BrowseTargetBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Target fabriq のルートフォルダを選択",
        };
        if (picker.ShowDialog(this) == true)
            VM.TargetPath = picker.FolderName;
    }

    private void BrowseBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title    = "バックアップ zip の保存先",
            Filter   = "ZIP (*.zip)|*.zip",
            FileName = Path.GetFileName(VM.BackupPath),
        };
        var initialDir = Path.GetDirectoryName(VM.BackupPath);
        if (!string.IsNullOrEmpty(initialDir)) picker.InitialDirectory = initialDir;

        if (picker.ShowDialog(this) == true)
            VM.BackupPath = picker.FileName;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Plan DataGrid 内の CheckBox 変更時に preflight を再評価する。
    /// Command で代替できないのは、ObservableCollection 個別 item の PropertyChanged を
    /// VM に集約するルートが無いため（シンプルに event handler で中継する）。
    /// </summary>
    private void ItemSelection_Changed(object sender, RoutedEventArgs e)
    {
        VM.OnSelectionChanged();
    }
}
