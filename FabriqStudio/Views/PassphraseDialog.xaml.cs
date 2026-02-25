using System.Windows;
using System.Windows.Controls;

namespace FabriqStudio.Views;

public partial class PassphraseDialog : Window
{
    /// <summary>ユーザーが入力したパスフレーズ。空文字列 = クリア。</summary>
    public string Passphrase { get; private set; } = "";

    public PassphraseDialog(bool isCurrentlySet)
    {
        InitializeComponent();

        StatusText.Text = isCurrentlySet
            ? "パスフレーズは設定済みです。\n変更する場合は新しいパスフレーズを入力してください。\nクリアするには空のまま「設定」を押してください。"
            : "CSV の暗号化値 (ENC:xxx) を扱うには、マスターパスフレーズを設定してください。";

        Loaded += (_, _) => PassphraseBox.Focus();
    }

    /// <summary>
    /// ダイアログを開くファクトリメソッド。
    /// </summary>
    /// <param name="isCurrentlySet">現在パスフレーズが設定済みか</param>
    /// <param name="owner">オーナーウィンドウ（省略時は MainWindow）</param>
    /// <returns>
    /// OK: 入力されたパスフレーズ（空文字列 = クリア）。
    /// Cancel: null。
    /// </returns>
    public static string? Show(bool isCurrentlySet, Window? owner = null)
    {
        var dialog = new PassphraseDialog(isCurrentlySet)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }

    private void PassphraseBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // 必要に応じてバリデーション追加可能
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        Passphrase   = PassphraseBox.Password;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
