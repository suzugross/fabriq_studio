using FabriqStudio.Models.Gpo;

namespace FabriqStudio.Services.Gpo;

/// <summary>検索条件。</summary>
public sealed class GpoSearchQuery
{
    public string  Text          { get; init; } = "";
    /// <summary>"" = すべて / Machine / User（Both のポリシーはどちらにも該当）。</summary>
    public string  Scope         { get; init; } = "";
    public string? TopCategory   { get; init; }
    public bool    FavoritesOnly { get; init; }
    public int     Limit         { get; init; } = 500;
}

public sealed class GpoSearchResult
{
    public IReadOnlyList<GpoPolicy> Items        { get; init; } = [];
    public int                      TotalMatches { get; init; }
}

/// <summary>
/// GPO 辞書（ADMX/ADML から生成）の読み込み・検索。ワークスペース非依存。
/// 読み込みは初回利用時に 1 回だけ非同期で行い、以後は不変のカタログを共有する。
/// </summary>
public interface IGpoCatalogService
{
    GpoCatalog? Catalog   { get; }
    bool        IsLoaded  { get; }
    bool        IsLoading { get; }
    string?     LoadError { get; }

    /// <summary>現在の ADMX フォルダー（設定ファイルの上書き → 同梱 → C:\Windows\PolicyDefinitions の順で決まる）。</summary>
    string SourcePath        { get; }
    string DefaultSourcePath { get; }

    IReadOnlyList<GpoFavorite> Favorites { get; }

    /// <summary>未読込なら読み込む（同時に呼ばれても読み込みは 1 回）。失敗は <see cref="LoadError"/> に入り、例外にはしない。</summary>
    Task EnsureLoadedAsync();

    /// <summary>読み直す。<paramref name="sourcePath"/> を指定するとそのフォルダーに切り替えて記憶する。</summary>
    Task ReloadAsync(string? sourcePath = null);

    GpoPolicy? FindPolicy(string? id);

    GpoSearchResult Search(GpoSearchQuery query);

    /// <summary>読み込みの開始・完了・失敗で発火（UI スレッドとは限らない）。</summary>
    event EventHandler? CatalogChanged;
}
