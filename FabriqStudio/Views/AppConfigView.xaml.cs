using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FabriqStudio.Helpers;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class AppConfigView : UserControl
{
    private AppConfigViewModel? _vm;

    /// <summary>
    /// XAML で定義された「Enabled」列のオリジナル定義。
    /// preset.csv が存在する間は一時的に <see cref="DataGridTemplateColumn"/> で置き換え、
    /// preset が消えたら元に戻すために保持する。
    /// </summary>
    private DataGridColumn? _originalEnabledColumn;

    /// <summary>オリジナルの Enabled 列が DataGrid 内で持つインデックス（初回キャプチャ時に確定）。</summary>
    private int _enabledColumnIndex = -1;

    public AppConfigView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded             += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => CaptureOriginalEnabledColumn();

    /// <summary>
    /// DataContext（= AppConfigViewModel）切替時に <see cref="AppConfigViewModel.PresetsLoaded"/> の
    /// 購読を付け替える。旧 VM の参照はリークさせないよう必ず解除する。
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PresetsLoaded -= OnPresetsLoaded;

        _vm = e.NewValue as AppConfigViewModel;

        if (_vm is not null)
        {
            _vm.PresetsLoaded += OnPresetsLoaded;
            ApplyPresetToEnabledColumn();
        }
    }

    private void OnPresetsLoaded(object? sender, EventArgs e) => ApplyPresetToEnabledColumn();

    /// <summary>
    /// 初回のみ、XAML 側で定義された「Enabled」列の参照とインデックスを記憶する。
    /// preset が消えたときの復元に使用する。
    /// </summary>
    private void CaptureOriginalEnabledColumn()
    {
        if (_originalEnabledColumn is not null) return;
        if (AppConfigDataGrid is null) return;

        for (var i = 0; i < AppConfigDataGrid.Columns.Count; i++)
        {
            var col = AppConfigDataGrid.Columns[i];
            if (col is DataGridBoundColumn bc
                && bc.Binding is Binding b
                && string.Equals(b.Path?.Path, "Enabled", StringComparison.Ordinal))
            {
                _originalEnabledColumn = col;
                _enabledColumnIndex    = i;
                return;
            }
        }
    }

    /// <summary>
    /// preset.csv に Enabled のプリセットがある場合は TemplateColumn（ComboBox 編集）に差し替え、
    /// ない場合はオリジナルの TextColumn に戻す。ヘッダ幅・表示名は常にオリジナルに揃える。
    /// </summary>
    private void ApplyPresetToEnabledColumn()
    {
        if (_vm is null) return;
        CaptureOriginalEnabledColumn();
        if (_originalEnabledColumn is null) return;
        if (_enabledColumnIndex < 0 || _enabledColumnIndex >= AppConfigDataGrid.Columns.Count) return;

        const string columnName = "Enabled";

        if (_vm.ColumnPresets.TryGetValue(columnName, out var presets) && presets.Count > 0)
        {
            var newCol = PresetColumnFactory.Build(columnName, presets);
            // XAML 定義の日本語ヘッダ「有効」と幅 50 を引き継ぐ
            newCol.Header = _originalEnabledColumn.Header;
            newCol.Width  = _originalEnabledColumn.Width;
            AppConfigDataGrid.Columns[_enabledColumnIndex] = newCol;
        }
        else
        {
            // preset なし: オリジナルに復元（既にオリジナルなら何もしない）
            if (!ReferenceEquals(AppConfigDataGrid.Columns[_enabledColumnIndex], _originalEnabledColumn))
                AppConfigDataGrid.Columns[_enabledColumnIndex] = _originalEnabledColumn;
        }
    }

    // ── プリセット対応: 暗号化セル(ENC:)の誤編集ガード ─────────────────────

    /// <summary>
    /// プリセット対象列で <c>ENC:</c> 値を編集しようとした際、警告を表示して編集をキャンセルする。
    /// </summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (_vm is null) return;

        // 「Enabled」列のみプリセット対象。他列（Type など）は既存の編集挙動を維持する。
        var columnName = GetColumnDataName(e.Column);
        if (string.IsNullOrEmpty(columnName)) return;
        if (!_vm.ColumnPresets.ContainsKey(columnName)) return;

        if (e.Row.Item is not DataRowView row) return;
        if (!row.Row.Table.Columns.Contains(columnName)) return;

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

    /// <summary>
    /// 列の実データ名（CSV 列名）を取得する。
    /// <list type="bullet">
    ///   <item>XAML 静的定義列: <see cref="DataGridBoundColumn.Binding"/> の Path</item>
    ///   <item>動的差し替え後の TemplateColumn: <see cref="DataGridColumn.SortMemberPath"/>（ファクトリで設定）</item>
    /// </list>
    /// </summary>
    private static string? GetColumnDataName(DataGridColumn? column)
    {
        if (column is null) return null;

        if (column is DataGridBoundColumn bc && bc.Binding is Binding b)
            return b.Path?.Path;

        return column.SortMemberPath;
    }
}
