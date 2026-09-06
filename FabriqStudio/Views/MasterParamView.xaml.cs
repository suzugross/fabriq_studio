using System.Windows;
using System.Windows.Controls;
using FabriqStudio.Helpers;
using FabriqStudio.ViewModels.Master;

namespace FabriqStudio.Views;

public partial class MasterParamView : UserControl
{
    public MasterParamView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 表形式の質問の DataGrid 列を差し替える:
    /// テンプレートの列ラベルをヘッダーに使い、選択肢のある列は ComboBox（PresetColumnFactory）にする。
    /// </summary>
    private void OnTableAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (sender is not DataGrid grid || grid.DataContext is not TableItemViewModel vm) return;

        var name = e.PropertyName;
        if (string.IsNullOrEmpty(name)) return;

        var options = vm.OptionsFor(name);
        if (options is { Count: > 0 })
            e.Column = PresetColumnFactory.Build(name, options);

        e.Column.Header = vm.HeaderFor(name);
        if (e.Column is DataGridTextColumn text)
            text.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    /// <summary>章を切り替えたらフォームを先頭へ戻す（UI だけの責務）。</summary>
    private void OnSectionContentChanged(object sender, DependencyPropertyChangedEventArgs e)
        => FormScroller.ScrollToTop();

    /// <summary>GPO 一覧の行をダブルクリック → 編集（判断は VM の EditCommand.CanExecute に委ねる）。</summary>
    private void OnGpoListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListBox { DataContext: GpoItemViewModel vm } || vm.SelectedPolicy is null) return;
        if (vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
    }

    // ── 資材のドラッグ＆ドロップ（判断は VM の CanDrop / AcceptDropAsync に委ねる）──

    private static IAssetDropTarget? DropTargetOf(object sender)
        => (sender as FrameworkElement)?.DataContext as IAssetDropTarget;

    private void OnAssetDragOver(object sender, DragEventArgs e)
    {
        var target = DropTargetOf(sender);
        var ok = target is { CanDrop: true } && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnAssetDrop(object sender, DragEventArgs e)
    {
        var target = DropTargetOf(sender);
        if (target is not { CanDrop: true } || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        await target.AcceptDropAsync(paths);
    }
}
