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
    private readonly ModuleDetailViewModel _vm;

    private ModuleSettingsDialog(ModuleMasterEntry module)
    {
        // DI コンテナからサービスを取得し、Singleton とは別の VM インスタンスを生成
        var sp = App.Services;
        _vm = new ModuleDetailViewModel(
            sp.GetRequiredService<IFileService>(),
            sp.GetRequiredService<IWorkspaceService>(),
            sp.GetRequiredService<IRegistryCollectionService>());

        InitializeComponent();
        DetailView.DataContext = _vm;

        // NavigateBackMessage をインターセプトしてダイアログを閉じる
        // （MainViewModel への伝播を防止）
        Loaded   += (_, _) => WeakReferenceMessenger.Default.Register<NavigateBackMessage>(this, OnNavigateBack);
        Unloaded += (_, _) => WeakReferenceMessenger.Default.Unregister<NavigateBackMessage>(this);

        _vm.Load(module);
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
