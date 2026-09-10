using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>選択ダイアログ（PickListDialog）の 1 行。</summary>
public sealed partial class PickListItem : ObservableObject
{
    public PickListItem(string name, string detail, string detail2, bool isRegistered)
    {
        Name         = name;
        Detail       = detail;
        Detail2      = detail2;
        IsRegistered = isRegistered;
    }

    public string Name    { get; }
    public string Detail  { get; }
    public string Detail2 { get; }

    /// <summary>既に CSV に登録済み（選べない）。</summary>
    public bool   IsRegistered   { get; }
    public bool   CanCheck       => !IsRegistered;
    public string RegisteredText => IsRegistered ? "登録済み" : "";

    /// <summary>呼び出し側が元データを結び付ける。</summary>
    public object? Tag { get; init; }

    [ObservableProperty] private bool _isChecked;

    /// <summary>同じものかどうかの鍵（Detail = Id があればそれ、無ければ名前）。</summary>
    public string Key => Detail.Length > 0 ? Detail : Name;
}

/// <summary>
/// 選択ダイアログの依頼。<paramref name="Items"/> があれば固定一覧（入力欄は絞り込み）、
/// <paramref name="Search"/> があれば検索（入力欄 + Enter / 検索ボタン）。
/// </summary>
public sealed record PickListRequest(
    string Title,
    IReadOnlyList<PickListItem>? Items = null,
    Func<string, Task<IReadOnlyList<PickListItem>>>? Search = null,
    string? InputHint = null,
    string ConfirmText = "追加");
