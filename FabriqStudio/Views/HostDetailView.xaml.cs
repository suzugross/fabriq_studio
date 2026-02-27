using System.Windows;
using System.Windows.Controls;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class HostDetailView : UserControl
{
    public HostDetailView()
    {
        InitializeComponent();
    }

    private void EncryptField_Click(object sender, RoutedEventArgs e)
        => CryptoAction(sender, isEncrypt: true);

    private void DecryptField_Click(object sender, RoutedEventArgs e)
        => CryptoAction(sender, isEncrypt: false);

    private void CryptoAction(object sender, bool isEncrypt)
    {
        if (DataContext is not HostDetailViewModel vm) return;

        if (vm.IsLocked)
        {
            MessageBox.Show("編集モードに切り替えてから操作してください。",
                "ロック中", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // ContextMenu の PlacementTarget から対象 TextBox を取得
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Parent is not ContextMenu contextMenu) return;
        if (contextMenu.PlacementTarget is not TextBox textBox) return;

        var propertyName = textBox.Tag?.ToString();
        if (string.IsNullOrEmpty(propertyName)) return;

        var error = isEncrypt
            ? vm.EncryptField(propertyName)
            : vm.DecryptField(propertyName);

        if (error is not null)
            MessageBox.Show(error, isEncrypt ? "暗号化" : "復号",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── 全フィールド一括暗号化・復号 ──────────────────────────────

    private void EncryptAllFields_Click(object sender, RoutedEventArgs e)
        => BatchCryptoAction(isEncrypt: true);

    private void DecryptAllFields_Click(object sender, RoutedEventArgs e)
        => BatchCryptoAction(isEncrypt: false);

    private void BatchCryptoAction(bool isEncrypt)
    {
        if (DataContext is not HostDetailViewModel vm) return;

        if (vm.IsLocked)
        {
            MessageBox.Show("編集モードに切り替えてから操作してください。",
                "ロック中", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var action = isEncrypt ? "一括暗号化" : "一括復号";
        var confirm = MessageBox.Show(
            $"全フィールドを{action}しますか？\n（AdminID 等の除外カラムはスキップされます）",
            action, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = isEncrypt
            ? vm.EncryptAllFields()
            : vm.DecryptAllFields();

        if (result is null) return;

        if (result.HasErrors)
            MessageBox.Show(result.ToSummary(isEncrypt), action,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(result.ToSummary(isEncrypt), action,
                MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
