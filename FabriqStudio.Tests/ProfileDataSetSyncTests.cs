using FabriqStudio.Services;
using FabriqStudio.ViewModels;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>
/// 基本パラメータの「実行プロファイル」と編集先データセット（IDataSetContext）の連動。
/// 状態は IDataSetContext が唯一持ち、基本パラメータは選択を押し出す／映すだけ。
/// </summary>
public sealed class ProfileDataSetSyncTests
{
    private const string ProfileHeader = "Order,Enabled,Script,Description,Group\r\n";

    [Fact]
    public async Task SelectedProfile_DrivesAndFollows_DataSetContext()
    {
        using var ws = new TempWorkspace();
        ws.File("profiles/Cust_A.csv", ProfileHeader);
        ws.File("profiles/Cust_B.csv", ProfileHeader);
        var workspace = new StubWorkspace(ws.Root);
        var ctx       = new DataSetContext(workspace);
        var vm        = NewVm(workspace, ctx);
        await WaitProfilesAsync(vm);

        // 起動時: 先頭のプロファイルが選ばれ、編集先も同じになる（既定が「本体」ではなくなる）
        Assert.Equal("Cust_A", vm.SelectedProfile?.Name);
        Assert.Equal("Cust_A", ctx.Current);

        // 基本パラメータで選ぶ → 編集先が追従
        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "Cust_B");
        Assert.Equal("Cust_B", ctx.Current);

        // 編集先（サイドバーのセレクタ）で選ぶ → 実行プロファイルが追従
        ctx.Set("Cust_A");
        Assert.Equal("Cust_A", vm.SelectedProfile?.Name);

        // 「（本体）」は基本パラメータでは表せないので選択を維持し、編集先へ押し返さない
        ctx.Set(null);
        Assert.Equal("Cust_A", vm.SelectedProfile?.Name);
        Assert.Null(ctx.Current);

        // 一覧に無い名前（PDF だけのデータセット）は無視
        ctx.Set("Only_Pdf");
        Assert.Equal("Cust_A", vm.SelectedProfile?.Name);
        Assert.Equal("Only_Pdf", ctx.Current);
    }

    [Fact]
    public async Task NoProfiles_LeavesDataSetOnBody()
    {
        using var ws = new TempWorkspace();
        ws.Dir("profiles");
        var workspace = new StubWorkspace(ws.Root);
        var ctx       = new DataSetContext(workspace);
        var vm        = NewVm(workspace, ctx);
        await WaitProfilesAsync(vm, expectAny: false);

        Assert.Null(vm.SelectedProfile);
        Assert.Null(ctx.Current);
    }

    private static BasicParamsViewModel NewVm(IWorkspaceService workspace, IDataSetContext ctx)
    {
        var csv = new CsvService(workspace);
        return new BasicParamsViewModel(
            csv, new FileService(), new ProfileService(workspace, csv), workspace, new CryptoService(),
            new FabriqBackupService(), new FabriqUpdateService(), ctx);
    }

    /// <summary>コンストラクタの LoadAllAsync（fire-and-forget）が終わるまで待つ。</summary>
    private static async Task WaitProfilesAsync(BasicParamsViewModel vm, bool expectAny = true)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((vm.IsProfilesLoading || (expectAny && vm.Profiles.Count == 0)) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        await Task.Delay(200);   // IsProfilesLoading=false の直後に走る編集先の追従まで待つ
        Assert.False(vm.IsProfilesLoading);
    }
}
