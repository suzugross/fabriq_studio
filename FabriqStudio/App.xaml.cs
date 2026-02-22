using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FabriqStudio.Services;
using FabriqStudio.ViewModels;
using FabriqStudio.Views;

namespace FabriqStudio;

public partial class App : Application
{
    private IServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // --- Configuration ---
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        services.AddSingleton<IConfiguration>(config);

        // --- Services ---
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ICsvService, CsvService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IModuleService, ModuleService>();
        services.AddSingleton<IFileService, FileService>();

        // --- ViewModels (Singleton: データを一度だけロード) ---
        services.AddSingleton<BasicParamsViewModel>();
        services.AddSingleton<ModuleEditViewModel>();
        services.AddSingleton<HostListViewModel>();
        services.AddSingleton<HostDetailViewModel>();
        services.AddSingleton<ModuleDetailViewModel>();
        services.AddSingleton<ProfileDetailViewModel>();
        services.AddSingleton<MainViewModel>();

        // --- Views ---
        services.AddTransient<MainWindow>();
    }
}
