using System.Windows;
using FabriqStudio.Services.Master;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

/// <summary>既定のアプリの関連付け XML を編集するモーダル ダイアログ。保存したら true。</summary>
public partial class AppAssocEditorDialog : Window
{
    private readonly AppAssocEditorViewModel _vm;

    private AppAssocEditorDialog(IAppAssocService service, string targetPath, string? targetRelLabel)
    {
        InitializeComponent();
        _vm = new AppAssocEditorViewModel(service, targetPath, targetRelLabel);
        DataContext = _vm;
        _vm.RequestClose += ok =>
        {
            DialogResult = ok;
            Close();
        };
        Closing += (_, e) =>
        {
            if (DialogResult == true || !_vm.IsDirty) return;
            var r = MessageBox.Show(this, "変更を保存せずに閉じますか？", "既定のアプリの関連付け",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) e.Cancel = true;
        };
        Loaded += (_, _) => _vm.LoadInitial();
    }

    public static bool Show(Window? owner, IAppAssocService service, string targetPath, string? targetRelLabel = null)
    {
        var dialog = new AppAssocEditorDialog(service, targetPath, targetRelLabel);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true && dialog._vm.Saved;
    }
}
