using System.IO;
using FabriqStudio.Services;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>Pianist Profile の profiles/ フォルダ（フォルダ単位 all-or-nothing）を編集先データセットへ取り込む経路。</summary>
public sealed class PianistDataSetTests
{
    [Fact]
    public async Task Materialize_CopiesProfilesFolderAndList_OnlyWhenMissing()
    {
        using var ws = new TempWorkspace();
        ws.File("modules/extended/pianist/profiles/A/pianist.json", "{}");
        ws.File("modules/extended/pianist/profiles/A/instructions/1.txt", "x");
        ws.File("modules/extended/pianist/pianist_list.csv", "Enabled,ProfileName,Group,Description,Segment\r\n1,A,,,\r\n");
        var workspace = new StubWorkspace(ws.Root);
        var ctx = new DataSetContext(workspace);
        var svc = new PianistProfileService(workspace, new CsvService(workspace), ws.Resolver(), ctx);

        Assert.False(svc.IsDataFolderFallback);   // データセット未選択 = 本体
        Assert.Equal("modules/extended/pianist/profiles/", svc.ProfilesDirLabel);

        ctx.Set("Cust_A");
        Assert.True(svc.IsDataFolderFallback);     // PDF に pianist/profiles/ が無い → 本体を参照
        Assert.Equal(ws.Abs("modules/extended/pianist/profiles/A"), (await svc.GetProfilesAsync()).Single().FolderPath);

        var copied = await svc.MaterializePianistDataAsync();
        Assert.Equal(3, copied);
        Assert.False(svc.IsDataFolderFallback);
        Assert.True(File.Exists(ws.Abs("profiles/Cust_A/modules/pianist/profiles/A/instructions/1.txt")));
        Assert.True(File.Exists(ws.Abs("profiles/Cust_A/modules/pianist/pianist_list.csv")));
        Assert.StartsWith(ws.Abs("profiles/Cust_A"), (await svc.GetProfilesAsync()).Single().FolderPath);
        Assert.Equal("profiles/Cust_A/modules/pianist/profiles/", svc.ProfilesDirLabel);

        Assert.Equal(0, await svc.MaterializePianistDataAsync());   // 2 回目は何もしない

        ctx.Set(null);
        Assert.False(svc.IsDataFolderFallback);
        Assert.Equal(ws.Abs("modules/extended/pianist/profiles/A"), (await svc.GetProfilesAsync()).Single().FolderPath);
    }

    [Fact]
    public void DataSetContext_ResetsOnWorkspaceSwitch_AndRaisesChanged()
    {
        var workspace = new StubWorkspace(@"C:\ws");
        var ctx = new DataSetContext(workspace);
        var raised = 0;
        ctx.Changed += (_, _) => raised++;

        ctx.Set("  Cust_A ");
        Assert.Equal("Cust_A", ctx.Current);
        ctx.Set("Cust_A");                 // 同じ値は発火しない
        Assert.Equal(1, raised);
        ctx.Set("");
        Assert.Null(ctx.Current);
        Assert.Equal(2, raised);
    }
}
