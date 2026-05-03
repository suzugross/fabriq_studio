using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// Pianist Profile（modules/extended/pianist/profiles/&lt;name&gt;/）の I/O 実装。
///
/// Phase 1 は読み込みのみ。procedure.csv と shortcuts.csv は固定スキーマなので
/// 既存の <see cref="ICsvService"/> 経由で読み（BOM/UTF-8 自動処理を享受）、
/// values.csv は wide format の動的カラムを扱うため CsvHelper を直接呼ぶ。
///
/// pianist.json / instructions/*.txt は §10 規約（BOM なし + LF）でも読み込み側の
/// File.ReadAllText / JsonSerializer は BOM 有無 / 改行形式を吸収するため特別な処理は不要。
/// </summary>
public class PianistProfileService : IPianistProfileService
{
    /// <summary>workspace ルートからの Pianist プロファイル親ディレクトリ（相対パス）。</summary>
    private const string PianistProfilesRel = "modules/extended/pianist/profiles";

    /// <summary>
    /// Pianist 配下の CSV を読むときの寛容設定。
    /// PS 側 Import-Csv は寛容だが CsvHelper は RFC 4180 に厳格で、sample profile の自然な
    /// 表記が落ちるため必要な箇所だけ寛容化する。プロンプト §10.2 が「外部エディタで規約
    /// 破りに保存されたファイルも読込みは可能」と明記しているため Pianist 側のみ適用、
    /// 共通 <see cref="ICsvService"/> は触らない（他 CSV の厳格性を維持）。
    ///
    /// - <c>BadDataFound = null</c>: 未エスケープの " を含むセル（kintone profile の Note の
    ///   `"Kintone"` 強調表記など）を許容
    /// - <c>MissingFieldFound = null</c>: 行末コンマ不足で末尾列が欠けている行
    ///   （kintone profile L19 の `Screenshot` 欠落など）を空文字として補完
    /// </summary>
    private static readonly CsvConfiguration TolerantReadConfig =
        new(CultureInfo.InvariantCulture)
        {
            BadDataFound      = null,
            MissingFieldFound = null,
        };

    private readonly IWorkspaceService _workspace;
    private readonly ICsvService       _csvService;

    public PianistProfileService(IWorkspaceService workspace, ICsvService csvService)
    {
        _workspace  = workspace;
        _csvService = csvService;
    }

    private string GetRoot() =>
        _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    public Task<IReadOnlyList<PianistProfileEntry>> GetProfilesAsync()
    {
        var profilesDir = Path.Combine(GetRoot(), PianistProfilesRel);

        if (!Directory.Exists(profilesDir))
            return Task.FromResult<IReadOnlyList<PianistProfileEntry>>(Array.Empty<PianistProfileEntry>());

        IReadOnlyList<PianistProfileEntry> result = Directory
            .GetDirectories(profilesDir)
            .Select(d => new PianistProfileEntry
            {
                Name       = Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar)),
                FolderPath = d
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(result);
    }

    public async Task<PianistProfileData> LoadProfileAsync(PianistProfileEntry entry)
    {
        if (string.IsNullOrEmpty(entry.FolderPath) || !Directory.Exists(entry.FolderPath))
            throw new FileNotFoundException(
                $"Pianist プロファイルフォルダが見つかりません: {entry.FolderPath}");

        var data = new PianistProfileData { Entry = entry };

        data.Metadata = await LoadMetadataAsync(entry.FolderPath, entry.Name);
        await LoadStepsAsync(data, entry.FolderPath);
        data.Values = await LoadValuesAsync(entry.FolderPath);
        await LoadShortcutsAsync(data, entry.FolderPath);
        LoadInstructions(data, entry.FolderPath);

        return data;
    }

    // ─── pianist.json ───────────────────────────────────────────────
    private static async Task<PianistProfileMetadata> LoadMetadataAsync(
        string folderPath, string fallbackLabel)
    {
        var metaPath = Path.Combine(folderPath, "pianist.json");
        if (!File.Exists(metaPath))
        {
            // pianist.ps1 と同じ fallback（L313-L315）: フォルダ名を label に使う
            return new PianistProfileMetadata { Label = fallbackLabel };
        }

        try
        {
            await using var stream = File.OpenRead(metaPath);
            var meta = await JsonSerializer.DeserializeAsync<PianistProfileMetadata>(stream);
            return meta ?? new PianistProfileMetadata { Label = fallbackLabel };
        }
        catch (JsonException)
        {
            // 不正な JSON でも編集 UI を起動できるようにフォールバック
            return new PianistProfileMetadata
            {
                Label       = fallbackLabel,
                Description = "(invalid pianist.json)"
            };
        }
    }

    // ─── procedure.csv ──────────────────────────────────────────────
    private static async Task LoadStepsAsync(PianistProfileData data, string folderPath)
    {
        var procPath = Path.Combine(folderPath, "procedure.csv");
        if (!File.Exists(procPath)) return;

        await Task.Run(() =>
        {
            using var reader = new StreamReader(procPath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, TolerantReadConfig);

            foreach (var row in csv.GetRecords<PianistStep>())
                data.Steps.Add(row);
        });
    }

    // ─── values.csv（wide format, dynamic columns） ─────────────────
    private static async Task<PianistValueTable> LoadValuesAsync(string folderPath)
    {
        var valuesPath = Path.Combine(folderPath, "values.csv");
        var table = new PianistValueTable();
        if (!File.Exists(valuesPath)) return table;

        await Task.Run(() =>
        {
            using var reader = new StreamReader(valuesPath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, TolerantReadConfig);

            if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is null)
                return;

            var header = csv.HeaderRecord;
            var newPCNameIdx = Array.FindIndex(header,
                h => string.Equals(h, "NewPCName", StringComparison.Ordinal));

            if (newPCNameIdx < 0)
            {
                // pianist 1.0.0 旧 long format（Key,Value,Encrypted,Note）。
                // Phase 1 では検出のみ。実際の自動移行は Phase 7 で実装（§5.5）。
                table.WasLegacyFormat = true;
                return;
            }

            // NewPCName 以外を変数列としてヘッダー順に保持
            for (int i = 0; i < header.Length; i++)
            {
                if (i == newPCNameIdx) continue;
                table.VariableColumns.Add(header[i]);
            }

            while (csv.Read())
            {
                var row = new PianistValueRow
                {
                    NewPCName = csv.GetField(newPCNameIdx) ?? "",
                    Table     = table,
                };
                foreach (var col in table.VariableColumns)
                    row.Cells[col] = csv.GetField(col) ?? "";

                table.Rows.Add(row);
            }
        });

        // `*` 行を必ず先頭 1 行で確保（§5.2.B）。同時に各行の Table 逆参照も貼る。
        if (!table.WasLegacyFormat)
            table.EnsureStarRow();

        return table;
    }

    // ─── shortcuts.csv ──────────────────────────────────────────────
    private static async Task LoadShortcutsAsync(PianistProfileData data, string folderPath)
    {
        var shortcutsPath = Path.Combine(folderPath, "shortcuts.csv");
        if (!File.Exists(shortcutsPath)) return;

        await Task.Run(() =>
        {
            using var reader = new StreamReader(shortcutsPath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, TolerantReadConfig);

            foreach (var row in csv.GetRecords<PianistShortcut>())
                data.Shortcuts.Add(row);
        });
    }

    // ─── instructions/*.txt ─────────────────────────────────────────
    private static void LoadInstructions(PianistProfileData data, string folderPath)
    {
        var dir = Path.Combine(folderPath, "instructions");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.txt", SearchOption.TopDirectoryOnly))
        {
            var phaseId = Path.GetFileNameWithoutExtension(file);
            // pianist.ps1 と同様 UTF-8 で読み、CRLF/LF 混在は呼び出し側で吸収
            data.Instructions[phaseId] = File.ReadAllText(file, Encoding.UTF8);
        }
    }

    // ─── 新規プロファイル作成（§11 / §15 step 3） ───────────────
    /// <summary>プロファイル名（フォルダ名）のパターン: 半角英数 + アンダースコア（§2）。</summary>
    private static readonly Regex ProfileNamePattern = new(@"^[A-Za-z0-9_]+$");

    public string? ValidateNewProfileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "プロファイル名を入力してください。";
        if (!ProfileNamePattern.IsMatch(name))
            return "プロファイル名は半角英数 + アンダースコア (_) のみ使用できます（§2）。";
        var folder = Path.Combine(GetRoot(), PianistProfilesRel, name);
        if (Directory.Exists(folder))
            return $"プロファイル「{name}」は既に存在します。";
        return null;
    }

    public async Task<PianistProfileEntry> CreateNewProfileAsync(string name)
    {
        var err = ValidateNewProfileName(name);
        if (err is not null) throw new InvalidOperationException(err);

        var folder = Path.Combine(GetRoot(), PianistProfilesRel, name);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "instructions"));
        // [Samples] section が参照する画像置き場（pianist v1.5.0 以降）。
        // 空でも作っておくことで Studio の画像追加 UX が初回からスムーズになる。
        Directory.CreateDirectory(Path.Combine(folder, "screenshots"));

        var entry = new PianistProfileEntry { Name = name, FolderPath = folder };

        // テンプレートデータを組み立てる（最小限の placeholder）
        var data = new PianistProfileData
        {
            Entry = entry,
            Metadata = new PianistProfileMetadata
            {
                Schema       = 1,
                Label        = name,
                Description  = "",
                TargetApp    = "",
                DefaultPhase = "P01",
                Version      = "0.1.0",
            },
        };

        // P01 に Wait 1 件の placeholder Step
        data.Steps.Add(new PianistStep
        {
            PhaseID    = "P01",
            PhaseLabel = "新規 Phase",
            Color      = "Blue",
            StepNo     = 1,
            Action     = "Wait",
            Value      = "1000",
        });

        // values.csv: NewPCName 列のみ + `*` 行（変数列ゼロ）
        data.Values.EnsureStarRow();

        // instructions/P01.txt: 4-section DSL の空雛形（pianist v1.4.0 以降）。
        // [RPA] / [Manual] / [Variables] / [Samples] が空でも明示しておくと
        // Studio で開いた瞬間に編集 UI の各サブタブにフォーカスできる。
        data.Instructions["P01"] = "[RPA]\r\n\r\n[Manual]\r\n\r\n";

        // §10 規約で全ファイル書き出し
        await SaveMetadataAsync(folder, data.Metadata);
        await SaveStepsAsync(folder, data.Steps);
        await SaveValuesAsync(folder, data.Values);
        await SaveShortcutsAsync(folder, data.Shortcuts);
        SaveInstructions(folder, data.Instructions);

        return entry;
    }

    // ─── 旧 long format ローダ（§5.5 移行用） ──────────────────────
    public async Task<IReadOnlyList<PianistLegacyValueEntry>> LoadLegacyValuesAsync(
        PianistProfileEntry entry)
    {
        var path = Path.Combine(entry.FolderPath, "values.csv");
        if (!File.Exists(path))
            return Array.Empty<PianistLegacyValueEntry>();

        return await Task.Run<IReadOnlyList<PianistLegacyValueEntry>>(() =>
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            using var csv    = new CsvReader(reader, TolerantReadConfig);
            return csv.GetRecords<PianistLegacyValueEntry>().ToList();
        });
    }

    // ─── 保存（§10 規約準拠） ─────────────────────────────────────
    /// <summary>UTF-8 BOM 付きエンコーディング（CSV 用）。</summary>
    private static readonly UTF8Encoding Utf8WithBom    = new(encoderShouldEmitUTF8Identifier: true);
    /// <summary>UTF-8 BOM なしエンコーディング（JSON / TXT 用）。</summary>
    private static readonly UTF8Encoding Utf8NoBom      = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// CSV 共通の書き込み設定（§10.1）。<see cref="CsvHelper"/> の既定 NewLine が
    /// Environment.NewLine に依存して将来クロスプラットフォームで破綻するのを防ぐため、
    /// 明示的に CRLF を指定する。
    /// </summary>
    private static readonly CsvHelper.Configuration.CsvConfiguration CsvWriteConfig =
        new(CultureInfo.InvariantCulture) { NewLine = "\r\n" };

    public async Task<string?> SaveProfileAsync(PianistProfileData data, ICryptoService crypto)
    {
        var folder = data.Entry.FolderPath;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return $"プロファイルフォルダが見つかりません: {folder}";

        try
        {
            // 1. pianist.json
            await SaveMetadataAsync(folder, data.Metadata);

            // 2. procedure.csv
            await SaveStepsAsync(folder, data.Steps);

            // 3. values.csv（セル値はそのまま書く。暗号化／復号は事前に右クリック ContextMenu で
            //    ユーザーが明示的に行う方針 — HostDetail パターンと統一）
            await SaveValuesAsync(folder, data.Values);

            // 4. shortcuts.csv
            await SaveShortcutsAsync(folder, data.Shortcuts);

            // 5. instructions/*.txt
            SaveInstructions(folder, data.Instructions);

            return null;
        }
        catch (Exception ex)
        {
            return $"保存エラー: {ex.Message}";
        }
    }

    private static async Task SaveMetadataAsync(string folder, PianistProfileMetadata meta)
    {
        var path    = Path.Combine(folder, "pianist.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json    = JsonSerializer.Serialize(meta, options);

        // §10: 改行は LF / 末尾改行 1 つ。.NET 8 の JsonSerializer は環境依存の改行を
        // 含み得る（実際は \n を出すが念のため）ため、書き出し前に正規化する。
        json = json.Replace("\r\n", "\n");
        if (!json.EndsWith("\n", StringComparison.Ordinal)) json += "\n";

        await File.WriteAllTextAsync(path, json, Utf8NoBom);
    }

    private static async Task SaveStepsAsync(string folder, IEnumerable<PianistStep> steps)
    {
        var path = Path.Combine(folder, "procedure.csv");
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(path, append: false, encoding: Utf8WithBom);
            using var csv    = new CsvWriter(writer, CsvWriteConfig);
            csv.WriteRecords(steps);
        });
    }

    private static async Task SaveShortcutsAsync(string folder, IEnumerable<PianistShortcut> shortcuts)
    {
        var path = Path.Combine(folder, "shortcuts.csv");
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(path, append: false, encoding: Utf8WithBom);
            using var csv    = new CsvWriter(writer, CsvWriteConfig);
            csv.WriteRecords(shortcuts);
        });
    }

    private static async Task SaveValuesAsync(string folder, PianistValueTable table)
    {
        var path = Path.Combine(folder, "values.csv");
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(path, append: false, encoding: Utf8WithBom);
            using var csv    = new CsvWriter(writer, CsvWriteConfig);

            // ヘッダー: NewPCName + 変数列（VariableColumns 順）
            csv.WriteField("NewPCName");
            foreach (var col in table.VariableColumns)
                csv.WriteField(col);
            csv.NextRecord();

            // データ行（`*` 行が先頭に居る前提 = EnsureStarRow 済み）。
            // セル値は ENC:... or 平文のままメモリにあるので、そのまま書く。
            foreach (var row in table.Rows)
            {
                csv.WriteField(row.NewPCName);
                foreach (var col in table.VariableColumns)
                    csv.WriteField(row[col]);
                csv.NextRecord();
            }
        });
    }

    private static void SaveInstructions(string folder, IDictionary<string, string> instructions)
    {
        var dir = Path.Combine(folder, "instructions");
        Directory.CreateDirectory(dir);

        foreach (var (phaseId, body) in instructions)
        {
            var path = Path.Combine(dir, $"{phaseId}.txt");
            // §10: BOM なし + LF + 末尾改行
            var normalized = body.Replace("\r\n", "\n");
            if (!normalized.EndsWith("\n", StringComparison.Ordinal))
                normalized += "\n";
            File.WriteAllText(path, normalized, Utf8NoBom);
        }
    }

    // ─── pianist_list.csv I/O ─────────────────────────────────────
    /// <summary>workspace ルートからの pianist_list.csv 相対パス。</summary>
    private const string PianistListRel = "modules/extended/pianist/pianist_list.csv";

    /// <summary>
    /// CsvHelper シリアライズ専用 DTO。Studio 内のモデル（<see cref="PianistListEntry"/>）は
    /// Enabled を bool で扱うが CSV 上は "1"/"0" 文字列のため、間にこの DTO を挟んで変換する。
    /// </summary>
    private class PianistListCsvRow
    {
        public string Enabled     { get; set; } = "";
        public string ProfileName { get; set; } = "";
        public string Group       { get; set; } = "";
        public string Description { get; set; } = "";
        public string Segment     { get; set; } = "";
    }

    public async Task<IReadOnlyList<PianistListEntry>> LoadPianistListAsync()
    {
        var path = Path.Combine(GetRoot(), PianistListRel);
        if (!File.Exists(path))
            return Array.Empty<PianistListEntry>();

        return await Task.Run<IReadOnlyList<PianistListEntry>>(() =>
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            using var csv    = new CsvReader(reader, TolerantReadConfig);
            var rows = csv.GetRecords<PianistListCsvRow>().ToList();
            return rows.Select(r => new PianistListEntry
            {
                Enabled     = string.Equals(r.Enabled?.Trim(), "1", StringComparison.Ordinal),
                ProfileName = r.ProfileName ?? "",
                Group       = r.Group       ?? "",
                Description = r.Description ?? "",
                Segment     = r.Segment     ?? "",
            }).ToList();
        });
    }

    public async Task<string?> SavePianistListAsync(IEnumerable<PianistListEntry> entries)
    {
        var path = Path.Combine(GetRoot(), PianistListRel);
        var dir  = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return $"pianist モジュールディレクトリが存在しません: {dir}";

        try
        {
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(path, append: false, encoding: Utf8WithBom);
                using var csv    = new CsvWriter(writer, CsvWriteConfig);

                csv.WriteField("Enabled");
                csv.WriteField("ProfileName");
                csv.WriteField("Group");
                csv.WriteField("Description");
                csv.WriteField("Segment");
                csv.NextRecord();

                foreach (var e in entries)
                {
                    csv.WriteField(e.Enabled ? "1" : "0");
                    csv.WriteField(e.ProfileName);
                    csv.WriteField(e.Group);
                    csv.WriteField(e.Description);
                    csv.WriteField(e.Segment);
                    csv.NextRecord();
                }
            });
            return null;
        }
        catch (Exception ex)
        {
            return $"pianist_list.csv の保存に失敗: {ex.Message}";
        }
    }
}
