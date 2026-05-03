using System.Windows;

namespace FabriqStudio.Views;

/// <summary>
/// Phase 削除確認ダイアログ。<paramref name="instructionsFileExists"/> = true のときのみ
/// 「instructions/.txt も削除する」チェックボックスを表示（既定 OFF、§7.2）。
/// </summary>
public partial class PianistPhaseDeleteDialog : Window
{
    public bool DeleteInstructionsFile { get; private set; }

    private PianistPhaseDeleteDialog(
        string phaseId, string phaseLabel, int stepCount, bool instructionsFileExists)
    {
        InitializeComponent();
        HeaderText.Text = $"Phase「{phaseId}」を削除しますか？";
        DetailText.Text =
            $"PhaseID: {phaseId}\n"
            + $"Label:   {phaseLabel}\n"
            + $"含まれる Step: {stepCount} 件\n\n"
            + "削除すると procedure.csv 上の上記 Step が全て取り除かれます。";

        DeleteFileLabel.Text = $"instructions/{phaseId}.txt も削除する";

        if (!instructionsFileExists)
        {
            // ファイルが無いときはチェックボックスを隠す
            DeleteFileCheck.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 戻り値: <c>ok=true</c> なら削除確定、<c>deleteFile</c> はチェックボックス状態。
    /// <c>ok=false</c> はキャンセル。
    /// </summary>
    public static (bool ok, bool deleteFile) Show(
        string phaseId, string phaseLabel, int stepCount,
        bool instructionsFileExists, Window? owner = null)
    {
        var dlg = new PianistPhaseDeleteDialog(phaseId, phaseLabel, stepCount, instructionsFileExists)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true ? (true, dlg.DeleteInstructionsFile) : (false, false);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        DeleteInstructionsFile = DeleteFileCheck.IsChecked == true;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
