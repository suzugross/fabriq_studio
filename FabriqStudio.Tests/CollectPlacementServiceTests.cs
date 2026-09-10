using System.IO;
using FabriqStudio.Services;
using FabriqStudio.Services.Collect;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>採取物の配置: PDF への取り込み + 行追加 + プロファイルへの実行行の追加。</summary>
public sealed class CollectPlacementServiceTests
{
    private const string ProfileHeader = "Order,ScriptPath,Enabled,Description,Segment\r\n";

    private static (TempWorkspace ws, CollectPlacementService svc) Setup()
    {
        var ws = new TempWorkspace();
        ws.File("modules/standard/taskbar_config/module.csv", "MenuName,Category,Script,Order,Enabled\r\nTaskbar Pin,Desktop,taskbar_config.ps1,50,1\r\n");
        ws.File("modules/standard/taskbar_config/taskbar_config.ps1", "# ps");
        ws.File("modules/standard/taskbar_config/taskbar_list.csv",
            "Enabled,Order,LinkPath,AppId,Description,Segment\r\n1,10,,Microsoft.Windows.Explorer,File Explorer,\r\n");
        ws.File("modules/standard/driver_config/driver.csv", "Enabled,Id,model,Segment\r\n1,1,,\r\n");
        ws.File("profiles/Cust_A.csv", ProfileHeader + "10,standard/hostname_config/hostname_config.ps1,1,Hostname,\r\n");

        var workspace = new StubWorkspace(ws.Root);
        var csv       = new CsvService(workspace);
        var file      = new FileService();
        var resolver  = ws.Resolver();
        var svc = new CollectPlacementService(resolver, new ProfileDataService(resolver), file,
                                              new ProfileService(workspace, csv), new ModuleService(workspace, csv, file));
        return (ws, svc);
    }

    private static Dictionary<string, string> Row(params (string, string)[] cells)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (c, v) in cells) d[c] = v;
        return d;
    }

    [Fact]
    public async Task AppendRows_MaterializesBodyCsv_ThenAppends_WithNumberingAndDuplicateSkip()
    {
        var (ws, svc) = Setup();
        using (ws)
        {
            var rows = new List<IReadOnlyDictionary<string, string>>
            {
                Row(("Enabled", "1"), ("LinkPath", @"%APPDATA%\x.lnk"), ("Description", "x")),
                Row(("Enabled", "1"), ("LinkPath", @"%appdata%\X.LNK"), ("Description", "dup")),
                Row(("Enabled", "1"), ("LinkPath", @"C:\y.lnk"), ("Description", "y"), ("Unknown", "ignored")),
            };

            var r = await svc.AppendRowsAsync("Cust_A", "taskbar_config", "taskbar_list.csv", rows,
                                              uniqueColumn: "LinkPath", numberColumn: "Order", numberStep: 10);

            Assert.Equal((2, 1, true), (r.Added, r.Skipped, r.Materialized));
            var pdf = File.ReadAllLines(ws.Abs("profiles/Cust_A/modules/taskbar_config/taskbar_list.csv"));
            Assert.Equal("Enabled,Order,LinkPath,AppId,Description,Segment", pdf[0].TrimStart('\uFEFF'));
            Assert.Equal(4, pdf.Length);                                  // ヘッダー + 本体の 1 行 + 追加 2 行
            Assert.Equal(@"1,20,%APPDATA%\x.lnk,,x,", pdf[2]);
            Assert.Equal(@"1,30,C:\y.lnk,,y,", pdf[3]);
            Assert.Equal(2, File.ReadAllLines(ws.Abs("modules/standard/taskbar_config/taskbar_list.csv")).Length);   // 本体は不変
        }
    }

    [Fact]
    public async Task EnsureProfileRow_AddsOnce_AtEnd_WithProfileDetailPathFormat()
    {
        var (ws, svc) = Setup();
        using (ws)
        {
            Assert.True(await svc.EnsureProfileRowAsync("Cust_A", "taskbar_config", "taskbar_config.ps1"));
            Assert.False(await svc.EnsureProfileRowAsync("Cust_A", "taskbar_config", "taskbar_config.ps1"));

            var lines = File.ReadAllLines(ws.Abs("profiles/Cust_A.csv"));
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("20,standard/taskbar_config/taskbar_config.ps1,1,Taskbar Pin,", lines[2]);
        }
    }

    [Fact]
    public async Task AssetPath_PointsIntoPdf_WithoutCreatingFolders()
    {
        var (ws, svc) = Setup();
        using (ws)
        {
            var p = svc.AssetPath("Cust_A", "driver_config", "driver/HP_EliteBook");
            Assert.Equal(ws.Abs("profiles/Cust_A/modules/driver_config/driver/HP_EliteBook"), p);
            Assert.False(Directory.Exists(ws.Abs("profiles/Cust_A")));
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ReadCsv_FallsBackToBody_WithoutMaterializing()
    {
        var (ws, svc) = Setup();
        using (ws)
        {
            var t = await svc.ReadCsvAsync("Cust_A", "taskbar_config", "taskbar_list.csv");
            Assert.Single(t.Rows);
            Assert.False(File.Exists(ws.Abs("profiles/Cust_A/modules/taskbar_config/taskbar_list.csv")));
        }
    }
}
