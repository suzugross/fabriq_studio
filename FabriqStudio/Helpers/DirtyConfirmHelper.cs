using System.Windows;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Helpers;

/// <summary>
/// 未保存編集の破棄確認ダイアログを表示する共通ヘルパ。
///
/// MainViewModel（画面遷移ガード）と ModuleSettingsDialog（ダイアログクローズ時）の両方から
/// 同一の文言・挙動で呼び出せるよう静的ユーティリティとして提供する。
///
/// ユーザーが OK を選択した場合は <see cref="IDirtyAwareViewModel.DiscardChanges"/> を呼んで
/// in-memory ロールバックしてから true を返す（親リストとエンティティを共有する画面で
/// 編集中の値が残らないようにする）。
/// </summary>
public static class DirtyConfirmHelper
{
    /// <summary>
    /// <paramref name="dirty"/> に未保存編集があれば破棄確認ダイアログを表示する。
    /// </summary>
    /// <returns>進行可（dirty=null / 編集なし / OK 選択）の場合 true、キャンセル時 false。</returns>
    public static bool ConfirmDiscard(IDirtyAwareViewModel? dirty)
    {
        if (dirty is null || !dirty.HasUnsavedChanges) return true;

        var result = MessageBox.Show(
            $"「{dirty.DirtyDescription}」に未保存の変更があります。\n\n" +
            "このまま続行すると、変更内容は失われます。\n" +
            "破棄して続行しますか？",
            "未保存の変更",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK) return false;

        dirty.DiscardChanges();
        return true;
    }
}
