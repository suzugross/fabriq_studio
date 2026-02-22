using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FabriqStudio.Services;
using FabriqStudio.ViewModels;
using FabriqStudio.Views;

namespace FabriqStudio;

public partial class App : Application
{
    private IServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        // ── ワークスペースの永続化復元 ───────────────────────────────────
        // VM 構築前に実行することで、各 VM コンストラクタが IsOpen=true を確認して
        // 直接データロードを行える。WorkspaceChanged は発火しない。
        _services.GetRequiredService<IWorkspaceService>().TryRestorePersisted();

        // ── レジストリ辞書カタログの初期化 ──────────────────────────────
        await _services.GetRequiredService<IRegistryCollectionService>().EnsureInitializedAsync();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // --- Services ---
        // IWorkspaceService: fabriq ルートパスの動的管理（永続化 / バリデーション / 変更通知）
        services.AddSingleton<IWorkspaceService, WorkspaceService>();

        services.AddSingleton<ICsvService, CsvService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IModuleService, ModuleService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IAutokeyService, AutokeyService>();
        services.AddSingleton<IRegistryCollectionService, RegistryCollectionService>();

        // --- ViewModels (Singleton: データを一度だけロード) ---
        services.AddSingleton<BasicParamsViewModel>();
        services.AddSingleton<ModuleEditViewModel>();
        services.AddSingleton<HostListViewModel>();
        services.AddSingleton<HostDetailViewModel>();
        services.AddSingleton<ModuleDetailViewModel>();
        services.AddSingleton<ProfileDetailViewModel>();
        services.AddSingleton<AutokeyRecipeEditorViewModel>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<RegistryCollectionViewModel>();
        services.AddSingleton<MainViewModel>();

        // --- Views ---
        services.AddTransient<MainWindow>();
    }
}
