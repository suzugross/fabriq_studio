using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FabriqStudio.Models;

namespace FabriqStudio.Views;

public partial class CommandSelectorDialog : Window
{
    private const string AllCategories = "すべて";
    private readonly ICollectionView _view;

    /// <summary>ユーザーが選択したコマンド。キャンセル時は null。</summary>
    public GyotaqCommand? SelectedCommand { get; private set; }

    private CommandSelectorDialog(IEnumerable<GyotaqCommand> library)
    {
        InitializeComponent();

        // フィルタリング可能なビューを構築（GetDefaultView で WPF 管理ビューを取得）
        var items = library.ToList();
        _view = CollectionViewSource.GetDefaultView(items);
        _view.Filter = FilterPredicate;
        CommandGrid.ItemsSource = _view;

        // カテゴリ ComboBox を構築（「すべて」 + 重複除外したカテゴリ一覧）
        var categories = items
            .Select(c => c.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        categories.Insert(0, AllCategories);
        CategoryCombo.ItemsSource   = categories;
        CategoryCombo.SelectedIndex = 0;

        UpdateCountLabel();

        // 初期フォーカスを検索ボックスに
        Loaded += (_, _) => SearchBox.Focus();
    }

    // ── フィルタリング ──────────────────────────────────────────────────────

    private bool FilterPredicate(object obj)
    {
        if (obj is not GyotaqCommand cmd) return false;

        // カテゴリフィルタ
        var selectedCategory = CategoryCombo.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedCategory) && selectedCategory != AllCategories)
        {
            if (!string.Equals(cmd.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // テキスト検索（Name, OpenCommand, Category を部分一致）
        var search = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            return cmd.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || cmd.OpenCommand.Contains(search, StringComparison.OrdinalIgnoreCase)
                || cmd.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || cmd.DefaultTitle.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        _view.Refresh();
        UpdateCountLabel();
    }

    private void UpdateCountLabel()
    {
        var count = _view.Cast<object>().Count();
        CountLabel.Text = $"{count} 件";
    }

    // ── 選択操作 ────────────────────────────────────────────────────────────

    private void OnSelect_Click(object sender, RoutedEventArgs e)
    {
        AcceptSelection();
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AcceptSelection();
    }

    private void AcceptSelection()
    {
        if (CommandGrid.SelectedItem is GyotaqCommand cmd)
        {
            SelectedCommand = cmd;
            DialogResult    = true;
        }
    }

    private void OnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // ── ファクトリメソッド ──────────────────────────────────────────────────

    /// <summary>
    /// コマンド選択ダイアログを表示し、選択されたコマンドを返す。
    /// キャンセル時は null。
    /// </summary>
    public static GyotaqCommand? Show(IEnumerable<GyotaqCommand> library, Window? owner = null)
    {
        var dialog = new CommandSelectorDialog(library)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.SelectedCommand : null;
    }
}
