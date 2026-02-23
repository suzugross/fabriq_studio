using System.Windows;
using FabriqStudio.Models;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class RegistryPickerWindow : Window
{
    private readonly RegistryPickerViewModel _vm;

    /// <summary>選択されたエントリ。ShowDialog() == true の場合のみ有効。</summary>
    public RegistryTemplateEntry? SelectedEntry { get; private set; }

    private RegistryPickerWindow(
        IReadOnlyList<RegistryTemplateEntry> entries,
        string? targetHive)
    {
        InitializeComponent();
        _vm = new RegistryPickerViewModel(entries, targetHive);
        DataContext = _vm;
    }

    /// <summary>
    /// レジストリ辞書ピッカーを表示し、選択されたエントリを返すファクトリメソッド。
    /// </summary>
    /// <param name="entries">辞書エントリ一覧</param>
    /// <param name="owner">オーナーウィンドウ（省略時は MainWindow）</param>
    /// <param name="targetHive">Hive を固定する場合は "HKLM" or "HKCU"。null ならフリー選択。</param>
    /// <returns>選択されたエントリ。キャンセル時は null。</returns>
    public static RegistryTemplateEntry? Show(
        IReadOnlyList<RegistryTemplateEntry> entries,
        Window? owner = null,
        string? targetHive = null)
    {
        var dialog = new RegistryPickerWindow(entries, targetHive)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.SelectedEntry : null;
    }

    private void SelectBtn_Click(object sender, RoutedEventArgs e)
    {
        SelectedEntry = _vm.SelectedEntry;
        DialogResult  = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
