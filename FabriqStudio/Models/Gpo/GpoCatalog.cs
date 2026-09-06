namespace FabriqStudio.Models.Gpo;

/// <summary>
/// ADMX / ADML から生成した GPO 辞書（管理用テンプレート = レジストリベースのポリシー一覧）。
/// 読み込み後は不変として扱う（検索は <see cref="Services.Gpo.IGpoCatalogService"/> が行う）。
/// </summary>
public sealed class GpoCatalog
{
    public string   SourcePath { get; init; } = "";
    /// <summary>表示名・説明に使った言語（例: ja-JP。無ければ en-US）。</summary>
    public string   Language   { get; init; } = "";
    /// <summary>版タグ（OS ビルド / ADMX 本数 / 最終更新日 / 言語）。</summary>
    public string   VersionTag { get; init; } = "";
    public DateTime LoadedAt   { get; init; } = DateTime.Now;
    public int      AdmxCount  { get; init; }

    public List<GpoPolicy>   Policies   { get; } = [];
    public List<GpoCategory> Categories { get; } = [];
    /// <summary>読み込み時に無視したファイル・ポリシーの理由（致命ではない）。</summary>
    public List<string>      Errors     { get; } = [];

    private Dictionary<string, GpoPolicy>? _byId;
    private IReadOnlyList<string>?         _topCategories;

    /// <summary>ID（&lt;ADMX 名&gt;:&lt;policy name&gt;）でポリシーを引く。大文字小文字は無視。</summary>
    public GpoPolicy? FindPolicy(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (_byId is null)
        {
            var d = new Dictionary<string, GpoPolicy>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Policies) d.TryAdd(p.Id, p);
            _byId = d;
        }
        return _byId.TryGetValue(id.Trim(), out var policy) ? policy : null;
    }

    /// <summary>最上位カテゴリ名（フィルタ用）。</summary>
    public IReadOnlyList<string> TopCategories
        => _topCategories ??= Policies
            .Select(p => p.TopCategory)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();
}

/// <summary>ADMX のカテゴリ（gpedit のツリー）。</summary>
public sealed class GpoCategory
{
    /// <summary>名前空間:名前（ADMX 間の参照解決に使う内部キー）。</summary>
    public string  Key         { get; init; } = "";
    public string  Name        { get; init; } = "";
    public string  DisplayName { get; init; } = "";
    public string? ParentKey   { get; init; }
    /// <summary>表示パス（例: Windows コンポーネント &gt; Windows Update &gt; 更新プログラムの管理）。</summary>
    public string  Path        { get; set; } = "";
}

/// <summary><see cref="GpoPolicy.Class"/> の値。</summary>
public static class GpoPolicyClass
{
    public const string Machine = "Machine";
    public const string User    = "User";
    public const string Both    = "Both";

    public static string Label(string cls) => cls switch
    {
        Machine => "コンピューター",
        User    => "ユーザー",
        Both    => "両方",
        _       => cls,
    };
}

/// <summary>管理用テンプレートのポリシー 1 件。</summary>
public sealed class GpoPolicy
{
    /// <summary>&lt;ADMX ファイル名（拡張子なし）&gt;:&lt;policy name&gt;。gpo_list.csv の PolicyRef の左辺と同じ。</summary>
    public string Id        { get; init; } = "";
    public string AdmxFile  { get; init; } = "";
    public string Name      { get; init; } = "";
    /// <summary>Machine / User / Both。</summary>
    public string Class     { get; init; } = GpoPolicyClass.Machine;

    public string DisplayName   { get; set; } = "";
    public string DisplayNameEn { get; set; } = "";
    public string ExplainText   { get; set; } = "";
    public string ExplainTextEn { get; set; } = "";

    public string CategoryKey  { get; set; } = "";
    public string CategoryPath { get; set; } = "";
    public string SupportedOn  { get; set; } = "";

    /// <summary>ハイブ無しのキー（例: Software\Policies\Microsoft\Windows\WindowsUpdate\AU）。</summary>
    public string  Key       { get; init; } = "";
    public string? ValueName { get; init; }

    public GpoValue? EnabledValue  { get; set; }
    public GpoValue? DisabledValue { get; set; }
    public List<GpoRegistryItem> EnabledList  { get; } = [];
    public List<GpoRegistryItem> DisabledList { get; } = [];

    /// <summary>要素（ADMX の定義順。UI は <see cref="ElementsForUi"/> の順で並べる）。</summary>
    public List<GpoElement> Elements { get; } = [];

    /// <summary>プレゼンテーション中の説明テキストのうち、要素に紐づかなかったもの。</summary>
    public List<string> PresentationNotes { get; } = [];

    // ── お気に入り層（gpo_favorites.json）。辞書サービスが読み込み後に付ける ──
    public bool    IsFavorite    { get; set; }
    public string? FavoriteGroup { get; set; }
    public string? FavoriteNote  { get; set; }
    public GpoFavorite? Favorite { get; set; }

    // ── 検索用キャッシュ ──
    public string DisplayNameLower   { get; private set; } = "";
    public string DisplayNameEnLower { get; private set; } = "";
    public string NameLower          { get; private set; } = "";
    public string SearchText         { get; private set; } = "";

    public void BuildSearchIndex()
    {
        DisplayNameLower   = DisplayName.ToLowerInvariant();
        DisplayNameEnLower = DisplayNameEn.ToLowerInvariant();
        NameLower          = Name.ToLowerInvariant();
        SearchText = string.Join('\n', DisplayName, DisplayNameEn, Name, Id, Key, ValueName ?? "", CategoryPath, ExplainText, ExplainTextEn)
            .ToLowerInvariant();
    }

    public string TopCategory
    {
        get
        {
            var i = CategoryPath.IndexOf(" > ", StringComparison.Ordinal);
            return i < 0 ? CategoryPath : CategoryPath[..i];
        }
    }

    public bool   HasElements   => Elements.Count > 0;
    public string ScopeLabel    => GpoPolicyClass.Label(Class);
    public bool   IsBoth        => Class == GpoPolicyClass.Both;
    public string KeyDisplay    => string.IsNullOrEmpty(ValueName) ? Key : $"{Key}\\{ValueName}";
    public IEnumerable<GpoElement> ElementsForUi => Elements.OrderBy(e => e.Order);

    public override string ToString() => DisplayName;
}

public enum GpoValueKind { Decimal, LongDecimal, String, Delete }

/// <summary>ADMX の値ノード（decimal / longDecimal / string / delete）。</summary>
public sealed class GpoValue
{
    public GpoValueKind Kind { get; }
    public string       Data { get; }

    public GpoValue(GpoValueKind kind, string data)
    {
        Kind = kind;
        Data = data;
    }

    public static GpoValue Dword(uint v) => new(GpoValueKind.Decimal, v.ToString());

    /// <summary>gpo_list.csv の Type 列。delete は空。</summary>
    public string RegistryType => Kind switch
    {
        GpoValueKind.Decimal     => "REG_DWORD",
        GpoValueKind.LongDecimal => "REG_QWORD",
        GpoValueKind.String      => "REG_SZ",
        _                        => "",
    };

    /// <summary>回答ファイルへの保存・enum 項目の照合に使う文字列。</summary>
    public override string ToString() => Kind == GpoValueKind.Delete ? "<delete>" : Data;
}

/// <summary>enabledList / disabledList / trueList / falseList / valueList の 1 項目。</summary>
public sealed class GpoRegistryItem
{
    /// <summary>省略時はポリシー（または要素）のキー。</summary>
    public string?  Key       { get; init; }
    public string   ValueName { get; init; } = "";
    public GpoValue Value     { get; init; } = new(GpoValueKind.Decimal, "1");
}

public enum GpoElementType { Boolean, Decimal, LongDecimal, Text, Enum, List, MultiText }

/// <summary>ADML の presentation に対応する UI コントロールの種類。</summary>
public enum GpoControlType { CheckBox, DropdownList, DecimalTextBox, LongDecimalTextBox, TextBox, ComboBox, ListBox, MultiTextBox }

/// <summary>ポリシーの要素（有効時に追加で書く値）。</summary>
public sealed class GpoElement
{
    public string         Id        { get; init; } = "";
    public GpoElementType Type      { get; init; }
    /// <summary>省略時はポリシーのキー。</summary>
    public string?        Key       { get; init; }
    public string?        ValueName { get; init; }
    public bool           Required  { get; init; }

    // boolean
    public GpoValue? TrueValue  { get; set; }
    public GpoValue? FalseValue { get; set; }
    public List<GpoRegistryItem> TrueList  { get; } = [];
    public List<GpoRegistryItem> FalseList { get; } = [];

    // decimal / longDecimal
    public ulong? MinValue    { get; set; }
    public ulong? MaxValue    { get; set; }
    public bool   StoreAsText { get; set; }

    // text / list
    public int?   MaxLength   { get; set; }
    public bool   Expandable  { get; set; }

    // enum
    public List<GpoEnumItem> Items { get; } = [];

    // list
    public string? ValuePrefix   { get; set; }
    public bool    Additive      { get; set; }
    public bool    ExplicitValue { get; set; }

    // multiText
    public int? MaxStrings { get; set; }

    // presentation
    public string?        Label          { get; set; }
    public GpoControlType Control        { get; set; }
    public string?        DefaultText    { get; set; }
    public bool           DefaultChecked { get; set; }
    public int?           DefaultItem    { get; set; }
    public List<string>   Suggestions    { get; } = [];
    /// <summary>直前に置かれた説明テキスト（presentation の text）。</summary>
    public string?        Note           { get; set; }
    /// <summary>UI の表示順（presentation の順。無ければ ADMX 順 + 1000）。</summary>
    public int            Order          { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Id : Label!;

    public bool IsNumeric => Type is GpoElementType.Decimal or GpoElementType.LongDecimal;
    public bool IsLines   => Type is GpoElementType.List or GpoElementType.MultiText;

    /// <summary>回答に保存する形式での既定値（bool は "1"/"0"、enum は項目の値文字列）。</summary>
    public string DefaultValueString()
    {
        switch (Type)
        {
            case GpoElementType.Boolean:
                return DefaultChecked ? "1" : "0";
            case GpoElementType.Enum:
                if (Items.Count == 0) return "";
                var i = DefaultItem is { } d && d >= 0 && d < Items.Count ? d : 0;
                return Items[i].Value.ToString();
            case GpoElementType.Decimal:
            case GpoElementType.LongDecimal:
            case GpoElementType.Text:
                return DefaultText ?? "";
            default:
                return "";
        }
    }

    /// <summary>数値要素の範囲表示（例: 0～599940）。</summary>
    public string RangeText
    {
        get
        {
            if (!IsNumeric) return "";
            var min = MinValue ?? 0;
            var max = MaxValue ?? (Type == GpoElementType.Decimal ? uint.MaxValue : ulong.MaxValue);
            return $"{min}～{max}";
        }
    }
}

/// <summary>enum 要素の選択肢。</summary>
public sealed class GpoEnumItem
{
    public string   DisplayName { get; init; } = "";
    public GpoValue Value       { get; init; } = new(GpoValueKind.Decimal, "0");
    public List<GpoRegistryItem> ValueList { get; } = [];

    public override string ToString() => DisplayName;
}
