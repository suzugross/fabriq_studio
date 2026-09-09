using System.Text.RegularExpressions;

namespace FabriqStudio.Models.Master;

/// <summary>
/// 計画（BuildPlan）を同期・純粋に計算できるよう、ワークスペースの必要情報を事前に読み込んだもの。
/// 本体（modules/&lt;tier&gt;/…）の情報に加え、各データフォルダ（PDF: profiles/&lt;名&gt;/modules/…）の情報を持つ。
/// <see cref="ForMaster"/> で「マスタが使うデータフォルダを重ねた合成ビュー」を作り、Emitter はそれを見る
/// （カーネルと同じ規則: CSV はファイル単位、資材フォルダはフォルダ単位、reg_*_list*.csv はモジュール単位で PDF 優先）。
/// 生成（Apply）後は再読込する。
/// </summary>
public sealed class MasterWorkspaceSnapshot
{
    public string RootPath { get; init; } = "";

    /// <summary>本体: モジュールディレクトリ名（大文字小文字無視）→ 情報。</summary>
    public Dictionary<string, MasterModuleInfo> Modules { get; private init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>データフォルダ名（profiles/&lt;名&gt;/）→ モジュール名 → PDF 側の情報。</summary>
    public Dictionary<string, Dictionary<string, MasterModuleInfo>> Overlays { get; private init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>profiles/ 直下に存在するプロファイル名（拡張子なし、大文字小文字無視）。</summary>
    public HashSet<string> ProfileNames { get; private init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>kernel/csv/hostlist.csv の情報（無ければ null）。仮ホスト名の行を管理番号で書くために使う。</summary>
    public MasterCsvInfo? Hostlist { get; set; }

    private readonly Func<string, string?>? _dataSetOf;
    private readonly Dictionary<string, MasterModuleInfo?> _composed = new(StringComparer.OrdinalIgnoreCase);

    public MasterWorkspaceSnapshot() { }

    private MasterWorkspaceSnapshot(MasterWorkspaceSnapshot source, Func<string, string?> dataSetOf)
    {
        RootPath     = source.RootPath;
        Modules      = source.Modules;
        Overlays     = source.Overlays;
        ProfileNames = source.ProfileNames;
        Hostlist     = source.Hostlist;
        _dataSetOf   = dataSetOf;
    }

    /// <summary>合成ビューを作る。<paramref name="dataSetOf"/> はモジュール名 → 重ねるデータフォルダ名（null = 本体のみ）。</summary>
    public MasterWorkspaceSnapshot ForMaster(Func<string, string?> dataSetOf) => new(this, dataSetOf);

    public bool HasModule(string moduleDir) => Modules.ContainsKey(moduleDir);

    /// <summary>モジュール情報。合成ビューではデータフォルダを重ねた結果を返す（結果はキャッシュ）。</summary>
    public MasterModuleInfo? GetModule(string moduleDir)
    {
        if (_dataSetOf is null)
            return Modules.TryGetValue(moduleDir, out var m) ? m : null;

        if (_composed.TryGetValue(moduleDir, out var cached)) return cached;
        var composed = GetModule(moduleDir, _dataSetOf(moduleDir));
        _composed[moduleDir] = composed;
        return composed;
    }

    /// <summary>指定データフォルダを本体に重ねたモジュール情報。本体に無いモジュールは null（スクリプトが無いため使えない）。</summary>
    public MasterModuleInfo? GetModule(string moduleDir, string? dataSet)
    {
        if (!Modules.TryGetValue(moduleDir, out var body)) return null;
        var pdf = OverlayModule(moduleDir, dataSet);
        return pdf is null ? body : MasterModuleInfo.Compose(body, pdf);
    }

    public MasterModuleInfo? OverlayModule(string moduleDir, string? dataSet)
        => dataSet is not null
           && Overlays.TryGetValue(dataSet, out var mods)
           && mods.TryGetValue(moduleDir, out var m) ? m : null;

    /// <summary>
    /// 指定データフォルダで実際に読まれる設定 CSV の情報（PDF にあればそれ、無ければ本体。どちらにも無ければ null）。
    /// reg_*_list*.csv は PDF に 1 件でもあれば本体側は読まれない。
    /// </summary>
    public MasterCsvInfo? CsvInfoFor(string moduleDir, string csvName, string? dataSet)
    {
        var pdf = OverlayModule(moduleDir, dataSet);
        if (pdf is not null)
        {
            if (pdf.Csvs.TryGetValue(csvName, out var pc)) return pc;
            if (MasterModuleInfo.IsRegList(csvName) && pdf.Csvs.Keys.Any(MasterModuleInfo.IsRegList)) return null;
        }
        return Modules.TryGetValue(moduleDir, out var body) && body.Csvs.TryGetValue(csvName, out var bc) ? bc : null;
    }
}

public sealed class MasterModuleInfo
{
    private static readonly Regex RegListPattern = new(@"^reg_(hklm|hkcu)_list.*\.csv$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Dir     { get; init; } = "";
    /// <summary>"standard" / "extended"</summary>
    public string Kind    { get; init; } = "";
    /// <summary>本体のモジュールフォルダ（スクリプト・module.csv・preset.csv の場所）。</summary>
    public string AbsPath { get; init; } = "";

    /// <summary>この情報がデータフォルダ由来（または合成済み）のときのデータフォルダ名。本体だけなら null。</summary>
    public string? DataSet { get; init; }

    /// <summary>module.csv の Script（ファイル名）→ MenuName。プロファイル行の Description に使う。</summary>
    public Dictionary<string, string> ScriptMenuNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>設定 CSV（module.csv / preset.csv を除く）のファイル名 → 情報。</summary>
    public Dictionary<string, MasterCsvInfo> Csvs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>モジュール直下のサブディレクトリ名（file / wallpaper / source / INF / payload 等）。</summary>
    public HashSet<string> SubDirs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>サブディレクトリ名（2 階層目は "sub\child"）→ 直下のファイル名（資材の存在確認用）。</summary>
    public Dictionary<string, HashSet<string>> SubDirFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasFile(string subDir, string fileName)
        => SubDirFiles.TryGetValue(subDir, out var files) && files.Contains(fileName);

    public static bool IsRegList(string csvName) => RegListPattern.IsMatch(csvName);

    /// <summary>
    /// データフォルダ側を本体に重ねる（fabriq §3 の粒度）:
    /// CSV はファイル単位で PDF 優先、reg_*_list*.csv は PDF に 1 件でもあれば本体側を落とす、
    /// 資材フォルダはフォルダ単位（PDF にあれば空でもその中身、無ければ本体）。
    /// </summary>
    public static MasterModuleInfo Compose(MasterModuleInfo body, MasterModuleInfo pdf)
    {
        var m = new MasterModuleInfo { Dir = body.Dir, Kind = body.Kind, AbsPath = body.AbsPath, DataSet = pdf.DataSet };
        foreach (var (k, v) in body.ScriptMenuNames) m.ScriptMenuNames[k] = v;

        var pdfHasReg = pdf.Csvs.Keys.Any(IsRegList);
        foreach (var (k, v) in pdf.Csvs) m.Csvs[k] = v;
        foreach (var (k, v) in body.Csvs)
        {
            if (m.Csvs.ContainsKey(k)) continue;
            if (pdfHasReg && IsRegList(k)) continue;
            m.Csvs[k] = v;
        }

        foreach (var d in body.SubDirs) m.SubDirs.Add(d);
        foreach (var d in pdf.SubDirs)  m.SubDirs.Add(d);
        foreach (var d in m.SubDirs)
        {
            var src = pdf.SubDirs.Contains(d) ? pdf : body;
            foreach (var (k, v) in src.SubDirFiles)
            {
                if (k.Equals(d, StringComparison.OrdinalIgnoreCase) || k.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase))
                    m.SubDirFiles[k] = v;
            }
            if (!m.SubDirFiles.ContainsKey(d)) m.SubDirFiles[d] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        return m;
    }
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
