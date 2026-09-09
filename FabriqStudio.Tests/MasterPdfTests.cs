using System.IO;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Master;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>マスタ設計のデータフォルダ（PDF）出力: 合成スナップショット・書き先解決・回答ファイルの移行。</summary>
public sealed class MasterPdfTests
{
    private static MasterCsvInfo Csv(string dir, string name) => new() { Name = name, AbsPath = Path.Combine(dir, name) };

    [Fact]
    public void Compose_PdfCsvsWin_RegListsAreModuleLevel_AssetFoldersAreFolderLevel()
    {
        var body = new MasterModuleInfo { Dir = "reg_hklm_config", Kind = "standard", AbsPath = @"C:\b" };
        body.Csvs["reg_hklm_list.csv"] = Csv(@"C:\b", "reg_hklm_list.csv");
        body.Csvs["other.csv"]         = Csv(@"C:\b", "other.csv");
        body.SubDirs.Add("INF");  body.SubDirFiles["INF"]        = new(StringComparer.OrdinalIgnoreCase) { "epson" };
        body.SubDirFiles[@"INF\epson"] = new(StringComparer.OrdinalIgnoreCase) { "e.inf" };
        body.SubDirs.Add("file"); body.SubDirFiles["file"]       = new(StringComparer.OrdinalIgnoreCase) { "a.exe" };

        var pdf = new MasterModuleInfo { Dir = "reg_hklm_config", Kind = "standard", AbsPath = @"C:\p", DataSet = "M" };
        pdf.Csvs["reg_hklm_list_M.csv"] = Csv(@"C:\p", "reg_hklm_list_M.csv");
        pdf.SubDirs.Add("INF"); pdf.SubDirFiles["INF"] = new(StringComparer.OrdinalIgnoreCase);   // 空 = 資材ゼロの明示

        var c = MasterModuleInfo.Compose(body, pdf);

        Assert.Equal(["other.csv", "reg_hklm_list_M.csv"], c.Csvs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());   // 本体の reg_* は読まれない
        Assert.Equal(@"C:\b\other.csv", c.Csvs["other.csv"].AbsPath);
        Assert.Empty(c.SubDirFiles["INF"]);                          // PDF の空フォルダが勝つ（本体の epson は消える）
        Assert.False(c.SubDirFiles.ContainsKey(@"INF\epson"));
        Assert.True(c.HasFile("file", "a.exe"));                     // PDF に無いフォルダは本体のまま
        Assert.Equal("M", c.DataSet);
        Assert.Equal(@"C:\b", c.AbsPath);                            // スクリプトの場所は本体
    }

    [Fact]
    public void Snapshot_ForMaster_ComposesPerModule_AndCsvInfoFor()
    {
        var s = new MasterWorkspaceSnapshot();
        var gpoBody = new MasterModuleInfo { Dir = "gpo_config", Kind = "standard", AbsPath = @"B\gpo" };
        gpoBody.Csvs["gpo_list.csv"] = Csv(@"B\gpo", "gpo_list.csv");
        var storeBody = new MasterModuleInfo { Dir = "storeapp_config", Kind = "standard", AbsPath = @"B\store" };
        storeBody.Csvs["storeapp_list.csv"] = Csv(@"B\store", "storeapp_list.csv");
        s.Modules["gpo_config"]      = gpoBody;
        s.Modules["storeapp_config"] = storeBody;

        var gpoPdf = new MasterModuleInfo { Dir = "gpo_config", DataSet = "M" };
        gpoPdf.Csvs["gpo_list.csv"] = Csv(@"P\gpo", "gpo_list.csv");
        var storePdf = new MasterModuleInfo { Dir = "storeapp_config", DataSet = "M_sysprep" };
        storePdf.Csvs["storeapp_list.csv"] = Csv(@"S\store", "storeapp_list.csv");
        s.Overlays["M"]         = new(StringComparer.OrdinalIgnoreCase) { ["gpo_config"] = gpoPdf };
        s.Overlays["M_sysprep"] = new(StringComparer.OrdinalIgnoreCase) { ["storeapp_config"] = storePdf };

        var view = s.ForMaster(dir => dir == "storeapp_config" ? "M_sysprep" : "M");

        Assert.Equal(@"P\gpo\gpo_list.csv",       view.GetModule("gpo_config")!.Csvs["gpo_list.csv"].AbsPath);
        Assert.Equal(@"S\store\storeapp_list.csv", view.GetModule("storeapp_config")!.Csvs["storeapp_list.csv"].AbsPath);
        Assert.Null(view.GetModule("nope"));
        Assert.Same(view.GetModule("gpo_config"), view.GetModule("gpo_config"));     // キャッシュ
        Assert.Same(s.Modules, view.Modules);

        Assert.Equal(@"B\gpo\gpo_list.csv", s.CsvInfoFor("gpo_config", "gpo_list.csv", "M_sysprep")!.AbsPath);   // その PDF に無ければ本体
        Assert.Equal(@"P\gpo\gpo_list.csv", s.CsvInfoFor("gpo_config", "gpo_list.csv", "M")!.AbsPath);
        Assert.Null(s.CsvInfoFor("gpo_config", "nope.csv", "M"));
    }

    [Fact]
    public void TargetResolver_DataSetFor_AndPaths()
    {
        using var ws = new TempWorkspace();
        ws.Dir("modules/standard/app_config");
        ws.Dir("modules/standard/sysprep_config");
        ws.Dir("profiles/M");
        ws.Dir("profiles/M/modules/app_config");
        var r = new MasterTargetResolver(new StubWorkspace(ws.Root), ws.Resolver());

        Assert.Equal("M",         r.DataSetFor("M", "app_config"));
        Assert.Equal("M_sysprep", r.DataSetFor("M", "sysprep_config"));
        Assert.Equal("M_sysprep", r.SysprepDataSet("M"));
        Assert.Equal("standard",  r.FindModuleKind("app_config"));
        Assert.Null(r.FindModuleKind("nope"));

        Assert.Equal(ws.Abs("profiles/M/modules/app_config/file/x.exe"), r.ResolveWrite("app_config", "file/x.exe", "M")!.AbsPath);
        Assert.Equal(ws.Abs("modules/standard/app_config/app_list.csv"),  r.ResolveRead("app_config", "app_list.csv", "M")!.AbsPath);   // PDF に無ければ本体
        Assert.Equal(ws.Abs("modules/standard/app_config/app_list.csv"),  r.ResolveRead("app_config", "app_list.csv", null)!.AbsPath);
        Assert.Null(r.ResolveWrite("nope", "a.csv", "M"));
        Assert.Equal(ws.Abs("modules/standard/app_config/app_list.csv"),  r.GetModuleCsvPath("app_config", "app_list.csv"));
        Assert.Equal(["M"], r.ListDataSets().ToArray());
        Assert.Equal(["app_config"], r.ListDataSetModules("M").ToArray());
        Assert.False(Directory.Exists(ws.Abs("profiles/M/modules/app_config/file")), "解決だけではフォルダを作らない");
    }

    [Fact]
    public async Task Answers_MigrateLegacyToDataFolder()
    {
        using var ws = new TempWorkspace();
        ws.File("profiles/M_old.master.json", "{\"masterName\":\"M_old\",\"values\":{\"a\":\"1\"}}");
        var svc = new MasterAnswersService(new StubWorkspace(ws.Root));

        Assert.True(svc.Exists("M_old"));
        Assert.Contains("M_old", await svc.ListMasterNamesAsync());
        Assert.Equal(ws.Abs("profiles/M_old/master.json"), svc.GetAnswersPath("M_old"));

        var a = await svc.LoadAsync("M_old");
        Assert.NotNull(a);
        await svc.SaveAsync(a!);

        Assert.True(File.Exists(ws.Abs("profiles/M_old/master.json")));
        Assert.False(File.Exists(ws.Abs("profiles/M_old.master.json")), "保存時に旧置き場から移す");
        Assert.Equal(["M_old"], (await svc.ListMasterNamesAsync()).ToArray());
        Assert.Equal("1", (await svc.LoadAsync("M_old"))!.Values["a"]);
    }
}
