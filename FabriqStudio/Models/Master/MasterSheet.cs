namespace FabriqStudio.Models.Master;

/// <summary>
/// パラメータシート（お客様提出用）とチェックリスト（作業確認用）の元になる、回答を人が読める形にした文書。
/// テンプレートの章立てどおりに並び、非表示の質問（visibleWhen が偽）と入力欄でない項目（info / action）は含めない。
/// </summary>
public sealed class SheetDocument
{
    public string MasterName    { get; init; } = "";
    public string ProjectName   { get; init; } = "";
    public string Version       { get; init; } = "";
    public string Worker        { get; init; } = "";
    public string Notes         { get; init; } = "";
    public string GeneratedAt   { get; init; } = "";
    /// <summary>直近の生成日時（回答ファイルの記録。未生成なら空）。</summary>
    public string LastGenerated { get; init; } = "";

    public List<SheetSection> Sections    { get; } = [];
    /// <summary>fabriq で自動化できず作業者が手で行う項目（生成計画の手動作業リスト）。</summary>
    public List<string>       ManualTasks { get; } = [];

    public int RowCount => Sections.Sum(s => s.Blocks.Sum(b => b.Rows.Count));
}

public sealed class SheetSection
{
    public string Id    { get; init; } = "";
    public string Title { get; init; } = "";
    public List<SheetBlock> Blocks { get; } = [];
}

/// <summary>章の中のジャンル（テンプレートの subgroup が連続する範囲）。</summary>
public sealed class SheetBlock
{
    public string Title { get; init; } = "";
    public List<SheetRow> Rows { get; } = [];
}

/// <summary>質問 1 件の表示行。値は <see cref="Text"/>（1 行）/ <see cref="Lines"/>（箇条書き）/ <see cref="Table"/>（表）のいずれか。</summary>
public sealed class SheetRow
{
    public string ItemId { get; init; } = "";
    public string Label  { get; init; } = "";
    /// <summary>落ち先の種別ラベル（対応 / 辞書 / 手動 / fabriq側）。</summary>
    public string Kind   { get; init; } = "";
    /// <summary>落ち先（モジュール名など。チェックリストの「反映先」）。</summary>
    public string Target { get; init; } = "";
    /// <summary>設定方法（Windows 側の言い方。テンプレートの sheet.method）。</summary>
    public string Method { get; init; } = "";
    public string        Text  { get; init; } = "";
    public List<string>? Lines { get; init; }
    public SheetTable?   Table { get; init; }
    /// <summary>秘密情報の行（帳票では平文で出す。強調などの用途に残す）。</summary>
    public bool IsSecret { get; init; }
}

public sealed class SheetTable
{
    public List<string>       Headers { get; } = [];
    public List<List<string>> Rows    { get; } = [];
}
