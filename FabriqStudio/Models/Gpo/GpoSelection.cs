using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabriqStudio.Models.Gpo;

/// <summary>ポリシーの状態（gpedit の 未構成 / 有効 / 無効）。</summary>
public static class GpoStates
{
    public const string Enabled       = "Enabled";
    public const string Disabled      = "Disabled";
    public const string NotConfigured = "NotConfigured";

    public static string Label(string state) => Normalize(state) switch
    {
        Enabled       => "有効",
        Disabled      => "無効",
        _             => "未構成",
    };

    public static string Normalize(string? state) => state?.Trim() switch
    {
        Enabled       => Enabled,
        Disabled      => Disabled,
        NotConfigured => NotConfigured,
        _             => Enabled,
    };
}

/// <summary>
/// マスタ設計で選んだポリシー 1 件（回答 JSON の tables["gpo_policies"] の 1 行）。
/// ポリシーは ID（ADMX 名:policy 名）で保持し、ADMX の版が変わっても名前で追従する。
/// </summary>
public sealed class GpoSelection
{
    public const string ColPolicyId    = "PolicyId";
    public const string ColDisplayName = "DisplayName";
    public const string ColState       = "State";
    public const string ColScope       = "Scope";
    public const string ColElements    = "Elements";

    public static readonly string[] Columns = [ColPolicyId, ColDisplayName, ColState, ColScope, ColElements];

    private static readonly JsonSerializerOptions ElementsJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public string PolicyId    { get; set; } = "";
    /// <summary>表示名のキャッシュ（辞書に無いときの表示用）。</summary>
    public string DisplayName { get; set; } = "";
    public string State       { get; set; } = GpoStates.Enabled;
    /// <summary>Machine / User。class=Both のポリシーで意味を持つ。</summary>
    public string Scope       { get; set; } = GpoPolicyClass.Machine;
    /// <summary>要素 ID → 値（bool は "1"/"0"、enum は項目値、list/multiText は改行区切り）。</summary>
    public Dictionary<string, string> Elements { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> ToRow() => new(StringComparer.Ordinal)
    {
        [ColPolicyId]    = PolicyId,
        [ColDisplayName] = DisplayName,
        [ColState]       = GpoStates.Normalize(State),
        [ColScope]       = Scope == GpoPolicyClass.User ? GpoPolicyClass.User : GpoPolicyClass.Machine,
        [ColElements]    = Elements.Count == 0 ? "" : JsonSerializer.Serialize(Elements, ElementsJson),
    };

    public static GpoSelection FromRow(IReadOnlyDictionary<string, string> row)
    {
        var sel = new GpoSelection
        {
            PolicyId    = Cell(row, ColPolicyId).Trim(),
            DisplayName = Cell(row, ColDisplayName),
            State       = GpoStates.Normalize(Cell(row, ColState)),
            Scope       = Cell(row, ColScope).Trim() == GpoPolicyClass.User ? GpoPolicyClass.User : GpoPolicyClass.Machine,
        };
        var json = Cell(row, ColElements);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, ElementsJson);
                if (dict is not null)
                    foreach (var (k, v) in dict) sel.Elements[k] = v ?? "";
            }
            catch
            {
                // 壊れた要素 JSON は空扱い（コンパイル時に必須要素のエラーになる）
            }
        }
        return sel;
    }

    /// <summary>要素の値（要素 ID で引き、無ければ値名でも引く。無ければ null）。</summary>
    public string? GetElementValue(GpoElement e)
    {
        if (Elements.TryGetValue(e.Id, out var v)) return v;
        if (e.ValueName is not null && Elements.TryGetValue(e.ValueName, out v)) return v;
        return null;
    }

    public GpoSelection Clone()
    {
        var c = new GpoSelection { PolicyId = PolicyId, DisplayName = DisplayName, State = State, Scope = Scope };
        foreach (var (k, v) in Elements) c.Elements[k] = v;
        return c;
    }

    private static string Cell(IReadOnlyDictionary<string, string> row, string col)
    {
        foreach (var (k, v) in row)
            if (k.Equals(col, StringComparison.OrdinalIgnoreCase)) return v ?? "";
        return "";
    }
}

/// <summary>お気に入り層（master_template/gpo_favorites.json）の 1 件。辞書本体は再生成可能なので手編集しない。</summary>
public sealed class GpoFavorite
{
    [JsonPropertyName("id")]       public string  Id    { get; set; } = "";
    [JsonPropertyName("group")]    public string  Group { get; set; } = "";
    [JsonPropertyName("note")]     public string? Note  { get; set; }
    /// <summary>推奨状態（Enabled / Disabled）。省略時は Enabled。</summary>
    [JsonPropertyName("state")]    public string? State { get; set; }
    [JsonPropertyName("scope")]    public string? Scope { get; set; }
    /// <summary>推奨する要素値（要素 ID → 値）。</summary>
    [JsonPropertyName("elements")] public Dictionary<string, string>? Elements { get; set; }
}

public sealed class GpoFavoritesFile
{
    [JsonPropertyName("version")]   public int Version { get; set; } = 1;
    [JsonPropertyName("favorites")] public List<GpoFavorite> Favorites { get; set; } = [];
}
