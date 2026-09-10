using System.IO;
using FabriqStudio.Models;
using FabriqStudio.Models.Master;
using FabriqStudio.Services;
using FabriqStudio.Services.Collect;
using FabriqStudio.Services.Master;
using FabriqStudio.ViewModels;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>採取画面の流れ（採取 → 選ぶ → 配置 → プロファイルに実行行）。外部コマンドは偽物、配置は本物。</summary>
public sealed class CollectViewModelTests
{
    private const string ProfileHeader = "Order,ScriptPath,Enabled,Description,Segment\r\n";

    private sealed class Fixture : IDisposable
    {
        public TempWorkspace    Ws  { get; }
        public DataSetContext   Ctx { get; }
        public CollectViewModel Vm  { get; }
        public FakeDrivers      Drivers { get; } = new();
        public List<PickListItem>? Offered { get; private set; }

        public Fixture(string? profile = "Cust_A")
        {
            Ws = new TempWorkspace();
            Ws.File("modules/standard/taskbar_config/module.csv", "MenuName,Category,Script,Order,Enabled\r\nTaskbar Pin,Desktop,taskbar_config.ps1,50,1\r\n");
            Ws.File("modules/standard/taskbar_config/taskbar_list.csv", "Enabled,Order,LinkPath,AppId,Description,Segment\r\n1,10,,Microsoft.Windows.Explorer,File Explorer,\r\n");
            Ws.File("modules/standard/storeapp_config/module.csv", "MenuName,Category,Script,Order,Enabled\r\nRemove Store Apps,Applications,storeapp_config.ps1,75,1\r\n");
            Ws.File("modules/standard/storeapp_config/storeapp_list.csv", "No,AppName,Enabled,Description,Segment\r\n10,Microsoft.BingNews,1,Bing News,\r\n");
            Ws.File("modules/standard/winget_install/module.csv", "MenuName,Category,Script,Order,Enabled\r\nWinget App Install,Applications,winget_install.ps1,20,1\r\n");
            Ws.File("modules/standard/winget_install/app_list.csv", "Enabled,AppID,Options,Description,Segment\r\n1,Google.Chrome,,Google Chrome,\r\n");
            Ws.File("modules/standard/driver_config/module.csv", "MenuName,Category,Script,Order,Enabled\r\nDriver Import,System,driver_import_config.ps1,92,1\r\n");
            Ws.File("modules/standard/driver_config/driver.csv", "Enabled,Id,model,Segment\r\n1,1,,\r\n");
            Ws.File("modules/standard/default_app_config/module.csv", "MenuName,Category,Script,Order,Enabled\r\nDefault App Config,System,default_app_config.ps1,94,1\r\n");
            Ws.File("modules/standard/default_app_config/default_app_list.csv", "Enabled,XmlFile,Description,Segment\r\n1,AppAssoc.xml,Default App Associations,\r\n");
            Ws.File("modules/extended/desktop_icon_config/module.csv", "MenuName,Category,Script,Order,Enabled\r\nDesktop Icon Restore,Desktop,desktop_icon_restore.ps1,87,1\r\n");
            Ws.File("profiles/Cust_A.csv", ProfileHeader + "10,standard/hostname_config/hostname_config.ps1,1,Hostname,\r\n");

            var workspace = new StubWorkspace(Ws.Root);
            var csv       = new CsvService(workspace);
            var file      = new FileService();
            var resolver  = Ws.Resolver();
            var place     = new CollectPlacementService(resolver, new ProfileDataService(resolver), file,
                                                        new ProfileService(workspace, csv), new ModuleService(workspace, csv, file));
            Ctx = new DataSetContext(workspace);
            Ctx.Set(profile);
            Vm = new CollectViewModel(workspace, Ctx, place, new FakeWinget(), new FakeStoreApps(), Drivers, new FakeIcons(), new FakeAppAssoc())
            {
                PickFiles    = (_, _) => [@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk"],
                PickFromList = req => PickAllSelectable(req),
                Confirm      = (_, _) => true,
            };
        }

        /// <summary>固定一覧はそのまま、検索なら "x" で検索して、選べるものを全部選ぶ。</summary>
        private async Task<IReadOnlyList<PickListItem>?> PickAllSelectable(PickListRequest req)
        {
            var items = req.Items ?? await req.Search!("x");
            Offered = items.ToList();
            var picked = items.Where(i => i.CanCheck).ToList();
            foreach (var p in picked) p.IsChecked = true;
            return picked;
        }

        public string[] Pdf(string rel) => File.ReadAllLines(Ws.Abs("profiles/Cust_A/modules/" + rel));
        public string[] Profile()      => File.ReadAllLines(Ws.Abs("profiles/Cust_A.csv"));

        public void Dispose() => Ws.Dispose();
    }

    // ── 偽物の採取サービス ──────────────────────────────────────

    private sealed class FakeWinget : IWingetSearchService
    {
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<WingetPackage>> SearchAsync(string query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WingetPackage>>([new("Google Chrome", "Google.Chrome", "1"), new("Mozilla Firefox", "Mozilla.Firefox", "2")]);
    }

    private sealed class FakeStoreApps : IStoreAppInventoryService
    {
        public Task<IReadOnlyList<StoreApp>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StoreApp>>([new("Microsoft.BingNews", "Microsoft", "1"), new("Contoso.App", "Contoso", "1")]);
    }

    private sealed class FakeDrivers : IDriverExportService
    {
        public string? LastDest { get; private set; }
        public string GetModelName() => "HP EliteBook 840 G8";
        public Task<DriverCaptureResult> ExportAsync(string destDir, CancellationToken ct = default)
        {
            LastDest = destDir;
            Directory.CreateDirectory(Path.Combine(destDir, "oem1.inf_amd64_abc"));
            return Task.FromResult(new DriverCaptureResult(true, false, 1, ""));
        }
    }

    private sealed class FakeIcons : IDesktopIconLayoutService
    {
        public Task<bool> ExportAsync(string destFile, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.WriteAllText(destFile, "Windows Registry Editor Version 5.00\r\n");
            return Task.FromResult(true);
        }
    }

    private sealed class FakeAppAssoc : IAppAssocService
    {
        public string BaseXmlPath => "";
        public Task EnsureLoadedAsync() => Task.CompletedTask;
        public IReadOnlyList<AppAssocApp>      Apps       => [];
        public IReadOnlyList<AppAssocCategory> Categories => [];
        public IReadOnlyList<AppAssocCandidate> LocalCandidates(string identifier) => [];
        public Task<string?> ExportFromThisPcAsync()
        {
            var p = Path.Combine(Path.GetTempPath(), $"fabriq_test_appassoc_{Guid.NewGuid():N}.xml");
            File.WriteAllText(p, "<DefaultAssociations />");
            return Task.FromResult<string?>(p);
        }
    }

    // ── テスト ─────────────────────────────────────────────────

    [Fact]
    public async Task Taskbar_AppendsRow_AndAddsProfileRow()
    {
        using var f = new Fixture();
        await f.Vm.Taskbar.RunCommand.ExecuteAsync(null);

        Assert.False(f.Vm.Taskbar.IsError, f.Vm.Taskbar.Status);
        Assert.StartsWith("✓ 1 件追加", f.Vm.Taskbar.Status);
        Assert.Contains("プロファイルに追加", f.Vm.Taskbar.Detail);
        var csv = f.Pdf("taskbar_config/taskbar_list.csv");
        Assert.Equal(3, csv.Length);
        Assert.Equal(@"1,20,C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk,,Google Chrome,", csv[2]);
        Assert.Contains(f.Profile(), l => l.Contains("standard/taskbar_config/taskbar_config.ps1"));
    }

    [Fact]
    public async Task StoreApps_OffersInstalledList_MarksRegistered_AppendsPicked()
    {
        using var f = new Fixture();
        await f.Vm.StoreApps.RunCommand.ExecuteAsync(null);

        Assert.False(f.Vm.StoreApps.IsError, f.Vm.StoreApps.Status);
        Assert.NotNull(f.Offered);
        Assert.True(f.Offered!.First(i => i.Name == "Microsoft.BingNews").IsRegistered);
        var csv = f.Pdf("storeapp_config/storeapp_list.csv");
        Assert.Equal(3, csv.Length);
        Assert.Equal("20,Contoso.App,1,Contoso.App,", csv[2]);
        Assert.Contains(f.Profile(), l => l.Contains("standard/storeapp_config/storeapp_config.ps1"));
    }

    [Fact]
    public async Task Winget_SearchesAndAppendsPicked_SkippingRegistered()
    {
        using var f = new Fixture();
        await f.Vm.Winget.RunCommand.ExecuteAsync(null);

        Assert.False(f.Vm.Winget.IsError, f.Vm.Winget.Status);
        var csv = f.Pdf("winget_install/app_list.csv");
        Assert.Equal(3, csv.Length);
        Assert.Equal("1,Mozilla.Firefox,,Mozilla Firefox,", csv[2]);
        Assert.Contains(f.Profile(), l => l.Contains("standard/winget_install/winget_install.ps1"));
    }

    [Fact]
    public async Task Drivers_ExportsIntoPdfModelFolder_KeepsAutoDetectRow_AddsImportRow()
    {
        using var f = new Fixture();
        await f.Vm.Drivers.RunCommand.ExecuteAsync(null);

        Assert.False(f.Vm.Drivers.IsError, f.Vm.Drivers.Status);
        Assert.Equal(f.Ws.Abs("profiles/Cust_A/modules/driver_config/driver/HP_EliteBook_840_G8"), f.Drivers.LastDest);
        Assert.Contains("driver/HP_EliteBook_840_G8/", f.Vm.Drivers.Status);
        Assert.Equal(2, f.Pdf("driver_config/driver.csv").Length);   // 本体の自動判定行（model 空）で足りる → 追加なし
        Assert.Contains(f.Profile(), l => l.Contains("standard/driver_config/driver_import_config.ps1"));
    }

    [Fact]
    public async Task DefaultApps_PlacesXml_AndAddsProfileRow()
    {
        using var f = new Fixture();
        await f.Vm.DefaultApps.RunCommand.ExecuteAsync(null);

        Assert.False(f.Vm.DefaultApps.IsError, f.Vm.DefaultApps.Status);
        Assert.True(File.Exists(f.Ws.Abs("profiles/Cust_A/modules/default_app_config/xml/AppAssoc.xml")));
        Assert.Equal(2, f.Pdf("default_app_config/default_app_list.csv").Length);
        Assert.Contains(f.Profile(), l => l.Contains("standard/default_app_config/default_app_config.ps1"));
    }

    [Fact]
    public async Task IconLayout_WritesRegIntoBackup_AndAddsRestoreRow()
    {
        using var f = new Fixture();
        await f.Vm.IconLayout.RunCommand.ExecuteAsync(null);

        Assert.False(f.Vm.IconLayout.IsError, f.Vm.IconLayout.Status);
        var dir = f.Ws.Abs("profiles/Cust_A/modules/desktop_icon_config/backup");
        Assert.Single(Directory.GetFiles(dir, "DesktopIcons_*.reg"));
        Assert.Contains(f.Profile(), l => l.Contains("extended/desktop_icon_config/desktop_icon_restore.ps1"));
    }

    [Fact]
    public async Task Cancel_LeavesNothingBehind()
    {
        using var f = new Fixture();
        f.Vm.PickFiles = (_, _) => null;
        await f.Vm.Taskbar.RunCommand.ExecuteAsync(null);

        Assert.Equal("キャンセル", f.Vm.Taskbar.Status);
        Assert.False(Directory.Exists(f.Ws.Abs("profiles/Cust_A")));
        Assert.Equal(2, f.Profile().Length);
    }

    [Fact]
    public void NoProfile_DisablesEverything()
    {
        using var f = new Fixture(profile: null);
        Assert.False(f.Vm.HasProfile);
        Assert.All(f.Vm.Items, i => Assert.False(i.RunCommand.CanExecute(null)));

        f.Ctx.Set("Cust_A");
        Assert.True(f.Vm.HasProfile);
        Assert.All(f.Vm.Items, i => Assert.True(i.RunCommand.CanExecute(null)));
    }
}
