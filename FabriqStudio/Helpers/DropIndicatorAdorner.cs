using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FabriqStudio.Helpers;

/// <summary>
/// DataGridRow の上端または下端に青い水平線を描画する Adorner。
/// D&amp;D のドロップ確定位置を視覚的に示すために <see cref="DataGridRowDragDropBehavior"/> が使用する。
/// <para>
/// 色は選択ハイライトと同じ <c>#3399FF</c> で統一。
/// ヒットテストには参加しないため、DragOver の判定を妨げない。
/// </para>
/// </summary>
public sealed class DropIndicatorAdorner : Adorner
{
    public enum DropPosition { Above, Below }

    /// <summary>
    /// Pen と Brush は Freeze 済みの静的インスタンスを使い回す。
    /// 複数回の Adorner 生成でもガベージ圧を増やさない。
    /// </summary>
    private static readonly Pen IndicatorPen;

    static DropIndicatorAdorner()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));
        brush.Freeze();
        IndicatorPen = new Pen(brush, 2.0);
        IndicatorPen.Freeze();
    }

    private DropPosition _position;

    public DropIndicatorAdorner(DataGridRow row, DropPosition position) : base(row)
    {
        _position        = position;
        IsHitTestVisible = false;
    }

    /// <summary>描画位置（対象行の上端 / 下端）。外部から切り替え可能。</summary>
    public DropPosition Position
    {
        get => _position;
        set
        {
            if (_position == value) return;
            _position = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var row = (DataGridRow)AdornedElement;

        // y=0 / y=ActualHeight のいずれかに 2px 線を引く。
        // Adorner は AdornerLayer に描画されるため行境界をまたぐ表示も可能。
        var y = _position == DropPosition.Above ? 0 : row.ActualHeight;

        dc.DrawLine(IndicatorPen, new Point(0, y), new Point(row.ActualWidth, y));
    }
}
