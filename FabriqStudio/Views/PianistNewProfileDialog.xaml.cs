using System.Windows;
using System.Windows.Controls;

namespace FabriqStudio.Views;

/// <summary>
/// Pianist Profile 新規作成用ダイアログ。
/// プロファイル名（フォルダ名）を入力 → サービス側のバリデータで即時検証。
/// </summary>
public partial class PianistNewProfileDialog : Window
{
    public string ProfileName { get; private set; } = "";

    private readonly Func<string, string?> _validator;

    private PianistNewProfileDialog(Func<string, string?> validator)
    {
        InitializeComponent();
        // ⚠ readonly フィールドを TextBox.Text 操作より前に確定
        _validator = validator;

        Loaded += (_, _) =>
        {
            ProfileNameBox.Focus();
            Validate();
        };
    }

    public static string? Show(Func<string, string?> validator, Window? owner = null)
    {
        var dlg = new PianistNewProfileDialog(validator)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true ? dlg.ProfileName : null;
    }

    private void Validate()
    {
        var name  = ProfileNameBox.Text?.Trim() ?? "";
        var error = _validator(name);

        ErrorText.Text       = error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        OkBtn.IsEnabled      = string.IsNullOrEmpty(error);
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e) => Validate();

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        ProfileName  = ProfileNameBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
