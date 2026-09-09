using System.IO;
using FabriqStudio.Services;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>
/// fabriq のプロファイル別データオーバーレイ（PDF）の解決規則を Studio 側がミラーできているか。
/// 期待値は E:\fabriq\dev\PROFILE_DATA_OVERLAY_FOR_TOOLING.md §2（写像）/ §3（粒度）/ §6（書き込み）。
/// </summary>
public sealed class ModuleDataResolverTests
{
    // ── §2 写像 ──────────────────────────────────────────────────

    [Theory]
    [InlineData(@"modules\standard\gpo_config\gpo_list.csv",     "profiles/Cust_A/modules/gpo_config/gpo_list.csv")]
    [InlineData(@"modules\standard\printer_driver_config\INF\",  "profiles/Cust_A/modules/printer_driver_config/INF")]
    [InlineData(@"modules\extended\pianist\profiles\",           "profiles/Cust_A/modules/pianist/profiles")]
    [InlineData("modules/standard/gpo_config",                   "profiles/Cust_A/modules/gpo_config")]
    public void Map_DropsTier_ForBriefExamples(string body, string expected)
    {
        using var ws = new TempWorkspace();
        Assert.Equal(expected, ws.Resolver().MapToDataSet(body, "Cust_A"));
    }

    [Theory]
    [InlineData(@"kernel\csv\hostlist.csv", "kernel/csv/hostlist.csv")]   // modules 配下以外は写像しない（§5 hostlist は恒久対象外）
    [InlineData("modules/standard",         "modules/standard")]           // <module> が無い形は干渉しない
    [InlineData("profiles/X.csv",           "profiles/X.csv")]
    public void Map_IsIdentity_OutsideModules(string rel, string expected)
    {
        using var ws = new TempWorkspace();
        Assert.Equal(expected, ws.Resolver().MapToDataSet(rel, "Cust_A"));
    }

    [Fact]
    public void Map_IsIdentity_WhenNoDataSet()
    {
        using var ws = new TempWorkspace();
        var r = ws.Resolver();
        Assert.Equal("modules/standard/gpo_config/gpo_list.csv", r.MapToDataSet(@"modules\standard\gpo_config\gpo_list.csv", null));
        Assert.Equal("modules/standard/gpo_config/gpo_list.csv", r.MapToDataSet(@"modules\standard\gpo_config\gpo_list.csv", "  "));
    }

    [Fact]
    public void Map_NormalizesDotSegments()
    {
        using var ws = new TempWorkspace();
        Assert.Equal("profiles/A/modules/gpo_config/gpo_list.csv",
            ws.Resolver().MapToDataSet(@"modules\standard\gpo_config\sub\..\.\gpo_list.csv", "A"));
    }

    // ── §3 粒度: 設定 CSV = ファイル単位 ─────────────────────────

    [Fact]
    public void ResolveRead_File_ProfileWhenPresent_FallbackWhenAbsent_IdentityWithoutDataSet()
    {
        using var ws = new TempWorkspace();
        var body = ws.File("modules/standard/gpo_config/gpo_list.csv");
        var pdf  = ws.File("profiles/Cust_A/modules/gpo_config/gpo_list.csv");
        var r    = ws.Resolver();

        var adopted = r.ResolveRead("modules/standard/gpo_config/gpo_list.csv", "Cust_A");
        Assert.Equal(ModuleDataSource.Profile, adopted.Source);
        Assert.Equal(pdf, adopted.AbsPath);
        Assert.Equal("profiles/Cust_A/modules/gpo_config/gpo_list.csv", adopted.RelPath);

        var fallback = r.ResolveRead("modules/standard/gpo_config/gpo_list.csv", "Cust_B");
        Assert.Equal(ModuleDataSource.Fallback, fallback.Source);
        Assert.Equal(body, fallback.AbsPath);

        var identity = r.ResolveRead("modules/standard/gpo_config/gpo_list.csv", null);
        Assert.Equal(ModuleDataSource.Identity, identity.Source);
        Assert.Equal(body, identity.AbsPath);
    }

    // ── §3 粒度: 資材フォルダ = フォルダ単位（空でも PDF が勝つ）──

    [Fact]
    public void ResolveRead_Folder_EmptyPdfFolderWins_AndNestedFilesAreNotBackfilled()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/printer_driver_config/INF/epson/e.inf", "[Version]");
        ws.Dir("profiles/Cust_A/modules/printer_driver_config/INF");   // 空フォルダ = 資材ゼロの明示
        var r = ws.Resolver();

        var folder = r.ResolveRead("modules/standard/printer_driver_config/INF", "Cust_A");
        Assert.Equal(ModuleDataSource.Profile, folder.Source);

        // フォルダが PDF にある以上、その配下のファイルは本体で穴埋めされない（存在しない PDF 側パスが返る）
        var nested = r.ResolveRead("modules/standard/printer_driver_config/INF/epson/e.inf", "Cust_A");
        Assert.Equal(ModuleDataSource.Profile, nested.Source);
        Assert.False(File.Exists(nested.AbsPath));
        Assert.StartsWith(ws.Abs("profiles/Cust_A"), nested.AbsPath);

        // PDF にフォルダが無ければ本体（フォールバック）
        var other = r.ResolveRead("modules/standard/printer_driver_config/INF/epson/e.inf", "Cust_B");
        Assert.Equal(ModuleDataSource.Fallback, other.Source);
        Assert.True(File.Exists(other.AbsPath));
    }

    // ── §3 粒度: reg_*_list*.csv = モジュール単位 ──────────────────

    [Fact]
    public void ResolveRead_RegList_IsModuleLevelAllOrNothing()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list.csv");
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list02.csv");
        ws.File("modules/standard/reg_hklm_config/other.csv");
        ws.File("profiles/Cust_A/modules/reg_hklm_config/reg_hklm_list_M1.csv");   // PDF には別名の 1 件だけ
        var r = ws.Resolver();

        // PDF 側に 1 件でもあれば PDF 側のみ → 本体の reg_hklm_list.csv は読まれない（存在しない PDF 側パス）
        var reg = r.ResolveRead("modules/standard/reg_hklm_config/reg_hklm_list.csv", "Cust_A");
        Assert.Equal(ModuleDataSource.Profile, reg.Source);
        Assert.False(File.Exists(reg.AbsPath));

        // 列挙系でない CSV はファイル単位のまま
        var other = r.ResolveRead("modules/standard/reg_hklm_config/other.csv", "Cust_A");
        Assert.Equal(ModuleDataSource.Fallback, other.Source);
    }

    [Fact]
    public void EnumerateCsvs_ComposesLikeKernel()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list.csv");
        ws.File("modules/standard/reg_hklm_config/reg_hklm_list02.csv");
        ws.File("modules/standard/reg_hklm_config/other.csv");
        ws.File("modules/standard/reg_hklm_config/module.csv", "MenuName,Category,Script,Order,Enabled\r\n");
        ws.File("modules/standard/reg_hklm_config/preset.csv", "Column,Value,Label\r\n");
        ws.File("modules/standard/reg_hklm_config/notes.csvbak");
        ws.File("profiles/Cust_A/modules/reg_hklm_config/reg_hklm_list_M1.csv");
        var r = ws.Resolver();

        var composed = r.EnumerateCsvs("reg_hklm_config", "Cust_A");
        Assert.Equal(["other.csv", "reg_hklm_list_M1.csv"], composed.Select(e => e.Name).ToArray());
        Assert.Equal(ModuleDataSource.Fallback, composed.Single(e => e.Name == "other.csv").Source);
        Assert.Equal(ModuleDataSource.Profile,  composed.Single(e => e.Name == "reg_hklm_list_M1.csv").Source);

        var plain = r.EnumerateCsvs("modules/standard/reg_hklm_config", null);
        Assert.Equal(["other.csv", "reg_hklm_list.csv", "reg_hklm_list02.csv"], plain.Select(e => e.Name).ToArray());
        Assert.All(plain, e => Assert.Equal(ModuleDataSource.Identity, e.Source));
    }

    // ── §6 書き込み ──────────────────────────────────────────────

    [Fact]
    public void ResolveWrite_TargetsPdf_WithoutCreatingDirectory()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/gpo_config/gpo_list.csv");
        var r = ws.Resolver();

        var w = r.ResolveWrite("modules/standard/gpo_config/gpo_list.csv", "Cust_A");
        Assert.Equal(ModuleDataSource.Profile, w.Source);
        Assert.Equal(ws.Abs("profiles/Cust_A/modules/gpo_config/gpo_list.csv"), w.AbsPath);
        Assert.False(Directory.Exists(ws.Abs("profiles/Cust_A")), "解決しただけでディレクトリを作ってはいけない（空フォルダが all-or-nothing を発動させる）");

        var body = r.ResolveWrite("modules/standard/gpo_config/gpo_list.csv", null);
        Assert.Equal(ModuleDataSource.Identity, body.Source);
        Assert.Equal(ws.Abs("modules/standard/gpo_config/gpo_list.csv"), body.AbsPath);

        var outside = r.ResolveWrite("kernel/csv/hostlist.csv", "Cust_A");
        Assert.Equal(ModuleDataSource.Identity, outside.Source);
    }

    // ── 上書き一覧（Advanced のモジュール編集画面用）──────────────

    [Fact]
    public void FindOverrides_ListsCsvsAndFolders_IgnoringFrameworkAssets()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/standard/printer_driver_config/printer_driver_list.csv");
        ws.File("profiles/Cust_A/modules/printer_driver_config/printer_driver_list.csv");
        ws.File("profiles/Cust_A/modules/printer_driver_config/module.csv");    // フレームワーク資産は無視
        ws.Dir ("profiles/Cust_A/modules/printer_driver_config/INF");
        ws.Dir ("profiles/Cust_A/modules/printer_driver_config/tools");         // フレームワーク資産は無視
        ws.Dir ("profiles/Cust_B/modules/gpo_config");                          // 別モジュールだけ
        ws.Dir ("profiles/Cust_C/modules/printer_driver_config");               // 中身が無ければ上書きではない
        ws.File("profiles/Cust_D.csv", "Order,ScriptPath\r\n");                 // プロファイル定義だけ（PDF 無し）
        var r = ws.Resolver();

        var overrides = r.FindOverrides("printer_driver_config");
        var only = Assert.Single(overrides);
        Assert.Equal("Cust_A", only.DataSet);
        Assert.Equal(["printer_driver_list.csv"], only.Csvs.ToArray());
        Assert.Equal(["INF"], only.Folders.ToArray());

        Assert.Empty(r.FindOverrides("wallpaper_config"));
        Assert.Empty(r.FindOverrides(""));
    }

    [Fact]
    public void FindOverrides_IsEmpty_WhenWorkspaceClosed()
    {
        var r = new ModuleDataResolver(new StubWorkspace(null!));
        Assert.Empty(r.FindOverrides("gpo_config"));
    }

    // ── パス組み立て ─────────────────────────────────────────────

    [Fact]
    public void ModuleRelPath_SearchesStandardThenExtended()
    {
        using var ws = new TempWorkspace();
        ws.Dir("modules/standard/gpo_config");
        ws.Dir("modules/extended/pianist");
        var r = ws.Resolver();

        Assert.Equal("modules/standard/gpo_config/gpo_list.csv", r.ModuleRelPath("gpo_config", "gpo_list.csv"));
        Assert.Equal("modules/extended/pianist/profiles",       r.ModuleRelPath("pianist", @"profiles\"));
        Assert.Equal("modules/extended/pianist",                r.ModuleRelPath("modules/extended/pianist"));
        Assert.Null(r.ModuleRelPath("nope"));
        Assert.Equal("modules/standard/x/a/b.csv", r.ModuleRelPath("standard", "x", @"a\b.csv"));
    }
}
