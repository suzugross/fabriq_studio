using System.IO;
using FabriqStudio.Services;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>PDF への取り込み（materialize）が「実行結果を変えない」規則を守っているか。</summary>
public sealed class ProfileDataServiceTests
{
    private static ProfileDataService Svc(TempWorkspace ws) => new(ws.Resolver());

    [Fact]
    public async Task MaterializeModule_CopiesBodyCsvsAsIs_SkipsFrameworkAssets_AndExisting()
    {
        using var ws = new TempWorkspace();
        var bodyList = ws.File("modules/standard/gpo_config/gpo_list.csv", "﻿Enabled,Policy\r\n1,A\r\n");   // BOM + CRLF をそのまま
        ws.File("modules/standard/gpo_config/module.csv", "MenuName\r\n");
        ws.File("modules/standard/gpo_config/preset.csv", "Column\r\n");
        ws.File("modules/standard/gpo_config/extra.csv",  "X\r\n1\r\n");
        ws.File("profiles/Cust_A/modules/gpo_config/extra.csv", "X\r\n9\r\n");     // 既にある → 触らない
        var svc = Svc(ws);

        var copied = await svc.MaterializeModuleAsync("Cust_A", "gpo_config");

        Assert.Equal(["gpo_list.csv"], copied.ToArray());
        Assert.Equal(File.ReadAllBytes(bodyList), File.ReadAllBytes(ws.Abs("profiles/Cust_A/modules/gpo_config/gpo_list.csv")));
        Assert.Equal("X\r\n9\r\n", File.ReadAllText(ws.Abs("profiles/Cust_A/modules/gpo_config/extra.csv")));
        Assert.False(File.Exists(ws.Abs("profiles/Cust_A/modules/gpo_config/module.csv")));
        Assert.False(File.Exists(ws.Abs("profiles/Cust_A/modules/gpo_config/preset.csv")));
    }

    [Fact]
    public async Task MaterializeModule_DoesNothing_WhenModuleHasNoCsv_AndCreatesNoFolder()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/hostname_config/hostname_config.ps1", "# ps");
        var svc = Svc(ws);

        var copied = await svc.MaterializeModuleAsync("Cust_A", "hostname_config");

        Assert.Empty(copied);
        Assert.False(Directory.Exists(ws.Abs("profiles/Cust_A")), "コピーするものが無ければフォルダを作らない");
    }

    [Fact]
    public async Task MaterializeModule_SkipsBodyRegLists_WhenPdfAlreadyHasOne()
    {
        // PDF に reg_* が 1 件でもあれば本体の reg_* は読まれていない。取り込むと本体のサンプル行が動き出す → 触らない
        using var ws = new TempWorkspace();
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list.csv");
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list02.csv");
        ws.File("modules/standard/reg_hklm_config/other.csv");
        ws.File("profiles/Cust_A/modules/reg_hklm_config/reg_hklm_list_M1.csv");
        var svc = Svc(ws);

        var copied = await svc.MaterializeModuleAsync("Cust_A", "reg_hklm_config");

        Assert.Equal(["other.csv"], copied.ToArray());
        Assert.False(File.Exists(ws.Abs("profiles/Cust_A/modules/reg_hklm_config/reg_hklm_list.csv")));
    }

    [Fact]
    public async Task MaterializeCsv_RegList_ExpandsToWholeModule_WhenPdfHasNone()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list.csv");
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list02.csv");
        ws.File("modules/standard/reg_hklm_config/other.csv");
        var svc = Svc(ws);

        var copied = await svc.MaterializeCsvAsync("Cust_A", "reg_hklm_config", "reg_hklm_list02.csv");

        Assert.Equal(["reg_hklm_list.csv", "reg_hklm_list02.csv"], copied.OrderBy(x => x).ToArray());
        Assert.False(File.Exists(ws.Abs("profiles/Cust_A/modules/reg_hklm_config/other.csv")), "通常 CSV は巻き込まない");
    }

    [Fact]
    public async Task MaterializeCsv_PlainCsv_CopiesOnlyThatFile()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/printer_driver_config/printer_driver_list.csv");
        ws.File("modules/standard/printer_driver_config/printer_list.csv");
        ws.Dir ("modules/standard/printer_driver_config/INF");
        var svc = Svc(ws);

        var copied = await svc.MaterializeCsvAsync("Cust_A", "printer_driver_config", "printer_list.csv");

        Assert.Equal(["printer_list.csv"], copied.ToArray());
        Assert.False(File.Exists(ws.Abs("profiles/Cust_A/modules/printer_driver_config/printer_driver_list.csv")));
        Assert.False(Directory.Exists(ws.Abs("profiles/Cust_A/modules/printer_driver_config/INF")), "資材フォルダは触らない");
    }

    [Fact]
    public void FindMissingModules_ReportsModulesWithUnimportedCsvs_UsingRegRule()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/gpo_config/gpo_list.csv");
        ws.File("modules/standard/wallpaper_config/wallpaper_list.csv");
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list.csv");
        ws.File("modules/standard/reg_hklm_config/other.csv");
        ws.File("modules/standard/hostname_config/hostname_config.ps1", "# no csv");
        ws.File("profiles/Cust_A/modules/gpo_config/gpo_list.csv");
        ws.File("profiles/Cust_A/modules/reg_hklm_config/reg_hklm_list_M1.csv");
        ws.File("profiles/Cust_A/modules/reg_hklm_config/other.csv");
        var svc = Svc(ws);

        var missing = svc.FindMissingModules("Cust_A", ["gpo_config", "wallpaper_config", "reg_hklm_config", "hostname_config", "nope", "gpo_config"]);

        Assert.Equal(["wallpaper_config"], missing.ToArray());   // reg_* は PDF 側にあるので本体の reg_hklm_list.csv は未取り込み扱いにしない
        Assert.Equal(["gpo_config", "wallpaper_config", "reg_hklm_config"], svc.FindMissingModules("Cust_B", ["gpo_config", "wallpaper_config", "reg_hklm_config"]).ToArray());
    }

    [Fact]
    public void FindUnusedModules_And_Delete()
    {
        using var ws = new TempWorkspace();
        ws.File("profiles/Cust_A/modules/gpo_config/gpo_list.csv");
        ws.File("profiles/Cust_A/modules/wallpaper_config/wallpaper_list.csv");
        ws.Dir ("profiles/Cust_A/modules/wallpaper_config/wallpaper");
        var svc = Svc(ws);

        Assert.Equal(["wallpaper_config"], svc.FindUnusedModules("Cust_A", ["gpo_config"]).ToArray());
        Assert.Empty(svc.FindUnusedModules("Cust_B", ["gpo_config"]));

        svc.DeleteModuleData("Cust_A", "wallpaper_config");
        Assert.False(Directory.Exists(ws.Abs("profiles/Cust_A/modules/wallpaper_config")));
        Assert.True(File.Exists(ws.Abs("profiles/Cust_A/modules/gpo_config/gpo_list.csv")), "他モジュールは無傷");

        svc.DeleteModuleData("Cust_A", "");   // 空は何もしない
        Assert.True(Directory.Exists(ws.Abs("profiles/Cust_A/modules")));
    }

    [Fact]
    public void HasDataFolder_ReflectsDisk()
    {
        using var ws = new TempWorkspace();
        var svc = Svc(ws);
        Assert.False(svc.HasDataFolder("Cust_A"));
        ws.Dir("profiles/Cust_A");
        Assert.True(svc.HasDataFolder("Cust_A"));
        Assert.False(svc.HasDataFolder(""));
    }
}
