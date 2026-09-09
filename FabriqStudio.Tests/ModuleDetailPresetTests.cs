using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.ViewModels;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>
/// preset.csv（ComboBox の候補）はフレームワーク資産（ブリーフ §4）なので、
/// プロファイル文脈（PDF）で開いても常に本体側を読む。PDF 側に置かれた preset.csv は無視する。
/// </summary>
public sealed class ModuleDetailPresetTests
{
    [Fact]
    public async Task ColumnPresets_ComeFromBody_EvenWhenPdfHasItsOwnPresetCsv()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/power_config/power_config.csv", "Enabled,Plan\r\n1,Balanced\r\n");
        ws.File("modules/standard/power_config/preset.csv",
            "Column,Value,Label\r\nPlan,Balanced,バランス\r\nPlan,High,高パフォーマンス\r\n");
        ws.File("profiles/Cust_A/modules/power_config/power_config.csv", "Enabled,Plan\r\n1,High\r\n");
        ws.File("profiles/Cust_A/modules/power_config/preset.csv",
            "Column,Value,Label\r\nPlan,WRONG,PDF 側の preset.csv は読まれない\r\n");

        var vm = NewVm(ws);
        vm.Load(new ModuleMasterEntry { ModuleDir = "power_config", Kind = "standard", MenuName = "電源" }, "Cust_A");
        await WaitLoadedAsync(vm);

        Assert.True(vm.IsProfileContext);
        Assert.False(vm.IsFallbackView);                                    // 設定 CSV は PDF 側を採用
        Assert.Equal(["power_config.csv"], vm.ConfigCsvFiles.ToArray());   // preset.csv は設定 CSV として列挙されない
        Assert.True(vm.ColumnPresets.TryGetValue("Plan", out var plan));
        Assert.Equal(["Balanced", "High"], plan.ToArray());                 // 本体の preset.csv
    }

    [Fact]
    public async Task ColumnPresets_ComeFromBody_WhenPdfFallsBackToBodyCsv()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/power_config/power_config.csv", "Enabled,Plan\r\n1,Balanced\r\n");
        ws.File("modules/standard/power_config/preset.csv", "Column,Value,Label\r\nPlan,Balanced,バランス\r\n");
        // profiles/Cust_B には power_config が無い → 本体を読み取り専用で表示（フォールバック）

        var vm = NewVm(ws);
        vm.Load(new ModuleMasterEntry { ModuleDir = "power_config", Kind = "standard", MenuName = "電源" }, "Cust_B");
        await WaitLoadedAsync(vm);

        Assert.True(vm.IsFallbackView);
        Assert.True(vm.ColumnPresets.TryGetValue("Plan", out var plan));
        Assert.Equal(["Balanced"], plan.ToArray());
    }

    private static ModuleDetailViewModel NewVm(TempWorkspace ws)
    {
        var workspace = new StubWorkspace(ws.Root);
        var resolver  = ws.Resolver();
        return new ModuleDetailViewModel(
            new FileService(), new CsvService(workspace), new RegistryCollectionService(), new CryptoService(),
            new ModulePresetService(), resolver, new ProfileDataService(resolver));
    }

    /// <summary>Load() は fire-and-forget なので、読み込み完了（IsLoading=false）まで待つ。</summary>
    private static async Task WaitLoadedAsync(ModuleDetailViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (vm.IsLoading && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.ErrorMessage);
    }
}
