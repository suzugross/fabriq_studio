using System.Text.Json.Serialization;

namespace FabriqStudio.Models.Master;

/// <summary>
/// マスタ設計画面の質問テンプレート（exe 同梱 master_template/master_template.json）。
/// 質問・選択肢・既定値・レジストリ辞書 ID への対応を JSON で持ち、
/// ヒアリングシートの改訂に JSON 編集で追随できるようにする。
/// 単位換算や複数列への展開が要るモジュール CSV の出力は C# の Emitter（Services/Master/Emitters）が担当する。
/// </summary>
public sealed class MasterTemplate
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("sections")] public List<MasterSection> Sections { get; set; } = [];
}

/// <summary>画面の 1 章（ヒアリングシートの章立てに対応）。</summary>
public sealed class MasterSection
{
    [JsonPropertyName("id")]          public string  Id          { get; set; } = "";
    [JsonPropertyName("title")]       public string  Title       { get; set; } = "";
    /// <summary>生成プロファイルの Group 列に使う名前（表示用。実際の割当は Emitter が決める）。</summary>
    [JsonPropertyName("group")]       public string  Group       { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    /// <summary>パラメータシート（お客様向け）での章名。省略時は <see cref="Title"/>。</summary>
    [JsonPropertyName("sheetTitle")]  public string? SheetTitle  { get; set; }
    [JsonPropertyName("items")]       public List<MasterItem> Items { get; set; } = [];
}

/// <summary>質問 1 件。<see cref="Type"/> で UI と値の形が決まる。</summary>
public sealed class MasterItem
{
    [JsonPropertyName("id")]    public string Id    { get; set; } = "";

    /// <summary>bool / choice / text / multiline / number / multi / table / info</summary>
    [JsonPropertyName("type")]  public string Type  { get; set; } = MasterItemTypes.Text;

    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("help")]  public string? Help { get; set; }

    /// <summary>既定値（bool は "1"/"0"、choice は Option.Value、text/number は文字列）。</summary>
    [JsonPropertyName("default")] public string? Default { get; set; }

    /// <summary>章の中の設定ジャンル（例: Windows Update / 電源オプション）。連続する同名の項目が 1 つのカードにまとまる。</summary>
    [JsonPropertyName("subgroup")] public string? Subgroup { get; set; }

    /// <summary>行右端に出す「落ち先」ラベル（例: power_config / 辞書 2 件 / 手動）。</summary>
    [JsonPropertyName("target")] public string? Target { get; set; }

    /// <summary>落ち先の種別（module / dict / manual / fabriq）。バッジ色に使う。</summary>
    [JsonPropertyName("kind")]   public string? Kind { get; set; }

    /// <summary>秘密情報（生成時に ENC: 暗号化する）。</summary>
    [JsonPropertyName("secret")] public bool Secret { get; set; }

    [JsonPropertyName("placeholder")] public string? Placeholder { get; set; }
    [JsonPropertyName("unit")]        public string? Unit        { get; set; }

    /// <summary>choice / multi の選択肢。</summary>
    [JsonPropertyName("options")] public List<MasterChoice>? Options { get; set; }

    /// <summary>bool が true のときに出すレジストリ辞書エントリ。</summary>
    [JsonPropertyName("registryTrue")]  public List<RegistryEmit>? RegistryTrue  { get; set; }

    /// <summary>bool が false のときに出すレジストリ辞書エントリ。</summary>
    [JsonPropertyName("registryFalse")] public List<RegistryEmit>? RegistryFalse { get; set; }

    /// <summary>table の列定義。</summary>
    [JsonPropertyName("columns")] public List<MasterColumn>? Columns { get; set; }

    /// <summary>table の初期行（新規マスタ作成時のみ投入）。</summary>
    [JsonPropertyName("defaultRows")] public List<Dictionary<string, string>>? DefaultRows { get; set; }

    /// <summary>multi で選択肢に無い値の自由追加を許すか。</summary>
    [JsonPropertyName("allowFree")] public bool AllowFree { get; set; }

    /// <summary>他の質問の値に応じて表示／非表示を切り替える条件。</summary>
    [JsonPropertyName("visibleWhen")] public VisibleWhen? VisibleWhen { get; set; }

    /// <summary>ドラッグ＆ドロップで資材を受け付ける設定（table / file の項目）。</summary>
    [JsonPropertyName("drop")] public MasterDropSpec? Drop { get; set; }

    /// <summary>action 項目が実行する処理の識別子（例: odtDownload）。ViewModel 側で解釈する。</summary>
    [JsonPropertyName("action")] public string? Action { get; set; }

    /// <summary>action 項目のボタン文言（省略時「▶ 実行」）。実行中の文言は runningLabel、横の注記は placeholder。</summary>
    [JsonPropertyName("buttonLabel")]  public string? ButtonLabel  { get; set; }
    [JsonPropertyName("runningLabel")] public string? RunningLabel { get; set; }

    /// <summary>パラメータシート／チェックリストでの見せ方（お客様向けの文言）。省略時は画面の文言をそのまま使う。</summary>
    [JsonPropertyName("sheet")] public SheetSpec? Sheet { get; set; }
}

/// <summary>
/// 帳票（パラメータシート / チェックリスト）での項目の見せ方。fabriq の仕組みではなく
/// 「Windows にどう設定したか」をお客様に伝える文言をここに置く。
/// </summary>
public sealed class SheetSpec
{
    /// <summary>帳票に出さない（fabriq 内部の項目）。</summary>
    [JsonPropertyName("hide")]    public bool    Hide    { get; set; }
    /// <summary>帳票での項目名（省略時は画面のラベル）。</summary>
    [JsonPropertyName("label")]   public string? Label   { get; set; }
    /// <summary>値ごとの表現（choice の値 / bool の "1"・"0" / multi の値 → お客様向けの文）。</summary>
    [JsonPropertyName("values")]  public Dictionary<string, string>? Values { get; set; }
    /// <summary>table で出す列（この順。"A|B" は A が空なら B を出す）。省略時は全列。</summary>
    [JsonPropertyName("columns")] public List<string>? Columns { get; set; }
    /// <summary>設定方法（Windows 側の言い方。例: レジストリで無効化、グループポリシー）。</summary>
    [JsonPropertyName("method")]  public string? Method  { get; set; }
    /// <summary>table のセル値の表現（列名 → 値 → お客様向けの文。例: IsDefault の 1 → 既定）。</summary>
    [JsonPropertyName("cellValues")] public Dictionary<string, Dictionary<string, string>>? CellValues { get; set; }
}

/// <summary>
/// ドロップ枠の定義。ドロップされたファイル／フォルダをモジュールのサブフォルダへコピーし、
/// table なら行を追加、file なら値（ファイル名）を設定する。
/// </summary>
public sealed class MasterDropSpec
{
    /// <summary>コピー先モジュール（例: app_config）。</summary>
    [JsonPropertyName("module")] public string Module { get; set; } = "";

    /// <summary>モジュール直下のサブフォルダ（例: file / assets / source / INF）。</summary>
    [JsonPropertyName("subDir")] public string SubDir { get; set; } = "";

    /// <summary>受け付ける拡張子（小文字、先頭ドット付き）。空なら制限なし。</summary>
    [JsonPropertyName("extensions")] public List<string> Extensions { get; set; } = [];

    /// <summary>フォルダのドロップを許可するか（プリンタドライバ等）。</summary>
    [JsonPropertyName("folders")] public bool Folders { get; set; }

    /// <summary>コピー時にこの名前に固定する（file 項目用。例: setup.exe）。</summary>
    [JsonPropertyName("fixedName")] public string? FixedName { get; set; }

    /// <summary>installer / shortcut / printerDriver / file。補完の方法が変わる。</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = MasterDropKinds.File;

    [JsonPropertyName("fileColumn")]        public string? FileColumn        { get; set; }
    [JsonPropertyName("nameColumn")]        public string? NameColumn        { get; set; }
    [JsonPropertyName("typeColumn")]        public string? TypeColumn        { get; set; }
    [JsonPropertyName("argsColumn")]        public string? ArgsColumn        { get; set; }
    [JsonPropertyName("driverColumn")]      public string? DriverColumn      { get; set; }
    [JsonPropertyName("descriptionColumn")] public string? DescriptionColumn { get; set; }

    /// <summary>ドロップ枠に表示する案内文。</summary>
    [JsonPropertyName("hint")] public string? Hint { get; set; }

    public bool AcceptsExtension(string fileName)
    {
        if (Extensions.Count == 0) return true;
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return Extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary><see cref="MasterDropSpec.Kind"/> の定数。</summary>
public static class MasterDropKinds
{
    public const string Installer     = "installer";
    public const string Shortcut      = "shortcut";
    public const string PrinterDriver = "printerDriver";
    public const string File          = "file";
    /// <summary>フォルダーをそのままコピーする（例: アプリ設定フォルダーを sysprep_config/source へ）。</summary>
    public const string Folder        = "folder";
}

/// <summary>choice / multi の選択肢 1 件。</summary>
public sealed class MasterChoice
{
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";

    /// <summary>この選択肢が選ばれたときに出すレジストリ辞書エントリ。</summary>
    [JsonPropertyName("registry")] public List<RegistryEmit>? Registry { get; set; }

    /// <summary>Emitter が参照する付加情報（例: Windows の機能の Action / Source）。</summary>
    [JsonPropertyName("data")] public Dictionary<string, string>? Data { get; set; }

    public override string ToString() => Label;
}

/// <summary>レジストリ辞書（registry_collection/catalog.json）のエントリ参照。</summary>
public sealed class RegistryEmit
{
    /// <summary>辞書エントリの 8 桁 hex ID。</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>辞書の既定値を上書きする値（省略時は辞書の値をそのまま使う）。</summary>
    [JsonPropertyName("value")] public string? Value { get; set; }
}

/// <summary>table の列定義。</summary>
public sealed class MasterColumn
{
    [JsonPropertyName("name")]    public string Name    { get; set; } = "";
    [JsonPropertyName("label")]   public string Label   { get; set; } = "";
    [JsonPropertyName("options")] public List<string>? Options { get; set; }
    [JsonPropertyName("default")] public string? Default { get; set; }
    [JsonPropertyName("secret")]  public bool Secret { get; set; }
}

/// <summary>表示条件: 参照先の質問の値が <see cref="Values"/> のいずれかに一致するとき表示。</summary>
public sealed class VisibleWhen
{
    [JsonPropertyName("item")]   public string       Item   { get; set; } = "";
    [JsonPropertyName("values")] public List<string> Values { get; set; } = [];
}

/// <summary><see cref="MasterItem.Type"/> の定数。</summary>
public static class MasterItemTypes
{
    public const string Bool      = "bool";
    public const string Choice    = "choice";
    public const string Text      = "text";
    public const string Multiline = "multiline";
    public const string Number    = "number";
    public const string Multi     = "multi";
    public const string Table     = "table";
    public const string Info      = "info";
    /// <summary>1 ファイルをドロップして配置する項目（値 = 配置したファイル名）。</summary>
    public const string File      = "file";
    /// <summary>ボタンで処理を実行する項目（値を持たない。例: ODT のダウンロード）。</summary>
    public const string Action    = "action";
    /// <summary>GPO 辞書から選んだポリシーの一覧（値は tables[id] に GpoSelection の行として保存）。</summary>
    public const string Gpo       = "gpo";
    /// <summary>レジストリ辞書から選んだ設定の一覧（値は tables[id] に RegistrySelection の行として保存）。</summary>
    public const string Registry  = "registry";
}

/// <summary><see cref="MasterItem.Kind"/> の定数（バッジ種別）。</summary>
public static class MasterItemKinds
{
    public const string Module = "module";
    public const string Dict   = "dict";
    public const string Manual = "manual";
    public const string Fabriq = "fabriq";
}
