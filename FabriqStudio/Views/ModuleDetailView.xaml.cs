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
}
