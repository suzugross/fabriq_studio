namespace FabriqStudio.Models.Master;

/// <summary>プレビュー／生成の計画。回答から純粋に計算され、Apply で初めてディスクに書く。</summary>
public sealed class MasterPlan
{
    public string MasterName { get; init; } = "";

    /// <summary>モジュール CSV への行追加（Segment または Description タグで隔離）。</summary>
    public List<PlanCsvRows>      CsvOps      { get; } = [];

    /// <summary>案件別レジストリ CSV（reg_hklm_list_&lt;名&gt;.csv / reg_hkcu_list_&lt;名&gt;.csv）の全体書き込み。</summary>
    public List<PlanRegistryFile> RegistryOps { get; } = [];

    /// <summary>生成するプロファイル CSV（マスタ本体 / 配備）。</summary>
    public List<PlanProfile>      Profiles    { get; } = [];

    /// <summary>生成するテキストファイル（ODT の configuration.xml 等）。</summary>
    public List<PlanTextFile>     TextFiles   { get; } = [];

    /// <summary>生成物のうち今回は不要になったため削除するファイル（相対パス）。</summary>
    public List<PlanDelete>       Deletes     { get; } = [];

    public List<PlanMessage>      Messages    { get; } = [];

    /// <summary>fabriq では自動化できず、作業者が手で行う項目。</summary>
    public List<string>           ManualTasks { get; } = [];

    /// <summary>ダイアログ表示用のファイル別サマリ（BuildPlan の最後に計算）。</summary>
    public List<PlanFileSummary>  FileSummaries { get; } = [];

    public bool HasErrors => Messages.Any(m => m.Severity == PlanSeverity.Error);
}

public enum PlanSeverity { Info, Warning, Error }

public sealed class PlanMessage
{
    public PlanSeverity Severity { get; init; }
    public string       Message  { get; init; } = "";
    public string?      ItemId   { get; init; }

    public string SeverityLabel => Severity switch
    {
        PlanSeverity.Error   => "エラー",
        PlanSeverity.Warning => "警告",
        _                    => "情報",
    };
}

/// <summary>
/// 隔離方式。Segment 列があれば Segment、無ければ Description のタグ。
/// hostlist.csv は AdminID（管理番号）= マスタ名 で隔離する。
/// </summary>
public enum PlanIsolation { Segment, DescriptionTag, AdminId, None }

public sealed class PlanCsvRows
{
    public string ModuleDir { get; init; } = "";
    public string CsvName   { get; init; } = "";
    /// <summary>ワークスペースルートからの相対パス（表示用）。</summary>
    public string RelPath   { get; init; } = "";
    public string AbsPath   { get; init; } = "";
    public PlanIsolation Isolation { get; init; }
    /// <summary>DescriptionTag 方式で使うタグ文字列（例: [master:M_xxx]）。</summary>
    public string Tag       { get; init; } = "";
    public List<Dictionary<string, string>> Rows { get; } = [];
    /// <summary>既存ファイル中の、同じマスタが以前に書いた行数（置換される行数）。</summary>
    public int ExistingIsolatedRows { get; set; }
}

public sealed class PlanRegistryRow
{
    public string SettingTitle { get; init; } = "";
    public string KeyPath      { get; init; } = "";
    public string KeyName      { get; init; } = "";
    public string Type         { get; init; } = "";
    public string Value        { get; init; } = "";
    /// <summary>行の Segment（通常はマスタ名。一時ポリシーは マスタ名:temp）。</summary>
    public string Segment      { get; init; } = "";
}

/// <summary>生成するプロファイルの種別。</summary>
public enum ProfileKind
{
    /// <summary>マスタ本体（profiles/&lt;名&gt;.csv）。</summary>
    Master,
    /// <summary>Sysprep（profiles/&lt;名&gt;_sysprep.csv）。マスタ作成後に Administrator で実行する。</summary>
    Sysprep,
}

public sealed class PlanRegistryFile
{
    /// <summary>"HKLM" / "HKCU"</summary>
    public string Hive      { get; init; } = "";
    public string ModuleDir { get; init; } = "";
    public string RelPath   { get; init; } = "";
    public string AbsPath   { get; init; } = "";
    public bool   Exists    { get; init; }
    public List<PlanRegistryRow> Rows { get; } = [];
}

public sealed class PlanProfile
{
    public string Name    { get; init; } = "";
    public string RelPath { get; init; } = "";
    public string AbsPath { get; init; } = "";
    public bool   Exists  { get; init; }
    public ProfileKind Kind { get; init; } = ProfileKind.Master;
    public bool   IsSysprep => Kind == ProfileKind.Sysprep;
    public List<ProfileScriptEntry> Rows { get; } = [];
}

public sealed class PlanTextFile
{
    public string RelPath { get; init; } = "";
    public string AbsPath { get; init; } = "";
    public bool   Exists  { get; init; }
    public string Content { get; init; } = "";
    /// <summary>ダイアログ表示用の短い説明（例: ODT configuration.xml）。</summary>
    public string Label   { get; init; } = "";
}

public sealed class PlanDelete
{
    public string RelPath { get; init; } = "";
    public string AbsPath { get; init; } = "";
    public string Reason  { get; init; } = "";
}

/// <summary>ダイアログの「書き込み先」一覧の 1 行。</summary>
public sealed class PlanFileSummary
{
    public string RelPath { get; init; } = "";
    /// <summary>新規 / 追加 / 置換 / 削除</summary>
    public string Action  { get; init; } = "";
    public string Detail  { get; init; } = "";
}

/// <summary>Apply の結果。</summary>
public sealed class MasterApplyResult
{
    public List<string> Written { get; } = [];
    public List<string> Failed  { get; } = [];
    public string? Error { get; set; }
    public bool Succeeded => Error is null && Failed.Count == 0;
}
