using System.IO;
using System.Windows;

namespace FabriqStudio.Views;

/// <summary>
/// 端末一覧エクスポート用ダイアログ。
/// <list type="bullet">
///   <item>親フォルダ選択（Microsoft.Win32.OpenFolderDialog）</item>
///   <item>メモ入力（任意）</item>
///   <item>復号オプション（パスフレーズ未設定時は自動でグレーアウト）</item>
/// </list>
/// </summary>
public partial class HostListExportDialog : Window
{
    /// <summary>選択された親フォルダ。ShowDialog() == true の場合のみ有効。</summary>
    public string ParentFolder { get; private set; } = "";
    public string Memo         { get; private set; } = "";
    public bool   Decrypt      { get; private set; }

    private readonly bool _hasPassphrase;

    private HostListExportDialog(int hostCount, int encryptedCellCount, bool hasPassphrase)
    {
        InitializeComponent();
        _hasPassphrase = hasPassphrase;

        SummaryText.Text = $"対象: {hostCount} 件 / 暗号化セル: {encryptedCellCount} 件";

        if (hasPassphrase)
        {
            DecryptCheckBox.IsChecked = encryptedCellCount > 0;
            DecryptCheckBox.IsEnabled = true;
            DecryptHintText.Text = encryptedCellCount > 0
                ? "復号に成功したセルは平文で CSV に書き出されます。"
                : "暗号化セルは検出されていません。";
        }
        else
        {
            DecryptCheckBox.IsChecked = false;
            DecryptCheckBox.IsEnabled = false;
            DecryptHintText.Text =
                "パスフレーズが未設定のため復号できません。ENC: 値はそのまま出力されます。";
        }
    }

    /// <summary>
    /// ダイアログを表示するファクトリメソッド。
    /// </summary>
    /// <param name="hostCount">エクスポート対象の端末数（サマリー表示用）</param>
    /// <param name="encryptedCellCount">現在の暗号化セル数（サマリー表示・復号チェックのデフォルト判定用）</param>
    /// <param name="hasPassphrase">パスフレーズ設定済みか</param>
    /// <param name="owner">オーナーウィンドウ</param>
    /// <returns>OK 時はダイアログインスタンス、キャンセル時は null。</returns>
    public static HostListExportDialog? Show(
        int hostCount, int encryptedCellCount, bool hasPassphrase, Window? owner = null)
    {
        var dialog = new HostListExportDialog(hostCount, encryptedCellCount, hasPassphrase)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog : null;
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "エクスポート先の親フォルダを選択",
        };
        if (picker.ShowDialog(this) != true) return;

        FolderPathBox.Text = picker.FolderName;
        OkBtn.IsEnabled    = Directory.Exists(FolderPathBox.Text);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        ParentFolder = FolderPathBox.Text.Trim();
        Memo         = MemoBox.Text ?? "";
        Decrypt      = _hasPassphrase && DecryptCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
