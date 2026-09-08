using System.Windows;

namespace FabriqStudio.Helpers;

/// <summary>
/// ドロップ枠の「ドラッグ中」状態を View 側だけで持つ添付プロパティ。
/// コードビハインドの DragEnter / DragOver / DragLeave / Drop で更新し、XAML の DataTrigger が枠の強調表示に使う。
/// 受け付けるかどうかの判断は VM（IAssetDropTarget.CanDrop）に委ねる。
/// </summary>
public static class DropZoneVisual
{
    public static readonly DependencyProperty IsDragOverProperty = DependencyProperty.RegisterAttached(
        "IsDragOver", typeof(bool), typeof(DropZoneVisual), new PropertyMetadata(false));

    public static bool GetIsDragOver(DependencyObject obj) => (bool)obj.GetValue(IsDragOverProperty);

    public static void SetIsDragOver(DependencyObject obj, bool value) => obj.SetValue(IsDragOverProperty, value);
}
