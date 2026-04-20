using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FabriqStudio.Models;

namespace FabriqStudio.Helpers;

/// <summary>
/// DataGrid の行ドラッグ&amp;ドロップによる並べ替えを提供する Attached Behavior。
/// <para>
/// 設計方針:
/// <list type="bullet">
///   <item>MVVM 厳守: Drop 確定時に <see cref="MoveCommandProperty"/> を介して VM の ICommand を呼ぶ。</item>
///   <item>汎用性: DataGrid の種別（行モデルの型）に依存せず動作する。</item>
///   <item>併存性: 既存の ↑/↓ ボタン操作・セル編集・選択ハイライトを壊さない。</item>
/// </list>
/// </para>
/// <para>
/// 使用例 (XAML):
/// <code>
/// &lt;DataGrid helpers:DataGridRowDragDropBehavior.IsEnabled="True"
///           helpers:DataGridRowDragDropBehavior.MoveCommand="{Binding MoveRowCommand}"&gt;
/// </code>
/// </para>
/// </summary>
public static class DataGridRowDragDropBehavior
{
    /// <summary>
    /// DataObject のフォーマット識別子。
    /// 複数 DataGrid 間の誤ドロップを防ぐため、本 Behavior 固有の文字列を使用する。
    /// </summary>
    private const string DragFormat = "FabriqStudio.DataGridRow";

    // ═══════════════════════════════════════════════════════════════════════
    // Attached Properties
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>D&amp;D 機能の有効/無効を切り替える。</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DataGridRowDragDropBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    /// <summary>Drop 確定時に実行する ICommand。パラメータは <see cref="RowMoveRequest"/>。</summary>
    public static readonly DependencyProperty MoveCommandProperty =
        DependencyProperty.RegisterAttached(
            "MoveCommand",
            typeof(ICommand),
            typeof(DataGridRowDragDropBehavior),
            new PropertyMetadata(null));

    public static ICommand? GetMoveCommand(DependencyObject obj) => (ICommand?)obj.GetValue(MoveCommandProperty);
    public static void SetMoveCommand(DependencyObject obj, ICommand? value) => obj.SetValue(MoveCommandProperty, value);

    // ═══════════════════════════════════════════════════════════════════════
    // 内部状態（DataGrid ごとに保持する）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>ドラッグ開始前の押下位置・対象行、および Phase 2 のドロップ位置 Adorner を保持する一時状態。</summary>
    private sealed class DragState
    {
        public Point                   StartPoint;
        public DataGridRow?            Source;
        public int                     SourceIndex = -1;

        // ── Phase 2: ドロップ位置インジケータ ──
        public DataGridRow?            IndicatorRow;
        public DropIndicatorAdorner?   Indicator;

        // ── Phase 3: 自動スクロール ──
        public DispatcherTimer?        ScrollTimer;
        public Point                   LastDragPosition;
    }

    // ── Phase 3: 自動スクロールのパラメータ ──
    /// <summary>自動スクロール発動ゾーン（DataGrid 上下端からのピクセル距離）</summary>
    private const double AutoScrollTriggerZone = 30.0;
    /// <summary>1 ティックあたりのスクロール量（ピクセル）</summary>
    private const double AutoScrollStep        = 12.0;
    /// <summary>タイマーティック間隔（ミリ秒）</summary>
    private const int    AutoScrollIntervalMs  = 30;

    /// <summary>内部状態を DataGrid にぶら下げるための Attached Property（外部からは見えない）。</summary>
    private static readonly DependencyProperty DragStateProperty =
        DependencyProperty.RegisterAttached(
            "DragState",
            typeof(DragState),
            typeof(DataGridRowDragDropBehavior),
            new PropertyMetadata(null));

    // ═══════════════════════════════════════════════════════════════════════
    // イベントハンドラの attach / detach
    // ═══════════════════════════════════════════════════════════════════════

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        if ((bool)e.NewValue)
        {
            grid.SetValue(DragStateProperty, new DragState());
            grid.AllowDrop                      = true;
            grid.PreviewMouseLeftButtonDown    += OnPreviewMouseLeftButtonDown;
            grid.PreviewMouseMove              += OnPreviewMouseMove;
            grid.DragOver                      += OnDragOver;
            grid.DragLeave                     += OnDragLeave;
            grid.Drop                          += OnDrop;
        }
        else
        {
            if (grid.GetValue(DragStateProperty) is DragState state)
            {
                RemoveIndicator(state);
                StopScrollTimer(state);
            }
            grid.AllowDrop                      = false;
            grid.PreviewMouseLeftButtonDown    -= OnPreviewMouseLeftButtonDown;
            grid.PreviewMouseMove              -= OnPreviewMouseMove;
            grid.DragOver                      -= OnDragOver;
            grid.DragLeave                     -= OnDragLeave;
            grid.Drop                          -= OnDrop;
            grid.ClearValue(DragStateProperty);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ドラッグ候補の記録（左ボタン押下）
    // ═══════════════════════════════════════════════════════════════════════

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.GetValue(DragStateProperty) is not DragState state) return;

        // セル編集中は D&D を抑止（通常のテキスト編集操作を優先）
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.IsEditing == true) return;

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null) return;

        state.StartPoint  = e.GetPosition(grid);
        state.Source      = row;
        state.SourceIndex = grid.ItemContainerGenerator.IndexFromContainer(row);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 閾値判定 → DoDragDrop 起動
    // ═══════════════════════════════════════════════════════════════════════

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not DataGrid grid) return;
        if (grid.GetValue(DragStateProperty) is not DragState state) return;
        if (state.Source is null || state.SourceIndex < 0) return;

        var delta = e.GetPosition(grid) - state.StartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // ドラッグ本番開始
        var data = new DataObject(DragFormat, state.SourceIndex);
        try
        {
            DragDrop.DoDragDrop(state.Source, data, DragDropEffects.Move);
        }
        finally
        {
            // DoDragDrop はブロッキング呼び出し。完了・キャンセル・例外いずれでも状態・インジケータ・タイマーをクリア。
            RemoveIndicator(state);
            StopScrollTimer(state);
            state.Source      = null;
            state.SourceIndex = -1;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ドラッグ中: Effect の確定
    // ═══════════════════════════════════════════════════════════════════════

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.GetValue(DragStateProperty) is not DragState state) return;

        if (!e.Data.GetDataPresent(DragFormat))
        {
            e.Effects = DragDropEffects.None;
            RemoveIndicator(state);
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        state.LastDragPosition = e.GetPosition(grid);
        EnsureScrollTimer(grid, state);
        UpdateIndicator(grid, e, state);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.GetValue(DragStateProperty) is not DragState state) return;

        // DataGrid の境界外へドラッグした場合のみインジケータとタイマーを止める。
        // （子要素へのドラッグ時も DragLeave が発火するが、その後 DragOver で再構築されるため問題なし）
        var pos = e.GetPosition(grid);
        if (pos.X < 0 || pos.Y < 0 || pos.X > grid.ActualWidth || pos.Y > grid.ActualHeight)
        {
            RemoveIndicator(state);
            StopScrollTimer(state);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ドロップ確定: MoveCommand 実行
    // ═══════════════════════════════════════════════════════════════════════

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.GetValue(DragStateProperty) is DragState state)
        {
            RemoveIndicator(state);
            StopScrollTimer(state);
        }

        if (!e.Data.GetDataPresent(DragFormat)) return;

        var cmd = GetMoveCommand(grid);
        if (cmd is null) return;

        var srcIdx = (int)e.Data.GetData(DragFormat);
        var dstIdx = ComputeDropIndex(grid, e, srcIdx);

        if (dstIdx < 0 || srcIdx == dstIdx) return;

        var req = new RowMoveRequest(srcIdx, dstIdx);
        if (cmd.CanExecute(req))
            cmd.Execute(req);

        e.Handled = true;
    }

    /// <summary>
    /// マウス位置から ObservableCollection.Move に渡す移動先インデックスを算出する。
    /// <para>
    /// マウスが対象行の上半分 → その行の位置に挿入 / 下半分 → その行の次に挿入。
    /// ObservableCollection.Move の仕様上、srcIdx &lt; dstIdx の場合は抜き取り後の
    /// インデックスで指定する必要があるため -1 補正する。
    /// </para>
    /// <para>
    /// 行以外の空白領域にドロップした場合はリスト末尾へ移動させる。
    /// </para>
    /// </summary>
    private static int ComputeDropIndex(DataGrid grid, DragEventArgs e, int srcIdx)
    {
        var targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        int dstIdx;

        if (targetRow is null)
        {
            // 空白（末尾空白 / 行の隙間）にドロップ → 末尾扱い
            dstIdx = grid.Items.Count - 1;
        }
        else
        {
            var targetIdx   = grid.ItemContainerGenerator.IndexFromContainer(targetRow);
            var pointInRow  = e.GetPosition(targetRow);
            var isUpperHalf = pointInRow.Y < targetRow.ActualHeight / 2;

            dstIdx = isUpperHalf ? targetIdx : targetIdx + 1;

            // ObservableCollection.Move の抜き取り後インデックス補正
            if (srcIdx < dstIdx) dstIdx--;
        }

        // 範囲チェック（万一の値崩れ対策）
        if (dstIdx < 0)                   dstIdx = 0;
        if (dstIdx >= grid.Items.Count)   dstIdx = grid.Items.Count - 1;

        return dstIdx;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 2: ドロップ位置インジケータ（Adorner）の制御
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 現在のマウス位置に応じて、ドロップ予定位置を示す Adorner を対象行に表示/更新する。
    /// 対象行・位置が前回と同じであれば何もしない（フリッカー防止）。
    /// </summary>
    private static void UpdateIndicator(DataGrid grid, DragEventArgs e, DragState state)
    {
        var target = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        DropIndicatorAdorner.DropPosition position;

        if (target is null)
        {
            // 行の外（末尾の空白領域等）→ 最終行の下端に指示
            target = FindLastRow(grid);
            if (target is null)
            {
                RemoveIndicator(state);
                return;
            }
            position = DropIndicatorAdorner.DropPosition.Below;
        }
        else
        {
            var pointInRow = e.GetPosition(target);
            position = pointInRow.Y < target.ActualHeight / 2
                ? DropIndicatorAdorner.DropPosition.Above
                : DropIndicatorAdorner.DropPosition.Below;
        }

        // 同じ行・同じ位置なら再描画しない
        if (ReferenceEquals(state.IndicatorRow, target)
            && state.Indicator?.Position == position)
            return;

        RemoveIndicator(state);

        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) return;

        var adorner = new DropIndicatorAdorner(target, position);
        layer.Add(adorner);

        state.Indicator    = adorner;
        state.IndicatorRow = target;
    }

    /// <summary>表示中の Adorner を AdornerLayer から除去する。</summary>
    private static void RemoveIndicator(DragState state)
    {
        if (state.Indicator is not null && state.IndicatorRow is not null)
        {
            var layer = AdornerLayer.GetAdornerLayer(state.IndicatorRow);
            layer?.Remove(state.Indicator);
        }
        state.Indicator    = null;
        state.IndicatorRow = null;
    }

    /// <summary>
    /// 物理的に存在する最後の DataGridRow コンテナを返す。
    /// 仮想化されていない範囲のみを対象とする（ドラッグ中の表示用途なので十分）。
    /// </summary>
    private static DataGridRow? FindLastRow(DataGrid grid)
    {
        for (int i = grid.Items.Count - 1; i >= 0; i--)
        {
            if (grid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                return row;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 3: 自動スクロール
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DragOver 中に呼ばれ、スクロールタイマーが未起動なら開始する。
    /// タイマーは <see cref="DragState.LastDragPosition"/> を見て上下端ゾーンに入っていれば
    /// 内部 ScrollViewer を定期スクロールする。インジケータの再構築は次の DragOver に任せる。
    /// </summary>
    private static void EnsureScrollTimer(DataGrid grid, DragState state)
    {
        if (state.ScrollTimer is not null) return;

        var scrollViewer = FindDescendant<ScrollViewer>(grid);
        if (scrollViewer is null) return;

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AutoScrollIntervalMs)
        };
        timer.Tick += (_, _) =>
        {
            var y      = state.LastDragPosition.Y;
            var height = grid.ActualHeight;
            var offset = scrollViewer.VerticalOffset;

            if (y < AutoScrollTriggerZone && offset > 0)
            {
                scrollViewer.ScrollToVerticalOffset(offset - AutoScrollStep);
                // スクロール後は行コンテナが変化する可能性があるためインジケータを一旦消す。
                // 次の DragOver（マウス移動 or 境界イベント）で再構築される。
                RemoveIndicator(state);
            }
            else if (y > height - AutoScrollTriggerZone && offset < scrollViewer.ScrollableHeight)
            {
                scrollViewer.ScrollToVerticalOffset(offset + AutoScrollStep);
                RemoveIndicator(state);
            }
        };
        timer.Start();
        state.ScrollTimer = timer;
    }

    /// <summary>スクロールタイマーを停止し参照を解放する。</summary>
    private static void StopScrollTimer(DragState state)
    {
        state.ScrollTimer?.Stop();
        state.ScrollTimer = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Visual Tree 走査ヘルパー
    // ═══════════════════════════════════════════════════════════════════════

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T t) return t;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    /// <summary>
    /// 指定要素の Visual Tree 子孫から最初にマッチする型 <typeparamref name="T"/> を深さ優先で探す。
    /// DataGrid 内部の ScrollViewer 検出に使用。
    /// </summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var result = FindDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}
