using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FabriqStudio.Views;

public partial class ModuleSettingsDialog : Window
{
    private ModuleSettingsDialog(ModuleMasterEntry module)
    {
        var sp  = App.Services;
        var dir = module.ModuleDir ?? "";

        // モジュール種別に応じた VM インスタンスを生成
        // App.xaml の DataTemplate が VM 型に応じて自動的に正しい View をレンダリングする
        object vm;
        if (dir.Contains("app_config", StringComparison.OrdinalIgnoreCase))
        {
            var appVm = new AppConfigViewModel(
                sp.GetRequiredService<IFileService>(),
                sp.GetRequiredService<IWorkspaceService>());
            appVm.Load(module);
            vm = appVm;
        }
        else
        {
            var detailVm = new ModuleDetailViewModel(
                sp.GetRequiredService<IFileService>(),
                sp.GetRequiredService<ICsvService>(),
                sp.GetRequiredService<IWorkspaceService>(),
                sp.GetRequiredService<IRegistryCollectionService>());
            detailVm.Load(module);
            vm = detailVm;
        }

        InitializeComponent();
        DetailContent.Content = vm;

        // NavigateBackMessage をインターセプトしてダイアログを閉じる
        // （MainViewModel への伝播を防止）
        Loaded   += (_, _) => WeakReferenceMessenger.Default.Register<NavigateBackMessage>(this, OnNavigateBack);
        Unloaded += (_, _) => WeakReferenceMessenger.Default.Unregister<NavigateBackMessage>(this);
    }

    private void OnNavigateBack(object recipient, NavigateBackMessage msg)
    {
        // ダイアログ内の「← 戻る」はウィンドウを閉じる動作にする
        WeakReferenceMessenger.Default.Unregister<NavigateBackMessage>(this);
        DialogResult = true;
    }

    /// <summary>
    /// モジュール設定ダイアログを表示するファクトリメソッド。
    /// </summary>
    /// <param name="module">対象モジュール</param>
    /// <param name="owner">オーナーウィンドウ（省略時は MainWindow）</param>
    public static void Show(ModuleMasterEntry module, Window? owner = null)
    {
        var dialog = new ModuleSettingsDialog(module)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }
}
