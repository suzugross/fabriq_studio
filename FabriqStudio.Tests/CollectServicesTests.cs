using FabriqStudio.Services.Collect;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>採取サービスの純粋関数部分（fabriq 側の規則との一致を固定する）。</summary>
public sealed class CollectServicesTests
{
    // ── ドライバ: fabriq driver_config の Get-SafeModelName と同じ正規化 ──

    [Theory]
    [InlineData("HP EliteBook 840 G8", "HP_EliteBook_840_G8")]
    [InlineData("VMware20,1", "VMware20,1")]
    [InlineData("  Dell/Latitude: 5420? ", "DellLatitude_5420")]
    [InlineData("Model.", "Model")]
    [InlineData("", "Unknown_Model")]
    [InlineData("   ", "Unknown_Model")]
    public void SafeModelName_MatchesFabriqRule(string raw, string expected)
        => Assert.Equal(expected, DriverExportService.SafeModelName(raw));

    [Fact]
    public void SafeModelName_TruncatesTo80()
    {
        var name = new string('A', 100);
        Assert.Equal(80, DriverExportService.SafeModelName(name).Length);
    }

    // ── タスクバー: ユーザープロファイル配下は環境変数に置き換える ──

    private static readonly (string, string)[] Env =
    [
        ("LOCALAPPDATA", @"C:\Users\admin\AppData\Local"),
        ("APPDATA",      @"C:\Users\admin\AppData\Roaming"),
        ("USERPROFILE",  @"C:\Users\admin"),
    ];

    [Theory]
    [InlineData(@"C:\Users\admin\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\App.lnk", @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\App.lnk")]
    [InlineData(@"C:\Users\admin\AppData\Local\Programs\Tool\tool.exe", @"%LOCALAPPDATA%\Programs\Tool\tool.exe")]
    [InlineData(@"C:\Users\admin\Desktop\x.lnk", @"%USERPROFILE%\Desktop\x.lnk")]
    [InlineData(@"C:\Users\administrator\Desktop\x.lnk", @"C:\Users\administrator\Desktop\x.lnk")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk")]
    public void TaskbarLinkPath_ToPortable(string input, string expected)
        => Assert.Equal(expected, TaskbarLinkPath.ToPortable(input, Env));

    // ── winget search の表を列位置（表示幅）で切る ──

    [Fact]
    public void WingetParse_HandlesMatchColumnAndSpacesInName()
    {
        const string output =
            "Name                 Id                             Version    Match\r\n" +
            "---------------------------------------------------------------------------------------\r\n" +
            "AkelPad              AkelPad.AkelPad                4.9.9      Tag: notepad\r\n" +
            "NoteTab Light        FookesHolding.NoteTabLight     7.2        Tag: notepad\r\n";

        var list = WingetSearchService.Parse(output);

        Assert.Equal(2, list.Count);
        Assert.Equal(new WingetPackage("AkelPad", "AkelPad.AkelPad", "4.9.9"), list[0]);
        Assert.Equal(new WingetPackage("NoteTab Light", "FookesHolding.NoteTabLight", "7.2"), list[1]);
    }

    [Fact]
    public void WingetParse_UsesDisplayWidth_ForCjkNames()
    {
        // 列位置: Name=0, Id=40, Version=65, Source=75（ヘッダーは ASCII）
        var header = "Name".PadRight(40) + "Id".PadRight(27) + "Version".PadRight(10) + "Source";
        var cjk    = "一太郎ビューア" + new string(' ', 40 - 14) + "JustSystems.Ichitaro".PadRight(27) + "2024".PadRight(10) + "winget";
        var ascii  = "Microsoft Visual Studio Code".PadRight(40) + "Microsoft.VisualStudioCode".PadRight(27) + "1.136.2".PadRight(10) + "winget";
        var output = header + "\r\n" + new string('-', 85) + "\r\n" + cjk + "\r\n" + ascii + "\r\n";

        var list = WingetSearchService.Parse(output);

        Assert.Equal(2, list.Count);
        Assert.Equal(new WingetPackage("一太郎ビューア", "JustSystems.Ichitaro", "2024"), list[0]);
        Assert.Equal(new WingetPackage("Microsoft Visual Studio Code", "Microsoft.VisualStudioCode", "1.136.2"), list[1]);
    }

    [Fact]
    public void WingetParse_NoHeader_ReturnsEmpty()
        => Assert.Empty(WingetSearchService.Parse("No package found matching input criteria.\r\n"));

    // ── ストアアプリ: ConvertTo-Json の配列 / 単一オブジェクト ──

    [Fact]
    public void StoreAppParse_ArrayAndSingleObject()
    {
        const string array = "[{\"Name\":\"Microsoft.BingNews\",\"Publisher\":\"CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond\",\"Version\":\"1.0\"}," +
                             "{\"Name\":\"Contoso.App\",\"Publisher\":\"CN=Contoso\",\"Version\":\"2.0\"}]";
        const string single = "{\"Name\":\"Contoso.App\",\"Publisher\":\"CN=Contoso\",\"Version\":\"2.0\"}";

        var many = StoreAppInventoryService.Parse(array);
        var one  = StoreAppInventoryService.Parse(single);

        Assert.Equal(2, many.Count);
        Assert.Equal(new StoreApp("Microsoft.BingNews", "Microsoft Corporation", "1.0"), many[0]);
        Assert.Single(one);
        Assert.Equal("Contoso", one[0].Publisher);
        Assert.Empty(StoreAppInventoryService.Parse(""));
    }
}
