namespace FabriqStudio.Models.Master;

/// <summary>
/// 計画（BuildPlan）を同期・純粋に計算できるよう、ワークスペースの必要情報を事前に読み込んだもの。
/// モジュールの有無・各 CSV のヘッダー・既存 Segment 値・既存タグ行数を持つ。
/// 生成（Apply）後は再読込する。
/// </summary>
public sealed class MasterWorkspaceSnapshot
{
    public string RootPath { get; init; } = "";

    /// <summary>モジュールディレクトリ名（大文字小文字無視）→ 情報。</summary>
    public Dictionary<string, MasterModuleInfo> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>profiles/ 直下に存在するプロファイル名（拡張子なし、大文字小文字無視）。</summary>
    public HashSet<string> ProfileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>kernel/csv/hostlist.csv の情報（無ければ null）。仮ホスト名の行を AdminID=マスタ名 で書くために使う。</summary>
    public MasterCsvInfo? Hostlist { get; set; }

    public bool HasModule(string moduleDir) => Modules.ContainsKey(moduleDir);

    public MasterModuleInfo? GetModule(string moduleDir)
        => Modules.TryGetValue(moduleDir, out var m) ? m : null;
}

public sealed class MasterModuleInfo
{
    public string Dir     { get; init; } = "";
    /// <summary>"standard" / "extended"</summary>
    public string Kind    { get; init; } = "";
    public string AbsPath { get; init; } = "";

    /// <summary>module.csv の Script（ファイル名）→ MenuName。プロファイル行の Description に使う。</summary>
    public Dictionary<string, string> ScriptMenuNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>設定 CSV（module.csv / preset.csv を除く）のファイル名 → 情報。</summary>
    public Dictionary<string, MasterCsvInfo> Csvs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>モジュール直下のサブディレクトリ名（file / wallpaper / source / INF / payload 等）。</summary>
    public HashSet<string> SubDirs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>サブディレクトリ名 → 直下のファイル名（資材の存在確認用）。</summary>
    public Dictionary<string, HashSet<string>> SubDirFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasFile(string subDir, string fileName)
        => SubDirFiles.TryGetValue(subDir, out var files) && files.Contains(fileName);
}

public sealed class MasterCsvInfo
{
    public string Name    { get; init; } = "";
    public string AbsPath { get; init; } = "";
    public List<string> Headers { get; } = [];
    public bool HasSegment => Headers.Any(h => h.Equals("Segment", StringComparison.OrdinalIgnoreCase));
    public bool HasColumn(string name) => Headers.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Segment 値 → 行数。</summary>
    public Dictionary<string, int> SegmentCounts { get; } = new(StringComparer.Ordinal);

    /// <summary>Description 列に含まれる [master:名] タグ → 行数。</summary>
    public Dictionary<string, int> TagCounts { get; } = new(StringComparer.Ordinal);

    /// <summary>AdminID 列の値 → 行数（hostlist.csv の仮ホスト名行の隔離に使う）。</summary>
    public Dictionary<string, int> AdminIdCounts { get; } = new(StringComparer.Ordinal);

    /// <summary>AdminID 列の値 → その最初の行（列名 → 値）。hostlist.csv で端末の行を上書きしないための判定に使う。</summary>
    public Dictionary<string, Dictionary<string, string>> RowsByAdminId { get; } = new(StringComparer.Ordinal);

    public int RowCount { get; set; }
}
