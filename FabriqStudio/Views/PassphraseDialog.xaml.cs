using System.Windows;

namespace FabriqStudio.Views;

/// <summary>パスフレーズ入力ダイアログの用途。文言とボタン名だけが変わる。</summary>
public enum PassphraseDialogMode
{
    /// <summary>左ペイン下部「🔑 パスフレーズ」からの手動操作。空入力でセッションのパスフレーズを解除できる。</summary>
    Manual,

    /// <summary>起動時 / ワークスペースを開いた直後: 設定済みワークスペースの照合。</summary>
    WorkspaceVerify,

    /// <summary>起動時 / ワークスペースを開いた直後: 未設定ワークスペースへの初回設定。</summary>
    WorkspaceSetup,
}

public partial class PassphraseDialog : Window
{
    /// <summary>ユーザーが入力したパスフレーズ。空文字列 = クリア（Manual）／スキップ（起動時）。</summary>
    public string Passphrase { get; private set; } = "";

    public PassphraseDialog(PassphraseDialogMode mode, bool isCurrentlySet)
    {
        InitializeComponent();

        switch (mode)
        {
            case PassphraseDialogMode.WorkspaceVerify:
                Title           = "パスフレーズの確認";
                StatusText.Text = "このワークスペースにはパスフレーズが設定されています。\n"
                                + "ENC: 値の暗号化・復号を行うには、有効なパスフレーズを入力してください。";
                HintText.Text   = "キャンセルしても編集は続行できます（ENC: 値の暗号化・復号のみ行えません）。";
                OkBtn.Content   = "確認";
                break;

            case PassphraseDialogMode.WorkspaceSetup:
                Title           = "パスフレーズの設定";
                StatusText.Text = "このワークスペースにはパスフレーズが設定されていません。\n"
                                + "CSV の秘密情報を ENC: 暗号化するには、マスターパスフレーズを設定してください。";
                HintText.Text   = "パスフレーズは保存されません。忘れると暗号化した値は復号できなくなるため、必ず記録してください。\n"
                                + "キャンセルしても編集は続行できます。";
                OkBtn.Content   = "設定";
                break;

            default:
                StatusText.Text = isCurrentlySet
                    ? "パスフレーズは設定済みです。\n変更する場合は新しいパスフレーズを入力してください。\nクリアするには空のまま「設定」を押してください。"
                    : "CSV の暗号化値 (ENC:xxx) を扱うには、マスターパスフレーズを設定してください。";
                break;
        }

        Loaded += (_, _) => PassphraseBox.Focus();
    }

    /// <summary>
    /// ダイアログを開くファクトリメソッド。
    /// </summary>
    /// <param name="mode">用途（文言の切り替え）</param>
    /// <param name="isCurrentlySet">現在パスフレーズが設定済みか（<see cref="PassphraseDialogMode.Manual"/> のみ参照）</param>
    /// <param name="owner">オーナーウィンドウ（省略時は MainWindow）</param>
    /// <returns>
    /// OK: 入力されたパスフレーズ（空文字列 = クリア／スキップ）。
    /// Cancel: null。
    /// </returns>
    public static string? Show(
        PassphraseDialogMode mode          = PassphraseDialogMode.Manual,
        bool                 isCurrentlySet = false,
        Window?              owner          = null)
    {
        var dialog = new PassphraseDialog(mode, isCurrentlySet)
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
