using System.Windows.Controls;
using System.Windows.Input;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class ProfileDetailView : UserControl
{
    public ProfileDetailView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 左ペイン「利用可能モジュール」の ListBoxItem ダブルクリックで AddModuleCommand を発火。
    /// ロック中・未選択は ViewModel 側 CanAddModule が無効化するため追加の判定不要。
    /// </summary>
    private void ModuleItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProfileDetailViewModel vm
            && vm.AddModuleCommand.CanExecute(null))
        {
            vm.AddModuleCommand.Execute(null);
            e.Handled = true;
        }
    }
}
