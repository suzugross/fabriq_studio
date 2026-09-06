using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FabriqStudio.Services;
using FabriqStudio.Services.Gpo;
using FabriqStudio.Services.Master;
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

        // ── GPO 辞書（ADMX）の読み込みを裏で始める（マスタ設計 / GPO 辞書画面が利用時に待ち合わせる）──
        _ = _services.GetRequiredService<IGpoCatalogService>().EnsureLoadedAsync();

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
        services.AddSingleton<ILooperService, LooperService>();
        services.AddSingleton<IRegistryCollectionService, RegistryCollectionService>();
        services.AddSingleton<IPrinterDriverDetectorService, PrinterDriverDetectorService>();
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IModulePresetService, ModulePresetService>();
        services.AddSingleton<IHostListExportService, HostListExportService>();
        services.AddSingleton<IFabriqBackupService, FabriqBackupService>();
        services.AddSingleton<IFabriqUpdateService, FabriqUpdateService>();
        services.AddSingleton<IPianistProfileService, PianistProfileService>();
        services.AddSingleton<IPianistTestRunService, PianistTestRunService>();

        // マスタ設計（テンプレート / 回答 / 書き込み先解決 / 生成）
        services.AddSingleton<IMasterTemplateService, MasterTemplateService>();
        services.AddSingleton<IMasterAnswersService, MasterAnswersService>();
        services.AddSingleton<IMasterTargetResolver, MasterTargetResolver>();
        services.AddSingleton<IMasterProfileGeneratorService, MasterProfileGeneratorService>();
        services.AddSingleton<IInstallerCatalogService, InstallerCatalogService>();
        services.AddSingleton<IMasterAssetService, MasterAssetService>();
        services.AddSingleton<IOdtDownloadService, OdtDownloadService>();

        // GPO 辞書（ADMX/ADML から生成。ワークスペース非依存）と gpo_list.csv への書き出し
        services.AddSingleton<IGpoCatalogService, GpoCatalogService>();
        services.AddSingleton<IGpoExportService, GpoExportService>();

        // --- ViewModels (Singleton: データを一度だけロード) ---
        services.AddSingleton<BasicParamsViewModel>();
        services.AddSingleton<ModuleEditViewModel>();
        services.AddSingleton<HostListViewModel>();
        services.AddSingleton<HostDetailViewModel>();
        services.AddSingleton<ModuleDetailViewModel>();
        services.AddSingleton<AppConfigViewModel>();
        services.AddSingleton<ProfileDetailViewModel>();
        services.AddSingleton<LooperEditorViewModel>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<RegistryCollectionViewModel>();
        services.AddSingleton<PrinterDriverDetectorViewModel>();
        services.AddSingleton<PianistProfileEditorViewModel>();
        services.AddSingleton<MasterParamViewModel>();
        services.AddSingleton<GpoCollectionViewModel>();
        services.AddSingleton<MainViewModel>();

        // --- Views ---
        services.AddTransient<MainWindow>();
    }
}
