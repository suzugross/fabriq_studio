using System.Text.Json.Serialization;

namespace FabriqStudio.Models.Master;

/// <summary>
/// マスタ設計画面の回答。<c>profiles/&lt;マスタ名&gt;.master.json</c> に保存する
/// （kernel / Studio のプロファイル列挙は *.csv のみを拾うため混入しない）。
/// 値はすべて文字列で持つ（bool は "1"/"0"、choice は選択肢の Value）。
/// </summary>
public sealed class MasterAnswers
{
    [JsonPropertyName("schemaVersion")]   public int    SchemaVersion   { get; set; } = 1;
    [JsonPropertyName("templateVersion")] public int    TemplateVersion { get; set; } = 1;

    /// <summary>マスタ名。Segment 値・プロファイル名・回答ファイル名に使う（^[A-Za-z0-9_-]+$）。</summary>
    [JsonPropertyName("masterName")]  public string MasterName  { get; set; } = "";
    [JsonPropertyName("projectName")] public string ProjectName { get; set; } = "";
    [JsonPropertyName("version")]     public string Version     { get; set; } = "1";
    [JsonPropertyName("worker")]      public string Worker      { get; set; } = "";
    [JsonPropertyName("notes")]       public string Notes       { get; set; } = "";
    [JsonPropertyName("createdAt")]   public string CreatedAt   { get; set; } = "";
    [JsonPropertyName("updatedAt")]   public string UpdatedAt   { get; set; } = "";

    /// <summary>bool / choice / text / multiline / number の値（itemId → 値）。</summary>
    [JsonPropertyName("values")]
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    /// <summary>multi の選択値（itemId → 選択肢 Value のリスト。自由追加分も含む）。</summary>
    [JsonPropertyName("multi")]
    public Dictionary<string, List<string>> Multi { get; set; } = new(StringComparer.Ordinal);

    /// <summary>table の行（itemId → 行のリスト。行は 列名 → 値）。</summary>
    [JsonPropertyName("tables")]
    public Dictionary<string, List<Dictionary<string, string>>> Tables { get; set; } = new(StringComparer.Ordinal);

    /// <summary>直近の生成で書き込んだファイル（相対パス）。表示用の記録。</summary>
    [JsonPropertyName("lastGenerated")] public string? LastGenerated { get; set; }
    [JsonPropertyName("lastFiles")]     public List<string> LastFiles { get; set; } = [];

    // ── 参照ヘルパ ──────────────────────────────────────────────

    public string GetValue(string itemId, string? fallback = null)
        => Values.TryGetValue(itemId, out var v) ? v : (fallback ?? "");

    public bool IsTrue(string itemId, bool fallback = false)
        => Values.TryGetValue(itemId, out var v) ? v.Trim() == "1" : fallback;

    public IReadOnlyList<string> GetMulti(string itemId)
        => Multi.TryGetValue(itemId, out var list) ? list : [];

    public IReadOnlyList<Dictionary<string, string>> GetTable(string itemId)
        => Tables.TryGetValue(itemId, out var rows) ? rows : [];

    /// <summary>マスタ名として許可する形式（Segment 値・ファイル名・reg CSV 名に流用するため英数字のみ）。</summary>
    public static bool IsValidMasterName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_-]+$");
}
