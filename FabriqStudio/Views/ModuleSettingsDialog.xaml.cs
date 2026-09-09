using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Helpers;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FabriqStudio.Views;

public partial class ModuleSettingsDialog : Window
{
    private ModuleSettingsDialog(ModuleMasterEntry module, string? dataSet)
    {
        var sp  = App.Services;
        var dir = module.ModuleDir ?? "";

        // モジュール種別に応じた VM インスタンスを生成
        // App.xaml の DataTemplate が VM 型に応じて自動的に正しい View をレンダリングする
        object vm;
        var dirName = System.IO.Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (dirName.Equals("app_config", StringComparison.OrdinalIgnoreCase))
        {
            var appVm = new AppConfigViewModel(
                sp.GetRequiredService<IFileService>(),
                sp.GetRequiredService<IModuleDataResolver>(),
                sp.GetRequiredService<IProfileDataService>());
            appVm.Load(module, dataSet);
            vm = appVm;
        }
        else
        {
            var detailVm = new ModuleDetailViewModel(
                sp.GetRequiredService<IFileService>(),
                sp.GetRequiredService<ICsvService>(),
                sp.GetRequiredService<IRegistryCollectionService>(),
                sp.GetRequiredService<ICryptoService>(),
                sp.GetRequiredService<IModulePresetService>(),
                sp.GetRequiredService<IModuleDataResolver>(),
                sp.GetRequiredService<IProfileDataService>());
            detailVm.Load(module, dataSet);
            vm = detailVm;
        }

        InitializeComponent();
        Title = dataSet is null ? "モジュール設定（本体）" : $"モジュール設定 — プロファイル {dataSet}";
        DetailContent.Content = vm;

        // NavigateBackMessage をインターセプトしてダイアログを閉じる
        // （MainViewModel への伝播は MainViewModel 側のモーダルガードで防いでいる）
        Loaded   += (_, _) => WeakReferenceMessenger.Default.Register<NavigateBackMessage>(this, OnNavigateBack);
        Unloaded += (_, _) => WeakReferenceMessenger.Default.Unregister<NavigateBackMessage>(this);
    }

    private void OnNavigateBack(object recipient, NavigateBackMessage msg)
    {
        // ダイアログ内の「← 戻る」: 未保存の変更があれば確認する。
        // OK 時に DiscardChanges → DialogResult=true で閉じる（OnClosing 経由で再確認はしない）。
        if (!DirtyConfirmHelper.ConfirmDiscard(DetailContent.Content as IDirtyAwareViewModel))
            return;
        WeakReferenceMessenger.Default.Unregister<NavigateBackMessage>(this);
        DialogResult = true;
    }

    /// <summary>
    /// X ボタン / Esc / Alt+F4 等での閉鎖時に未保存編集を破棄して良いか確認する。
    /// 「← 戻る」経由（DialogResult=true 設定後）の場合、DiscardChanges 済みなので
    /// HasUnsavedChanges=false となり Helper は即 true を返す（多重ダイアログにならない）。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!DirtyConfirmHelper.ConfirmDiscard(DetailContent.Content as IDirtyAwareViewModel))
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// モジュール設定ダイアログを表示するファクトリメソッド。
    /// </summary>
    /// <param name="module">対象モジュール</param>
    /// <param name="owner">オーナーウィンドウ（省略時は MainWindow）</param>
    /// <param name="dataSet">編集先のプロファイル名（profiles/&lt;名&gt;/modules/ を編集）。null なら本体。</param>
    public static void Show(ModuleMasterEntry module, Window? owner = null, string? dataSet = null)
    {
        var dialog = new ModuleSettingsDialog(module, dataSet)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }
}
