using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// レジストリ設定テンプレートコレクションの管理実装。
/// <para>
/// 永続化先: &lt;exe と同じフォルダ&gt;\registry_collection\catalog.json
/// （ポータブル運用対応: AppDomain.CurrentDomain.BaseDirectory を起点とする）
/// </para>
/// </summary>
public class RegistryCollectionService : IRegistryCollectionService
{
    // ── 永続化パス ────────────────────────────────────────────────────────

    private static readonly string DataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "registry_collection");

    private static readonly string CatalogPath = Path.Combine(DataDir, "catalog.json");

    // ── reg_config CSV の相対パス（ワークスペースルートからの相対）────────

    private const string HklmRelPath = @"modules\standard\reg_hklm_config\reg_hklm_list.csv";
    private const string HkcuRelPath = @"modules\standard\reg_hkcu_config\reg_hkcu_list.csv";

    // ── JSON シリアライズ設定 ─────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented            = true,
        PropertyNameCaseInsensitive = true,
    };

    // ── 状態 ─────────────────────────────────────────────────────────────

    private List<RegistryTemplateEntry> _entries = [];

    public IReadOnlyList<RegistryTemplateEntry> Entries => _entries;

    // ── 初期化 ────────────────────────────────────────────────────────────

    /// <summary>
    /// catalog.json が存在すればロード。存在しなければ空のまま待機（graceful degradation）。
    /// App.OnStartup で VM 構築前に呼び出すこと。
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        Directory.CreateDirectory(DataDir);

        if (File.Exists(CatalogPath))
            await ReloadAsync();
    }

    // ── Reload ────────────────────────────────────────────────────────────

    public async Task ReloadAsync()
    {
        if (!File.Exists(CatalogPath))
        {
            _entries = [];
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(CatalogPath);
            var data = JsonSerializer.Deserialize<CatalogData>(json, JsonOptions);
            _entries = data?.Entries ?? [];
        }
        catch
        {
            // ファイル破損等は無視して空リストにフォールバック
            _entries = [];
        }
    }

    // ── Add ──────────────────────────────────────────────────────────────

    public async Task AddAsync(RegistryTemplateEntry entry)
    {
        _entries.Add(entry);
        await SaveAsync();
    }

    // ── Update ────────────────────────────────────────────────────────────

    public async Task UpdateAsync(RegistryTemplateEntry entry)
    {
        var index = _entries.FindIndex(e => e.Id == entry.Id);
        if (index < 0) return;

        _entries[index] = entry;
        await SaveAsync();
    }

    // ── Remove ────────────────────────────────────────────────────────────

    public async Task RemoveAsync(string id)
    {
        var removed = _entries.RemoveAll(e => e.Id == id);
        if (removed > 0)
            await SaveAsync();
    }

    // ── Export ────────────────────────────────────────────────────────────

    public async Task<ExportResult> ExportToWorkspaceAsync(
        RegistryTemplateEntry entry,
        string workspaceRootPath)
    {
        var relPath = entry.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
            ? HkcuRelPath
            : HklmRelPath;

        var csvPath = Path.Combine(workspaceRootPath, relPath);

        return await Task.Run(() => ExportSingle(entry, csvPath));
    }

    /// <summary>
    /// 既存 CSV を読み込み、重複チェック後に新行を追加して全件書き戻す。
    /// append ではなく read-modify-write にすることで改行問題を回避する。
    /// </summary>
    private static ExportResult ExportSingle(RegistryTemplateEntry entry, string csvPath)
    {
        try
        {
            var existing = LoadRegConfigRows(csvPath);

            // 重複チェック（KeyPath + KeyName の大文字小文字を無視）
            if (existing.Any(r =>
                    string.Equals(r.KeyPath, entry.KeyPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.KeyName, entry.KeyName, StringComparison.OrdinalIgnoreCase)))
            {
                return new ExportResult { Skipped = 1 };
            }

            // AdminID を既存最大値 + 1 で採番
            var maxId = existing.Count > 0
                ? existing.Max(r => int.TryParse(r.AdminId, out var n) ? n : 0)
                : 0;

            existing.Add(new RegConfigRow
            {
                Enabled      = "1",
                AdminId      = (maxId + 1).ToString(),
                SettingTitle = entry.Title,
                KeyPath      = entry.KeyPath,
                KeyName      = entry.KeyName,
                Type         = entry.Type,
                Value        = entry.Value,
            });

            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

            // BOM 付き UTF-8 で全件書き込み（PowerShell 5.1 の Import-Csv と互換性維持）
            using var writer = new StreamWriter(csvPath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteRecords(existing);

            return new ExportResult { Added = 1 };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ExportResult { Error = $"アクセスが拒否されました。\n{ex.Message}" };
        }
        catch (IOException ex)
        {
            return new ExportResult { Error = $"ファイル操作中にエラーが発生しました。\n{ex.Message}" };
        }
        catch (Exception ex)
        {
            return new ExportResult { Error = $"予期しないエラーが発生しました。\n{ex.Message}" };
        }
    }

    /// <summary>
    /// reg_config CSV を読み込む。ファイルが存在しない場合や読み込み失敗時は空リストを返す。
    /// </summary>
    private static List<RegConfigRow> LoadRegConfigRows(string csvPath)
    {
        if (!File.Exists(csvPath)) return [];

        try
        {
            // BOM 付き UTF-8 / BOM なし UTF-8 のどちらも読めるよう detectEncodingFromByteOrderMarks を有効化
            using var reader = new StreamReader(csvPath, detectEncodingFromByteOrderMarks: true);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
            return csv.GetRecords<RegConfigRow>().ToList();
        }
        catch
        {
            return [];
        }
    }

    // ── 永続化書き込み ────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var data = new CatalogData { Entries = _entries };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(CatalogPath, json);
        }
        catch
        {
            // 保存失敗はランタイムに影響させない（次回起動時に再試行できる）
        }
    }

    // ── 内部 DTO ──────────────────────────────────────────────────────────

    /// <summary>catalog.json のルートオブジェクト。</summary>
    private sealed class CatalogData
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("entries")]
        public List<RegistryTemplateEntry> Entries { get; set; } = [];
    }

    /// <summary>
    /// reg_hklm_list.csv / reg_hkcu_list.csv の 1 行を表す内部モデル。
    /// CsvHelper の [Name] 属性でカラム名を明示する。
    /// </summary>
    private sealed class RegConfigRow
    {
        [Name("Enabled")]      public string Enabled      { get; set; } = "1";
        [Name("AdminID")]      public string AdminId      { get; set; } = "1";
        [Name("SettingTitle")] public string SettingTitle { get; set; } = "";
        [Name("KeyPath")]      public string KeyPath      { get; set; } = "";
        [Name("KeyName")]      public string KeyName      { get; set; } = "";
        [Name("Type")]         public string Type         { get; set; } = "";
        [Name("Value")]        public string Value        { get; set; } = "";
    }
}
