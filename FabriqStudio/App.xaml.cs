using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FabriqStudio.Services;
using FabriqStudio.ViewModels;
using FabriqStudio.Views;

namespace FabriqStudio;

public partial class App : Application
{
    private IServiceProvider? _services;

    /// <summary>DI コンテナ。ダイアログ等から直接サービスを取得する場合に使用。</summary>
    public static IServiceProvider Services
        => ((App)Current)._services
           ?? throw new InvalidOperationException("ServiceProvider is not initialized.");

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
        services.AddSingleton<ILooperService, LooperService>();
        services.AddSingleton<IDigitalGyotaqService, DigitalGyotaqService>();
        services.AddSingleton<IRegistryCollectionService, RegistryCollectionService>();

        // --- ViewModels (Singleton: データを一度だけロード) ---
        services.AddSingleton<BasicParamsViewModel>();
        services.AddSingleton<ModuleEditViewModel>();
        services.AddSingleton<HostListViewModel>();
        services.AddSingleton<HostDetailViewModel>();
        services.AddSingleton<ModuleDetailViewModel>();
        services.AddSingleton<AppConfigViewModel>();
        services.AddSingleton<ProfileDetailViewModel>();
        services.AddSingleton<AutokeyRecipeEditorViewModel>();
        services.AddSingleton<LooperEditorViewModel>();
        services.AddSingleton<DigitalGyotaqEditorViewModel>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<RegistryCollectionViewModel>();
        services.AddSingleton<MainViewModel>();

        // --- Views ---
        services.AddTransient<MainWindow>();
    }
}
