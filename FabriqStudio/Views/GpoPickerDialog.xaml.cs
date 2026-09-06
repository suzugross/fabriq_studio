using System.Windows;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Services.Gpo;
using FabriqStudio.ViewModels.Gpo;

namespace FabriqStudio.Views;

/// <summary>GPO 辞書からポリシーを 1 件選んで状態・要素を決めるモーダル ダイアログ。</summary>
public partial class GpoPickerDialog : Window
{
    private readonly GpoPickerDialogViewModel _vm;

    private GpoPickerDialog(IGpoCatalogService service, GpoSelection? existing)
    {
        InitializeComponent();
        _vm = new GpoPickerDialogViewModel(service, existing);
        DataContext = _vm;
        Title = _vm.Title;

        _vm.RequestClose += ok =>
        {
            DialogResult = ok;
            Close();
        };
        Browser.ItemActivated += (_, _) =>
        {
            if (_vm.OkCommand.CanExecute(null)) _vm.OkCommand.Execute(null);
        };
        Closed += (_, _) => _vm.Detach();
    }

    /// <summary>
    /// ダイアログを開く。<paramref name="existing"/> を渡すとそのポリシーを選択した状態で開く（編集）。
    /// 追加／更新で閉じたら選択を、キャンセルなら null を返す。
    /// </summary>
    public static GpoSelection? Show(Window? owner, IGpoCatalogService service, GpoSelection? existing)
    {
        var dialog = new GpoPickerDialog(service, existing);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog._vm.Result : null;
    }
}
