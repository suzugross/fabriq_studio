using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FabriqStudio.Models;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class BasicParamsView : UserControl
{
    public BasicParamsView()
    {
        InitializeComponent();
    }

    // ── ログ出力先 暗号化・復号 ContextMenu ハンドラ ──────────

    private void EncryptLogDestCell_Click(object sender, RoutedEventArgs e)
        => LogDestCellCryptoAction(isEncrypt: true);

    private void DecryptLogDestCell_Click(object sender, RoutedEventArgs e)
        => LogDestCellCryptoAction(isEncrypt: false);

    private void LogDestCellCryptoAction(bool isEncrypt)
    {
        if (DataContext is not BasicParamsViewModel vm) return;

        var cellInfo = LogDestDataGrid.CurrentCell;
        if (cellInfo.Column is null) return;
        if (cellInfo.Item is not LogDestination item) return;

        // Binding パスからプロパティ名を取得
        var propertyName = (cellInfo.Column as DataGridBoundColumn)?.Binding is Binding binding
            ? binding.Path.Path
            : null;
        if (string.IsNullOrEmpty(propertyName)) return;

        var error = isEncrypt
            ? vm.EncryptLogDestField(item, propertyName)
            : vm.DecryptLogDestField(item, propertyName);

        if (error is not null)
            MessageBox.Show(error, isEncrypt ? "暗号化" : "復号",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
