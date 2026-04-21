using System.Data;
using System.Windows;
using System.Windows.Controls;
using FabriqStudio.Helpers;
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

    // ── プリセット対応 / チェックボックス化: AutoGeneratingColumn で列をスワップ ───

    /// <summary>
    /// DataGrid の列自動生成時に列種別を差し替える。優先順位は以下：
    /// <list type="number">
    ///   <item><c>Enabled</c> 列: 常に CheckBox UI（preset.csv の設定より優先）</item>
    ///   <item>preset.csv で候補が定義されている列: ComboBox UI</item>
    ///   <item>それ以外: WPF 既定の <see cref="DataGridTextColumn"/>（自由記述）</item>
    /// </list>
    /// </summary>
    private void OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is not ModuleDetailViewModel vm) return;

        var columnName = e.PropertyName;
        if (string.IsNullOrEmpty(columnName)) return;

        // ① Enabled 列: チェックボックス化（preset より常に優先）
        if (string.Equals(columnName, "Enabled", StringComparison.OrdinalIgnoreCase))
        {
            e.Column = CheckBoxColumnFactory.Build(columnName);
            return;
        }

        // ② preset.csv の候補列: ComboBox
        if (!vm.ColumnPresets.TryGetValue(columnName, out var presets)) return;
        if (presets.Count == 0) return;

        e.Column = PresetColumnFactory.Build(columnName, presets);
    }

    // ── プリセット対応: 暗号化セル(ENC:)の誤編集ガード ─────────────────────

    /// <summary>
    /// プリセット対象列で <c>ENC:</c> 値を編集しようとした際、警告を表示して編集をキャンセルする。
    /// 復号は右クリック → 「🔓 セルを復号」から行う。
    /// </summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (DataContext is not ModuleDetailViewModel vm) return;

        var columnName = e.Column?.Header?.ToString();
        if (string.IsNullOrEmpty(columnName)) return;
        if (!vm.ColumnPresets.ContainsKey(columnName)) return;

        if (e.Row.Item is not DataRowView row) return;
        var value = row[columnName]?.ToString() ?? "";
        if (!value.StartsWith("ENC:", StringComparison.Ordinal)) return;

        MessageBox.Show(
            "このセルは暗号化されています。\n" +
            "先に右クリック →「🔓 セルを復号」で復号してから編集してください。",
            "編集不可",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Cancel = true;
    }

    // ── セル単位暗号化・復号 ──────────────────────────────────────

    private void EncryptCell_Click(object sender, RoutedEventArgs e)
        => CellCryptoAction(isEncrypt: true);

    private void DecryptCell_Click(object sender, RoutedEventArgs e)
        => CellCryptoAction(isEncrypt: false);

    private void CellCryptoAction(bool isEncrypt)
    {
        if (DataContext is not ModuleDetailViewModel vm) return;

        var cellInfo = ConfigDataGrid.CurrentCell;
        if (cellInfo.Column is null) return;

        var columnName = cellInfo.Column.Header?.ToString();
        if (string.IsNullOrEmpty(columnName)) return;

        if (cellInfo.Item is not DataRowView row) return;

        var error = isEncrypt
            ? vm.EncryptCell(row, columnName)
            : vm.DecryptCell(row, columnName);

        if (error is not null)
            MessageBox.Show(error, isEncrypt ? "暗号化" : "復号",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── 列一括暗号化・復号 ────────────────────────────────────────

    private void EncryptColumn_Click(object sender, RoutedEventArgs e)
        => ColumnCryptoAction(isEncrypt: true);

    private void DecryptColumn_Click(object sender, RoutedEventArgs e)
        => ColumnCryptoAction(isEncrypt: false);

    private void ColumnCryptoAction(bool isEncrypt)
    {
        if (DataContext is not ModuleDetailViewModel vm) return;

        var cellInfo = ConfigDataGrid.CurrentCell;
        if (cellInfo.Column is null) return;

        var columnName = cellInfo.Column.Header?.ToString();
        if (string.IsNullOrEmpty(columnName)) return;

        var action = isEncrypt ? "列を一括暗号化" : "列を一括復号";
        var confirm = MessageBox.Show(
            $"列 '{columnName}' の全行を{(isEncrypt ? "暗号化" : "復号")}しますか？",
            action, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = isEncrypt
            ? vm.EncryptColumn(columnName)
            : vm.DecryptColumn(columnName);

        ShowBatchResult(result, isEncrypt, action);
    }

    // ── 行一括暗号化・復号 ────────────────────────────────────────

    private void EncryptRow_Click(object sender, RoutedEventArgs e)
        => RowCryptoAction(isEncrypt: true);

    private void DecryptRow_Click(object sender, RoutedEventArgs e)
        => RowCryptoAction(isEncrypt: false);

    private void RowCryptoAction(bool isEncrypt)
    {
        if (DataContext is not ModuleDetailViewModel vm) return;

        var cellInfo = ConfigDataGrid.CurrentCell;
        if (cellInfo.Item is not DataRowView row) return;

        var action = isEncrypt ? "行を一括暗号化" : "行を一括復号";
        var confirm = MessageBox.Show(
            $"選択行の全暗号化可能列を{(isEncrypt ? "暗号化" : "復号")}しますか？\n（除外カラムはスキップされます）",
            action, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = isEncrypt
            ? vm.EncryptRow(row)
            : vm.DecryptRow(row);

        ShowBatchResult(result, isEncrypt, action);
    }

    // ── テーブル全体暗号化・復号 ──────────────────────────────────

    private void EncryptAll_Click(object sender, RoutedEventArgs e)
        => TableCryptoAction(isEncrypt: true);

    private void DecryptAll_Click(object sender, RoutedEventArgs e)
        => TableCryptoAction(isEncrypt: false);

    private void TableCryptoAction(bool isEncrypt)
    {
        if (DataContext is not ModuleDetailViewModel vm) return;

        var action = isEncrypt ? "全体を一括暗号化" : "全体を一括復号";
        var confirm = MessageBox.Show(
            $"テーブル全体を{(isEncrypt ? "暗号化" : "復号")}しますか？\n（除外カラムはスキップされます）",
            action, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = isEncrypt
            ? vm.EncryptAll()
            : vm.DecryptAll();

        ShowBatchResult(result, isEncrypt, action);
    }

    // ── 結果表示ヘルパー ──────────────────────────────────────────

    private static void ShowBatchResult(BatchCryptoResult result, bool isEncrypt, string title)
    {
        var icon = result.HasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information;
        MessageBox.Show(result.ToSummary(isEncrypt), title, MessageBoxButton.OK, icon);
    }
}
