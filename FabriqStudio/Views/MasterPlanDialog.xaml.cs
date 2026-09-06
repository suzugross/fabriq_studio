using System.ComponentModel;
using System.Windows;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Master;
using FabriqStudio.ViewModels;

namespace FabriqStudio.Views;

/// <summary>
/// 生成計画の確認ダイアログ。書き込みはこのモーダルの中でだけ行う。
/// 生成中（IsApplying）はウィンドウを閉じられない。
/// </summary>
public partial class MasterPlanDialog : Window
{
    private readonly MasterPlanDialogViewModel _vm;

    private MasterPlanDialog(MasterPlan plan, IMasterProfileGeneratorService generator)
    {
        InitializeComponent();
        _vm = new MasterPlanDialogViewModel(plan, generator);
        _vm.CloseRequested += (_, _) => { if (!_vm.IsApplying) Close(); };
        DataContext = _vm;
        Title       = _vm.Title;
    }

    /// <summary>
    /// ダイアログを表示し、生成が実行された場合はその結果を返す（閉じただけなら null）。
    /// </summary>
    public static MasterApplyResult? Show(MasterPlan plan, IMasterProfileGeneratorService generator, Window? owner = null)
    {
        var dialog = new MasterPlanDialog(plan, generator)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
        return dialog._vm.Result;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsApplying) e.Cancel = true;
        base.OnClosing(e);
    }
}
