using System.Data;
using System.IO;
using FabriqStudio.Models.Gpo;

namespace FabriqStudio.Services.Gpo;

public sealed class GpoExportResult
{
    public int     Added    { get; set; }
    public int     Replaced { get; set; }
    public string? Error    { get; set; }
    public string  RelPath  { get; set; } = "";
    public bool    Succeeded => Error is null;
}

/// <summary>GPO 辞書画面から、ワークスペースの gpo_config/gpo_list.csv へ行を書き出す。</summary>
public interface IGpoExportService
{
    /// <summary>現在の書き先（編集先データセットに従う）。表示用の相対パス。</summary>
    string RelPath { get; }

    Task<GpoExportResult> ExportAsync(IReadOnlyList<GpoRow> rows);
}

/// <summary>
/// 既存 CSV を読み込み、同じポリシー（PolicyRef の左辺）の行と同じエントリ（Scope + KeyPath + ValueName）の行を
/// 取り除いてから追加する（read-modify-write。fabriq 側の重複検証で止まらないようにする）。
/// </summary>
public sealed class GpoExportService : IGpoExportService
{
    private const string Module = "gpo_config";
    private const string Csv    = "gpo_list.csv";

    private readonly IFileService        _file;
    private readonly IModuleDataResolver _resolver;
    private readonly IDataSetContext     _dataSet;
    private readonly IProfileDataService _profileData;

    public GpoExportService(IFileService file, IModuleDataResolver resolver, IDataSetContext dataSet, IProfileDataService profileData)
    {
        _file        = file;
        _resolver    = resolver;
        _dataSet     = dataSet;
        _profileData = profileData;
    }

    /// <summary>本体側の相対パス（standard → extended の順に探索。無ければ standard 想定）。</summary>
    private string BodyRel => _resolver.ModuleRelPath(Module, Csv) ?? _resolver.ModuleRelPath("standard", Module, Csv);

    public string RelPath => _resolver.ResolveWrite(BodyRel, _dataSet.Current).RelPath;

    public async Task<GpoExportResult> ExportAsync(IReadOnlyList<GpoRow> rows)
    {
        var dataSet = _dataSet.Current;
        var bodyRel = BodyRel;

        // 編集先データセット（PDF）が選ばれていて PDF に無ければ、本体から as-is で取り込んでから追記する
        //（本体の既存行を落とさない = 取り込みで実行結果は変わらない）
        if (dataSet is not null)
            await _profileData.MaterializeCsvAsync(dataSet, Module, Csv);

        var target = _resolver.ResolveWrite(bodyRel, dataSet);
        var result = new GpoExportResult { RelPath = target.RelPath };
        var path   = target.AbsPath;

        if (!File.Exists(path))
        {
            result.Error = $"{result.RelPath} が見つかりません（gpo_config モジュールが無いか、fabriq の版が古い可能性があります）。";
            return result;
        }
        if (rows.Count == 0)
        {
            result.Error = "書き出す行がありません。";
            return result;
        }

        try
        {
            var table = await _file.ReadCsvAsDataTableAsync(path);
            foreach (var col in new[] { "Enabled", "AdminID", "SettingTitle", "Scope", "KeyPath", "ValueName", "Action", "Type", "Value" })
            {
                if (!table.Columns.Contains(col))
                {
                    result.Error = $"{result.RelPath} に列 {col} がありません。";
                    return result;
                }
            }
            var hasRef     = table.Columns.Contains("PolicyRef");
            var hasSegment = table.Columns.Contains("Segment");

            var refs = new HashSet<string>(rows.Select(r => RefHead(r.PolicyRef)), StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(rows.Select(r => r.DedupeKey), StringComparer.Ordinal);

            var toRemove = new List<DataRow>();
            foreach (DataRow row in table.Rows)
            {
                var segment = hasSegment ? row["Segment"]?.ToString()?.Trim() ?? "" : "";
                if (segment.Length > 0) continue;   // マスタ設計などが所有する行は触らない

                var policyRef = hasRef ? row["PolicyRef"]?.ToString() ?? "" : "";
                var key = new GpoRow
                {
                    Scope     = row["Scope"]?.ToString()?.Trim() ?? "",
                    KeyPath   = row["KeyPath"]?.ToString()?.Trim() ?? "",
                    ValueName = row["ValueName"]?.ToString()?.Trim() ?? "",
                }.DedupeKey;

                if ((policyRef.Length > 0 && refs.Contains(RefHead(policyRef))) || keys.Contains(key))
                    toRemove.Add(row);
            }
            foreach (var r in toRemove) table.Rows.Remove(r);
            result.Replaced = toRemove.Count;

            var maxId = 0;
            foreach (DataRow row in table.Rows)
                if (int.TryParse(row["AdminID"]?.ToString(), out var n) && n > maxId) maxId = n;

            foreach (var r in rows)
            {
                var row = table.NewRow();
                foreach (DataColumn c in table.Columns) row[c] = "";
                row["Enabled"]      = "1";
                row["AdminID"]      = (++maxId).ToString();
                row["SettingTitle"] = r.Title;
                row["Scope"]        = r.Scope;
                row["KeyPath"]      = r.KeyPath;
                row["ValueName"]    = r.ValueName;
                row["Action"]       = r.Action;
                row["Type"]         = r.Type;
                row["Value"]        = r.Value;
                if (hasRef) row["PolicyRef"] = r.PolicyRef;
                table.Rows.Add(row);
                result.Added++;
            }

            await _file.WriteCsvFromDataTableAsync(path, table);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    /// <summary>PolicyRef の「=状態」より前（ポリシーの識別部）。</summary>
    private static string RefHead(string policyRef)
    {
        var i = policyRef.IndexOf('=');
        return (i < 0 ? policyRef : policyRef[..i]).Trim();
    }
}
