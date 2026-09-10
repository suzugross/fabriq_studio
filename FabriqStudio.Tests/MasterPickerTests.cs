using System.Data;
using FabriqStudio.Models.Master;
using FabriqStudio.ViewModels.Master;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>マスタ設計の picker: タスクバー表の「ファイルを選ぶ」と、ストアアプリの「この PC から選ぶ」。</summary>
public sealed class MasterPickerTests
{
    private static MasterItemContext Ctx(
        Func<string, string?, Task<IReadOnlyList<string>?>>? pickFiles = null,
        Func<IReadOnlyCollection<string>, Task<IReadOnlyList<string>?>>? pickStore = null)
        => new() { OnChanged = () => { }, CanEdit = () => true, PickFiles = pickFiles, PickStoreApps = pickStore };

    [Fact]
    public async Task TablePicker_AddsLinkPathRows_WithDescription()
    {
        var item = new MasterItem
        {
            Id = "tb_pins", Type = "table", Picker = "lnk",
            Columns =
            [
                new MasterColumn { Name = "Kind", Options = ["AppId", "LinkPath"], Default = "AppId" },
                new MasterColumn { Name = "Value" },
                new MasterColumn { Name = "Description" },
            ],
        };
        var vm = new TableItemViewModel(item, Ctx(pickFiles: (_, _) => Task.FromResult<IReadOnlyList<string>?>(
            [@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk"])));

        Assert.True(vm.HasFilePicker);
        await vm.PickFilesCommand.ExecuteAsync(null);

        var rows = vm.Table.Rows.Cast<DataRow>().ToList();
        Assert.Single(rows);
        Assert.Equal("LinkPath", rows[0]["Kind"]);
        Assert.Equal(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk", rows[0]["Value"]);
        Assert.Equal("Google Chrome", rows[0]["Description"]);
    }

    [Fact]
    public async Task TablePicker_Cancel_AddsNothing()
    {
        var item = new MasterItem { Id = "tb_pins", Type = "table", Picker = "lnk", Columns = [new MasterColumn { Name = "Value" }] };
        var vm   = new TableItemViewModel(item, Ctx(pickFiles: (_, _) => Task.FromResult<IReadOnlyList<string>?>(null)));

        await vm.PickFilesCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.RowCount);
    }

    [Fact]
    public async Task MultiPicker_ChecksKnownOption_AndAddsUnknownAsFree()
    {
        var item = new MasterItem
        {
            Id = "sp_storeapps", Type = "multi", Picker = "storeApps", AllowFree = true,
            Options = [new MasterChoice { Value = "Microsoft.BingNews", Label = "Bing News" }],
        };
        IReadOnlyCollection<string>? seen = null;
        var vm = new MultiItemViewModel(item, Ctx(pickStore: existing =>
        {
            seen = existing;
            return Task.FromResult<IReadOnlyList<string>?>(["Microsoft.BingNews", "Contoso.App"]);
        }));

        Assert.True(vm.HasPcPicker);
        await vm.PickFromPcCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.True(vm.Options[0].IsChecked);
        Assert.Contains("Contoso.App", vm.FreeEntries);
        Assert.Equal(2, vm.SelectedCount);
    }

    [Fact]
    public void NoPicker_WhenTemplateHasNone()
    {
        var table = new TableItemViewModel(new MasterItem { Id = "x", Type = "table", Columns = [new MasterColumn { Name = "A" }] }, Ctx());
        var multi = new MultiItemViewModel(new MasterItem { Id = "y", Type = "multi" }, Ctx());
        Assert.False(table.HasFilePicker);
        Assert.False(multi.HasPcPicker);
    }
}
