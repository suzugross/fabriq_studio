using System.Windows;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class MainWindow : Window
{
    // DI によって MainViewModel が注入される（ビジネスロジックは一切記述しない）
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
