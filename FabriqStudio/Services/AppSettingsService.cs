using Microsoft.Extensions.Configuration;

namespace FabriqStudio.Services;

public class AppSettingsService : IAppSettingsService
{
    public string FabriqRootPath { get; }

    public AppSettingsService(IConfiguration configuration)
    {
        FabriqRootPath = configuration["FabriqSettings:RootPath"]
            ?? throw new InvalidOperationException(
                "appsettings.json に FabriqSettings:RootPath が設定されていません。");
    }
}
