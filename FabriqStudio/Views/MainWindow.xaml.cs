using System.ComponentModel;
using System.Windows;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    // DI によって MainViewModel が注入される（ビジネスロジックは一切記述しない）
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel  = viewModel;
    }

    /// <summary>
    /// 終了直前に現在ページの未保存変更を確認し、ユーザーがキャンセルした場合は終了を中断する。
    /// 判断ロジックは MainViewModel.ConfirmDiscardIfDirty に集約し、View は呼び出すだけ。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.ConfirmDiscardIfDirty())
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
