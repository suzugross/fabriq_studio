using System.Windows.Controls;
using System.Windows.Input;

namespace FabriqStudio.Views;

public partial class GpoBrowserControl : UserControl
{
    /// <summary>一覧の項目をダブルクリックした（ホストが「追加」などに使う）。</summary>
    public event EventHandler? ItemActivated;

    public GpoBrowserControl()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultList.SelectedItem is not null) ItemActivated?.Invoke(this, EventArgs.Empty);
    }
}
