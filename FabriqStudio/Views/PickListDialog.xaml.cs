using System.Windows;
using FabriqStudio.Models;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

/// <summary>一覧から選ぶダイアログ（ストアアプリ / winget）。ロジックは PickListDialogViewModel。</summary>
public partial class PickListDialog : Window
{
    private readonly PickListDialogViewModel _vm;

    private PickListDialog(PickListRequest request)
    {
        InitializeComponent();
        _vm = new PickListDialogViewModel(request);
        DataContext = _vm;
        Loaded += (_, _) => QueryBox.Focus();
    }

    /// <summary>選んだ項目を返す。キャンセルなら null。</summary>
    public static Task<IReadOnlyList<PickListItem>?> ShowAsync(Window? owner, PickListRequest request)
    {
        var dlg = new PickListDialog(request) { Owner = owner };
        var ok  = dlg.ShowDialog() == true;
        return Task.FromResult(ok ? dlg._vm.Selected : null);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
