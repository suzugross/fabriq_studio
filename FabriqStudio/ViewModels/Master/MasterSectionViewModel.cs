using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FabriqStudio.Models.Master;

namespace FabriqStudio.ViewModels.Master;

/// <summary>章の中の設定ジャンル（テンプレートの subgroup が連続する項目のまとまり）。1 枚のカードとして表示する。</summary>
public sealed class MasterBlockViewModel
{
    public MasterBlockViewModel(string? title, IReadOnlyList<MasterItemViewModel> items)
    {
        Title = title ?? "";
        Items = items;
    }

    public string Title    { get; }
    public bool   HasTitle => Title.Length > 0;
    public IReadOnlyList<MasterItemViewModel> Items { get; }
}

/// <summary>
/// 画面の 1 章（左レールの項目と、中央に 1 章ずつ表示するページ）。
/// 既定値から変えた項目数（<see cref="ModifiedCount"/>）を持ち、レールに出す。
/// </summary>
public sealed partial class MasterSectionViewModel : ObservableObject
{
    public MasterSectionViewModel(MasterSection section, IReadOnlyList<MasterItemViewModel> items, int index)
    {
        Section = section;
        Items   = items;
        Index   = index;
        Blocks  = BuildBlocks(items);

        foreach (var item in items)
            item.PropertyChanged += OnItemPropertyChanged;
        RefreshModifiedCount();
    }

    public MasterSection Section { get; }
    public IReadOnlyList<MasterItemViewModel>  Items  { get; }
    public IReadOnlyList<MasterBlockViewModel> Blocks { get; }

    /// <summary>1 始まりの章番号（レール表示・前後移動用）。</summary>
    public int Index { get; }

    public string  Id             => Section.Id;
    public string  Title          => Section.Title;
    public string  Group          => Section.Group;
    public string? Description    => Section.Description;
    public bool    HasDescription => !string.IsNullOrWhiteSpace(Section.Description);
    public bool    HasGroup       => !string.IsNullOrWhiteSpace(Section.Group);

    /// <summary>表示中の項目のうち、既定値から変更されたものの数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModified))]
    private int _modifiedCount;

    public bool HasModified => ModifiedCount > 0;

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MasterItemViewModel.IsModified) or nameof(MasterItemViewModel.IsVisible))
            RefreshModifiedCount();
    }

    public void RefreshModifiedCount()
        => ModifiedCount = Items.Count(i => i.IsVisible && i.IsModified);

    private static List<MasterBlockViewModel> BuildBlocks(IReadOnlyList<MasterItemViewModel> items)
    {
        var blocks  = new List<MasterBlockViewModel>();
        var current = new List<MasterItemViewModel>();
        string? currentTitle = null;
        var first = true;

        foreach (var item in items)
        {
            var title = item.Item.Subgroup;
            if (!first && !string.Equals(title, currentTitle, StringComparison.Ordinal))
            {
                blocks.Add(new MasterBlockViewModel(currentTitle, current));
                current = new List<MasterItemViewModel>();
            }
            currentTitle = title;
            current.Add(item);
            first = false;
        }
        if (current.Count > 0)
            blocks.Add(new MasterBlockViewModel(currentTitle, current));

        return blocks;
    }

    public override string ToString() => Title;
}
