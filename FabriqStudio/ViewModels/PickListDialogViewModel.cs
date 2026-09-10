using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 選択ダイアログ: 固定一覧の絞り込み、または検索。チェックした項目は検索し直しても保持する
/// （Chrome を選んでから firefox を検索して Firefox も選ぶ、ができる）。
/// </summary>
public sealed partial class PickListDialogViewModel : ObservableObject
{
    private readonly PickListRequest _req;
    private readonly List<PickListItem> _all = [];
    private readonly Dictionary<string, PickListItem> _picked = new(StringComparer.OrdinalIgnoreCase);

    public PickListDialogViewModel(PickListRequest req)
    {
        _req = req;
        if (req.Items is not null) Replace(req.Items);
    }

    public string Title       => _req.Title;
    public bool   HasSearch   => _req.Search is not null;
    public string InputHint   => _req.InputHint ?? (HasSearch ? "検索語を入力して Enter" : "絞り込み");
    public string ConfirmText => _req.ConfirmText;

    public ObservableCollection<PickListItem> Items { get; } = [];

    [ObservableProperty] private string  _query = "";
    [ObservableProperty] private bool    _isSearching;
    [ObservableProperty] private string? _message;

    public int  SelectedCount => _picked.Count;
    public bool CanConfirm    => _picked.Count > 0;

    /// <summary>ダイアログの結果（チェックした項目。検索し直す前のものも含む）。</summary>
    public IReadOnlyList<PickListItem> Selected => _picked.Values.ToList();

    partial void OnQueryChanged(string value)
    {
        if (!HasSearch) ApplyFilter();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_req.Search is null) return;
        var q = Query.Trim();
        if (q.Length == 0) return;

        IsSearching = true;
        Message     = null;
        try
        {
            var results = await _req.Search(q);
            Replace(results);
            Message = results.Count == 0 ? "見つかりません" : null;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void Replace(IReadOnlyList<PickListItem> items)
    {
        foreach (var old in _all) old.PropertyChanged -= OnItemChanged;
        _all.Clear();
        foreach (var it in items)
        {
            // 前回チェックしたものが再び出てきたらチェック状態を引き継ぐ
            if (_picked.TryGetValue(it.Key, out var prev) && !ReferenceEquals(prev, it))
            {
                it.IsChecked = true;
                _picked[it.Key] = it;
            }
            it.PropertyChanged += OnItemChanged;
            _all.Add(it);
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = HasSearch ? "" : Query.Trim();
        Items.Clear();
        foreach (var it in _all)
        {
            if (q.Length > 0
                && !it.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !it.Detail.Contains(q, StringComparison.OrdinalIgnoreCase))
                continue;
            Items.Add(it);
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PickListItem.IsChecked) || sender is not PickListItem it) return;
        if (it.IsChecked) _picked[it.Key] = it;
        else _picked.Remove(it.Key);
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanConfirm));
    }
}
