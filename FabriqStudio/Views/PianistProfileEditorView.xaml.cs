using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FabriqStudio.Converters;
using FabriqStudio.Helpers;
using FabriqStudio.Models;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

public partial class PianistProfileEditorView : UserControl
{
    /// <summary>
    /// values.csv は wide format（NewPCName + 任意の変数列）で列が動的に決まるため、
    /// XAML 静的に DataGrid.Columns を書けない。VM の <see cref="PianistProfileEditorViewModel.CurrentData"/>
    /// が変わるたびに <see cref="RebuildValuesGridColumns"/> でこちらが columns を再構築する。
    ///
    /// 暗号化／復号は HostDetail と同じ右クリック ContextMenu UX で行う:
    /// 「このセル / この列全行 / この行全変数列」の 3 段階を、暗号化と復号の組合せで 6 アクション。
    /// パスフレーズは左ペイン下「🔑 パスフレーズ」から事前設定する既存フローを共有する。
    /// </summary>
    private PianistProfileEditorViewModel? _vm;
    /// <summary>現在購読中の VariableColumns（CurrentData 切替時の unsubscribe 用）。</summary>
    private System.Collections.ObjectModel.ObservableCollection<string>? _subscribedColumns;

    public PianistProfileEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ─── DataContext / VM 切替時に rebuild を仕掛ける ─────────────────
    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = e.NewValue as PianistProfileEditorViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            RebuildValuesGridColumns();
        }
        else
        {
            ValuesGrid.Columns.Clear();
            UnsubscribeColumns();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PianistProfileEditorViewModel.CurrentData))
            RebuildValuesGridColumns();
    }

    private void UnsubscribeColumns()
    {
        if (_subscribedColumns is null) return;
        _subscribedColumns.CollectionChanged -= OnVariableColumnsChanged;
        _subscribedColumns = null;
    }

    private void SubscribeColumns(System.Collections.ObjectModel.ObservableCollection<string> cols)
    {
        UnsubscribeColumns();
        cols.CollectionChanged += OnVariableColumnsChanged;
        _subscribedColumns = cols;
    }

    private void OnVariableColumnsChanged(
        object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RebuildValuesGridColumns();

    // ─── 列再構築 ─────────────────────────────────────────────────────
    private void RebuildValuesGridColumns()
    {
        ValuesGrid.Columns.Clear();

        var data = _vm?.CurrentData;
        if (data is null)
        {
            UnsubscribeColumns();
            return;
        }

        // VariableColumns の Add/Remove に追従して列を再構築
        SubscribeColumns(data.Values.VariableColumns);

        // 1. 削除ボタン列
        ValuesGrid.Columns.Add(BuildDeleteColumn());

        // 2. NewPCName 列
        ValuesGrid.Columns.Add(new DataGridTextColumn
        {
            Header     = "NewPCName",
            Binding    = new Binding(nameof(PianistValueRow.NewPCName)),
            Width      = new DataGridLength(140),
            IsReadOnly = true,
        });

        // 3. 変数列群
        foreach (var col in data.Values.VariableColumns)
            ValuesGrid.Columns.Add(BuildVariableColumn(col));
    }

    /// <summary>
    /// 行頭の削除ボタン列。`*` 行ではボタンを非表示にする（§5.2.B）。
    /// </summary>
    private static DataGridTemplateColumn BuildDeleteColumn()
    {
        var template = new DataTemplate();
        var btn = new FrameworkElementFactory(typeof(Button));
        btn.SetValue(Button.ContentProperty, "🗑");
        btn.SetValue(Button.PaddingProperty, new Thickness(2, 0, 2, 0));
        btn.SetValue(Button.MarginProperty,  new Thickness(2));
        btn.SetValue(Button.FontSizeProperty, 11.0);
        btn.SetValue(Button.ToolTipProperty, "この行を削除");
        btn.SetValue(Button.CursorProperty, Cursors.Hand);

        var cmdBinding = new Binding("DataContext.DeleteRowCommand")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DataGrid) }
        };
        btn.SetBinding(Button.CommandProperty, cmdBinding);
        btn.SetBinding(Button.CommandParameterProperty, new Binding("."));

        // `*` 行ではボタンを Hidden
        var visBinding = new Binding(nameof(PianistValueRow.IsStar));
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.VisibilityProperty, Visibility.Visible));
        var trigger = new DataTrigger { Binding = visBinding, Value = true };
        trigger.Setters.Add(new Setter(Button.VisibilityProperty, Visibility.Hidden));
        style.Triggers.Add(trigger);
        btn.SetValue(Button.StyleProperty, style);

        template.VisualTree = btn;

        return new DataGridTemplateColumn
        {
            Header        = "",
            Width         = new DataGridLength(36),
            CellTemplate  = template,
            CanUserSort   = false,
            CanUserResize = false,
            IsReadOnly    = true,
        };
    }

    /// <summary>
    /// 1 つの変数列を生成。表示は MultiBinding 経由のセル値（継承時 dim italic）、
    /// 編集はインデクサ書込。右クリックで <see cref="BuildCellContextMenu"/> が出る。
    /// </summary>
    private DataGridTemplateColumn BuildVariableColumn(string columnName)
    {
        return new DataGridTemplateColumn
        {
            Header              = columnName,
            Width               = new DataGridLength(180),
            CellTemplate        = BuildDisplayTemplate(columnName),
            CellEditingTemplate = BuildEditingTemplate(columnName),
        };
    }

    private DataTemplate BuildDisplayTemplate(string columnName)
    {
        // ── 表示用 / 継承判定用の MultiBinding ──────────────────
        MultiBinding NewMultiBinding(IMultiValueConverter converter)
        {
            var mb = new MultiBinding { Converter = converter };
            mb.Bindings.Add(new Binding($"[{columnName}]"));
            mb.Bindings.Add(new Binding($"Table.Star[{columnName}]"));
            mb.Bindings.Add(new Binding(nameof(PianistValueRow.IsStar)));
            return mb;
        }

        var displayMb   = NewMultiBinding((PianistCellDisplayConverter)Application.Current.Resources["PianistCellDisplayConverter"]);
        var inheritedMb = NewMultiBinding((PianistCellInheritedConverter)Application.Current.Resources["PianistCellInheritedConverter"]);

        // TextBlock（表示値）
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, displayMb);
        tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        tb.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        tb.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 6, 0));
        tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        // 空セルでも右クリック ContextMenu が開くように Transparent で hit-test 可能にする
        tb.SetValue(TextBlock.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        tb.SetValue(TextBlock.ToolTipProperty,
            "右クリック: 暗号化/復号 / 列操作（リネーム/削除）");

        // 継承時 dim italic
        var tbStyle   = new Style(typeof(TextBlock));
        var tbTrigger = new DataTrigger { Binding = inheritedMb, Value = true };
        tbTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99))));
        tbTrigger.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
        tbStyle.Triggers.Add(tbTrigger);
        tb.SetValue(TextBlock.StyleProperty, tbStyle);

        // ContextMenu（暗号化／復号 6 メニュー、列名は closure 経由でハンドラに渡る）
        tb.SetValue(FrameworkElement.ContextMenuProperty, BuildCellContextMenu(columnName));

        return new DataTemplate { VisualTree = tb };
    }

    private static DataTemplate BuildEditingTemplate(string columnName)
    {
        var editor = new FrameworkElementFactory(typeof(TextBox));
        var rawBinding = new Binding($"[{columnName}]")
        {
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            Mode = BindingMode.TwoWay,
        };
        editor.SetBinding(TextBox.TextProperty, rawBinding);
        editor.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        editor.SetValue(TextBox.PaddingProperty, new Thickness(4, 0, 4, 0));
        editor.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);

        return new DataTemplate { VisualTree = editor };
    }

    // ─── ContextMenu（HostDetailView と同じ click ハンドラパターン） ────

    private ContextMenu BuildCellContextMenu(string columnName)
    {
        var menu = new ContextMenu();

        var encCell = new MenuItem { Header = "🔒 暗号化 (このセル)" };
        encCell.Click += (s, _) => HandleCellAction(s, columnName, isEncrypt: true);
        menu.Items.Add(encCell);

        var decCell = new MenuItem { Header = "🔓 復号 (このセル)" };
        decCell.Click += (s, _) => HandleCellAction(s, columnName, isEncrypt: false);
        menu.Items.Add(decCell);

        menu.Items.Add(new Separator());

        var encCol = new MenuItem { Header = $"🔒 暗号化 (列: {columnName} 全行)" };
        encCol.Click += (_, _) => HandleColumnAction(columnName, isEncrypt: true);
        menu.Items.Add(encCol);

        var decCol = new MenuItem { Header = $"🔓 復号 (列: {columnName} 全行)" };
        decCol.Click += (_, _) => HandleColumnAction(columnName, isEncrypt: false);
        menu.Items.Add(decCol);

        menu.Items.Add(new Separator());

        var encRow = new MenuItem { Header = "🔒 暗号化 (この行 全変数列)" };
        encRow.Click += (s, _) => HandleRowAction(s, isEncrypt: true);
        menu.Items.Add(encRow);

        var decRow = new MenuItem { Header = "🔓 復号 (この行 全変数列)" };
        decRow.Click += (s, _) => HandleRowAction(s, isEncrypt: false);
        menu.Items.Add(decRow);

        menu.Items.Add(new Separator());

        // 列操作（§5.2.E）— ContextMenu 内に集約
        var renameCol = new MenuItem { Header = $"✏ 列をリネーム ({columnName})" };
        renameCol.Click += (_, _) => HandleColumnRename(columnName);
        menu.Items.Add(renameCol);

        var deleteCol = new MenuItem { Header = $"🗑 列を削除 ({columnName})" };
        deleteCol.Click += (_, _) => HandleColumnDelete(columnName);
        menu.Items.Add(deleteCol);

        return menu;
    }

    // ─── 列操作（§5.2.E） ────────────────────────────────────────

    private void AddColumn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _vm.CurrentData is null)
        {
            MessageBox.Show("プロファイルが選択されていません。", "列追加",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = PianistColumnNameDialog.ShowAdd(n => _vm.ValidateColumnName(n));
        if (string.IsNullOrEmpty(name)) return;

        var error = _vm.AddVariableColumn(name);
        if (error is not null)
            MessageBox.Show(error, "列追加", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void HandleColumnRename(string oldName)
    {
        if (_vm is null) return;

        var result = PianistColumnNameDialog.ShowRename(
            oldName,
            validator:        n => _vm.ValidateColumnName(n, renamingFrom: oldName),
            previewProvider:  () => _vm.FindProcedureReferences(oldName));
        if (result is null) return;

        var (newName, rewriteProcedure) = result.Value;
        var error = _vm.RenameVariableColumn(oldName, newName, rewriteProcedure);
        if (error is not null)
            MessageBox.Show(error, "列リネーム", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void HandleColumnDelete(string columnName)
    {
        if (_vm is null) return;

        var refs = _vm.FindProcedureReferences(columnName);
        var refMsg = refs.Count == 0
            ? "（procedure.csv 内に参照はありません）"
            : BuildReferencesMessage(refs);

        var msg = $"列「{columnName}」を削除しますか？\n\n"
                  + "procedure.csv の自動書換は行いません（仕様）。\n"
                  + "削除後、以下の参照は未定義扱いになります:\n\n"
                  + refMsg;

        var ok = MessageBox.Show(msg, $"列削除: {columnName}",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.OK) return;

        var error = _vm.RemoveVariableColumn(columnName);
        if (error is not null)
            MessageBox.Show(error, "列削除", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string BuildReferencesMessage(IReadOnlyList<PianistStep> refs)
    {
        var lines = refs.Take(20)
            .Select(s => $"  {s.PhaseID}  Step {s.StepNo}  ({s.Action}): {s.Value}");
        var more = refs.Count > 20 ? $"\n  ... 他 {refs.Count - 20} 件" : "";
        return $"参照箇所 ({refs.Count} 件):\n" + string.Join("\n", lines) + more;
    }

    private static PianistValueRow? FindRowFromMenuItem(object? sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is not ContextMenu cm) return null;
        if (cm.PlacementTarget is not FrameworkElement target) return null;
        return target.DataContext as PianistValueRow;
    }

    private void HandleCellAction(object? sender, string columnName, bool isEncrypt)
    {
        if (_vm is null) return;
        var row = FindRowFromMenuItem(sender);
        if (row is null) return;

        var error = isEncrypt
            ? _vm.EncryptCell(row, columnName)
            : _vm.DecryptCell(row, columnName);

        if (error is not null)
            MessageBox.Show(error, isEncrypt ? "暗号化" : "復号",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void HandleColumnAction(string columnName, bool isEncrypt)
    {
        if (_vm is null) return;
        var label = isEncrypt ? "暗号化" : "復号";

        var confirm = MessageBox.Show(
            $"列「{columnName}」の全行を{label}しますか？\n（{( isEncrypt ? "既に ENC: のセル / 空セル" : "ENC: でないセル" )}は自動でスキップされます）",
            $"列の一括{label}",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = isEncrypt ? _vm.EncryptColumn(columnName) : _vm.DecryptColumn(columnName);
        ShowBatchResult(result, isEncrypt, $"列「{columnName}」の一括{label}");
    }

    private void HandleRowAction(object? sender, bool isEncrypt)
    {
        if (_vm is null) return;
        var row = FindRowFromMenuItem(sender);
        if (row is null) return;
        var label = isEncrypt ? "暗号化" : "復号";

        var confirm = MessageBox.Show(
            $"行「{row.NewPCName}」の全変数セルを{label}しますか？\n（スキップ条件はメニューと同じ）",
            $"行の一括{label}",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = isEncrypt ? _vm.EncryptRow(row) : _vm.DecryptRow(row);
        ShowBatchResult(result, isEncrypt, $"行「{row.NewPCName}」の一括{label}");
    }

    private static void ShowBatchResult(BatchCryptoResult? result, bool isEncrypt, string title)
    {
        if (result is null) return;
        var image = result.HasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information;
        MessageBox.Show(result.ToSummary(isEncrypt), title, MessageBoxButton.OK, image);
    }

    // ─── Phase 操作（§7.2 / §11） ─────────────────────────────────

    private void AddPhase_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _vm.CurrentData is null)
        {
            MessageBox.Show("プロファイルが選択されていません。", "Phase 新規作成",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = PianistPhaseEditDialog.ShowNew(
            colors:    PianistProfileEditorViewModel.PhaseColors,
            validator: id => _vm.ValidatePhaseId(id));
        if (result is null) return;

        var (id, label, color) = result.Value;
        var error = _vm.CreatePhase(id, label, color);
        if (error is not null)
            MessageBox.Show(error, "Phase 新規作成", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static PianistPhaseSummary? FindPhaseFromMenuItem(object? sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is not ContextMenu cm) return null;
        if (cm.PlacementTarget is not FrameworkElement target) return null;
        return target.DataContext as PianistPhaseSummary;
    }

    private void EditPhase_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var phase = FindPhaseFromMenuItem(sender);
        if (phase is null) return;

        var result = PianistPhaseEditDialog.ShowRename(
            currentId:    phase.PhaseID,
            currentLabel: phase.PhaseLabel,
            currentColor: phase.Color,
            colors:       PianistProfileEditorViewModel.PhaseColors,
            validator:    id => _vm.ValidatePhaseId(id, renamingFrom: phase.PhaseID));
        if (result is null) return;

        var (newId, newLabel, newColor, renameFile) = result.Value;
        var idChanged = !string.Equals(newId, phase.PhaseID, StringComparison.Ordinal);

        // §7.3 衝突確認: リネーム先 .txt が既存（孤児）→ 上書き or 中止
        if (idChanged && renameFile && _vm.DoesInstructionsFileExist(newId))
        {
            var ok = MessageBox.Show(
                $"既存の instructions/{newId}.txt が見つかりました。\n上書きしてリネームしますか？",
                "リネーム先ファイル衝突",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        var error = _vm.RenamePhase(phase.PhaseID, newId, newLabel, newColor, renameFile);
        if (error is not null)
            MessageBox.Show(error, "Phase 編集", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DeletePhase_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var phase = FindPhaseFromMenuItem(sender);
        if (phase is null) return;

        var fileExists = _vm.DoesInstructionsFileExist(phase.PhaseID);
        var (ok, deleteFile) = PianistPhaseDeleteDialog.Show(
            phaseId:                phase.PhaseID,
            phaseLabel:             phase.PhaseLabel,
            stepCount:              phase.StepCount,
            instructionsFileExists: fileExists);
        if (!ok) return;

        var error = _vm.DeletePhase(phase.PhaseID, deleteInstructionsFile: deleteFile);
        if (error is not null)
            MessageBox.Show(error, "Phase 削除", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ─── Window Picker（WaitWin / AppFocus 用） ────────────────────
    //
    // Step 編集セルで Action="WaitWin"/"AppFocus" のとき表示される 🔍 ボタンの click。
    // 直接 step.Value を書き換えるため、TextBox 経由の TwoWay binding に依存しない
    // （Picker ダイアログにフォーカスが移ることでセル編集モードが exit しても安全）。

    private void WindowPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not PianistStep step) return;

        var picked = PianistWindowPickerDialog.Show(step.Value, Window.GetWindow(this));
        if (picked is not null) step.Value = picked;
    }

    // Step 並び替えは Helpers/DataGridRowDragDropBehavior が adorner ライン + 自動スクロールを
    // 含めて一括処理し、Drop 確定時に VM の MoveStepRowCommand へ RowMoveRequest（filter-view
    // index）を渡す。コードビハインド側に手書きハンドラは持たない。

    // ─── Variables sub-tab ─────────────────────────────────────────
    /// <summary>
    /// orphan 宣言（values.csv に列がない [Variables] エントリ）を削除。
    /// CollectionChanged が再シリアライズを発火するので [Variables] section から消える。
    /// </summary>
    private void DeleteOrphanVariable_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not Models.PianistVariableSelection sel) return;

        _vm.OrphanVariableSelections.Remove(sel);
    }

    // ─── Samples sub-tab (画像管理) ───────────────────────────────
    /// <summary>サポートする画像拡張子（小文字、ドット付き）。</summary>
    private static readonly string[] AllowedImageExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

    /// <summary>screenshots/ への画像コピー + [Samples] section へのエントリ追加。</summary>
    private void AddSampleImage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.CurrentData is null)
        {
            MessageBox.Show("プロファイルが選択されていません。", "Sample 画像追加",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Sample 画像を選択",
            Filter = "画像ファイル (*.png; *.jpg; *.jpeg; *.gif; *.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
            Multiselect = true,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        foreach (var src in dlg.FileNames)
            ImportSampleImage(src);
    }

    private void DeleteSample_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (SamplesListBox.SelectedItem is not PianistSampleEntry entry) return;

        var ok = MessageBox.Show(
            $"Sample エントリ「{entry.File}」を [Samples] section から削除しますか？\n\n" +
            "この操作は instruction ファイルの参照のみを削除します。\n" +
            "画像ファイル本体（screenshots/ 配下）の削除は別途行ってください。",
            "Sample 削除",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        _vm.CurrentSamples.Remove(entry);
    }

    private void MoveSampleUp_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = SamplesListBox.SelectedIndex;
        if (idx <= 0) return;
        var item = _vm.CurrentSamples[idx];
        _vm.CurrentSamples.RemoveAt(idx);
        _vm.CurrentSamples.Insert(idx - 1, item);
        SamplesListBox.SelectedIndex = idx - 1;
    }

    private void MoveSampleDown_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var idx = SamplesListBox.SelectedIndex;
        if (idx < 0 || idx >= _vm.CurrentSamples.Count - 1) return;
        var item = _vm.CurrentSamples[idx];
        _vm.CurrentSamples.RemoveAt(idx);
        _vm.CurrentSamples.Insert(idx + 1, item);
        SamplesListBox.SelectedIndex = idx + 1;
    }

    private void SamplesListBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void SamplesListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (_vm?.CurrentData is null) return;
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        foreach (var src in files)
            ImportSampleImage(src);
    }

    /// <summary>
    /// 1 枚の画像ファイルを <c>&lt;profile&gt;/screenshots/</c> へコピーし、
    /// 既存に同名がない場合のみ <see cref="PianistProfileEditorViewModel.CurrentSamples"/>
    /// にエントリ追加する。同名 + 既存エントリありなら更新（上書きコピー + Exists リフレッシュ）。
    /// </summary>
    private void ImportSampleImage(string sourcePath)
    {
        if (_vm?.CurrentData is null) return;
        if (!System.IO.File.Exists(sourcePath))
        {
            MessageBox.Show($"ファイルが見つかりません: {sourcePath}", "Sample 画像追加",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ext = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        if (Array.IndexOf(AllowedImageExtensions, ext) < 0)
        {
            MessageBox.Show(
                $"未対応の拡張子です: {ext}\n対応: {string.Join(", ", AllowedImageExtensions)}",
                "Sample 画像追加",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var screenshotsDir = System.IO.Path.Combine(_vm.CurrentData.Entry.FolderPath, "screenshots");
        try { System.IO.Directory.CreateDirectory(screenshotsDir); }
        catch (Exception ex)
        {
            MessageBox.Show($"screenshots/ 作成に失敗: {ex.Message}", "Sample 画像追加",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var fileName = System.IO.Path.GetFileName(sourcePath);
        var dstPath  = System.IO.Path.Combine(screenshotsDir, fileName);

        // 同名既存ファイルへの上書き確認
        if (System.IO.File.Exists(dstPath) &&
            !string.Equals(System.IO.Path.GetFullPath(sourcePath),
                           System.IO.Path.GetFullPath(dstPath), StringComparison.OrdinalIgnoreCase))
        {
            var ok = MessageBox.Show(
                $"screenshots/ に同名ファイル「{fileName}」が既に存在します。上書きしますか？",
                "Sample 画像追加",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
        }

        try
        {
            // 同一パスの場合はコピー不要（drag-drop で screenshots 内ファイル自身を drop した想定）
            if (!string.Equals(System.IO.Path.GetFullPath(sourcePath),
                               System.IO.Path.GetFullPath(dstPath), StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.Copy(sourcePath, dstPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"画像コピーに失敗: {ex.Message}", "Sample 画像追加",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 既存エントリがあれば Exists を再判定するだけ、無ければ追加
        var existing = _vm.CurrentSamples.FirstOrDefault(
            x => string.Equals(x.File, fileName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Exists = true;
        }
        else
        {
            _vm.CurrentSamples.Add(new PianistSampleEntry
            {
                File    = fileName,
                Caption = "",
                Exists  = true,
            });
        }
    }
}
