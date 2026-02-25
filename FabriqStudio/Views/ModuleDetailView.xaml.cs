using System.Data;
using System.Windows;
using System.Windows.Controls;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class ModuleDetailView : UserControl
{
    public ModuleDetailView()
    {
        InitializeComponent();
    }

    /// <summary>module.csv フィールドの TextChanged → ViewModel の Dirty フラグを立てる。</summary>
    private void OnModuleCsvChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ModuleDetailViewModel vm)
            vm.MarkModuleCsvDirty();
    }

    private void EncryptCell_Click(object sender, RoutedEventArgs e)
        => CryptoAction(isEncrypt: true);

    private void DecryptCell_Click(object sender, RoutedEventArgs e)
        => CryptoAction(isEncrypt: false);

    private void CryptoAction(bool isEncrypt)
    {
        if (DataContext is not ModuleDetailViewModel vm) return;

        var cellInfo = ConfigDataGrid.CurrentCell;
        if (cellInfo.Column is null) return;

        var columnName = cellInfo.Column.Header?.ToString();
        if (string.IsNullOrEmpty(columnName)) return;

        if (cellInfo.Item is not DataRowView row) return;

        var error = isEncrypt
            ? vm.EncryptCell(row, columnName)
            : vm.DecryptCell(row, columnName);

        if (error is not null)
            MessageBox.Show(error, isEncrypt ? "暗号化" : "復号",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
