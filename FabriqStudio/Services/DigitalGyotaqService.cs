using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class DigitalGyotaqService : IDigitalGyotaqService
{
    private readonly IWorkspaceService _workspace;

    /// <summary>ツール専用テンプレートベースパス（gyotaq_template 等を格納）。</summary>
    private static readonly string StudioTemplatesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "template", "template_fabriq");

    public DigitalGyotaqService(IWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    private string GetRoot() =>
        _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    // ── Load ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GyotaqTask>> LoadTaskListAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Context.RegisterClassMap<GyotaqTaskMap>();
            return (IReadOnlyList<GyotaqTask>)csv.GetRecords<GyotaqTask>().ToList();
        });
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    public async Task SaveTaskListAsync(string filePath, IEnumerable<GyotaqTask> tasks)
    {
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(filePath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.Context.RegisterClassMap<GyotaqTaskMap>();
            csv.WriteRecords(tasks);
        });
    }

    // ── コマンドライブラリ ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GyotaqCommand>> LoadCommandLibraryAsync()
    {
        var uriListPath = ResolveUriListPath();
        return await Task.Run(() =>
        {
            using var reader = new StreamReader(uriListPath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
            return (IReadOnlyList<GyotaqCommand>)csv.GetRecords<GyotaqCommand>().ToList();
        });
    }

    // ── Export ───────────────────────────────────────────────────────────────

    public async Task<string> ExportModuleAsync(
        string                moduleName,
        IEnumerable<GyotaqTask> tasks,
        bool                  overwrite = false)
    {
        // 1. バリデーション
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("モジュール名を入力してください。");

        var invalidChars = Path.GetInvalidFileNameChars();
        if (moduleName.Any(c => invalidChars.Contains(c)))
            throw new ArgumentException("モジュール名に使用できない文字が含まれています。");

        var sanitized = moduleName.Trim();

        // 2. パス解決
        var fabriqRoot = GetRoot();
        var destDir    = Path.Combine(fabriqRoot, "modules", "extended", sanitized);

        // 3. 既存チェック
        if (Directory.Exists(destDir))
        {
            if (!overwrite)
                throw new InvalidOperationException($"モジュール「{sanitized}」は既に存在します。");

            await Task.Run(() => Directory.Delete(destDir, recursive: true));
        }

        await Task.Run(() => Directory.CreateDirectory(destDir));

        // 4. gyotaq_config.ps1 テンプレートをコピー
        var templateDir    = ResolveGyotaqTemplateDir();
        var templateScript = Path.Combine(templateDir, "gyotaq_config.ps1");
        var destScript     = Path.Combine(destDir, "gyotaq_config.ps1");
        await Task.Run(() => File.Copy(templateScript, destScript, overwrite: true));

        // 5. README.txt をコピー（存在する場合）
        var templateReadme = Path.Combine(templateDir, "README.txt");
        if (File.Exists(templateReadme))
        {
            var destReadme = Path.Combine(destDir, "README.txt");
            await Task.Run(() => File.Copy(templateReadme, destReadme, overwrite: true));
        }

        // 6. module.csv を生成
        var moduleCsvPath = Path.Combine(destDir, "module.csv");
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(moduleCsvPath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("MenuName,Category,Script,Order,Enabled");
            writer.WriteLine($"{sanitized},ManualWorks,gyotaq_config.ps1,95,0");
        });

        // 7. task_list.csv を保存
        var taskListPath = Path.Combine(destDir, "task_list.csv");
        await SaveTaskListAsync(taskListPath, tasks);

        return destDir;
    }

    // ── パス解決ヘルパー ─────────────────────────────────────────────────────

    /// <summary>
    /// gyotaq_template ディレクトリのパスを解決する。
    /// 常にアプリ同梱の StudioTemplatesPath/gyotaq_template/ を参照する。
    /// </summary>
    private static string ResolveGyotaqTemplateDir()
    {
        var path = Path.Combine(StudioTemplatesPath, "gyotaq_template");
        if (Directory.Exists(path))
            return path;

        throw new DirectoryNotFoundException(
            "Gyotaq テンプレートディレクトリが見つかりません。\n" +
            "アプリ同梱テンプレートに gyotaq_template/ が存在することを確認してください。");
    }

    /// <summary>
    /// uri_list.csv のパスを解決する。
    /// 常にアプリ同梱の StudioTemplatesPath/gyotaq_template/uri_list.csv を参照する。
    /// </summary>
    private static string ResolveUriListPath()
    {
        var path = Path.Combine(StudioTemplatesPath, "gyotaq_template", "uri_list.csv");
        if (File.Exists(path))
            return path;

        throw new FileNotFoundException(
            "コマンドライブラリ (uri_list.csv) が見つかりません。\n" +
            "アプリ同梱テンプレートに gyotaq_template/uri_list.csv が存在することを確認してください。");
    }

    // ── CsvHelper マッピング ─────────────────────────────────────────────────

    /// <summary>
    /// GyotaqTask の CSV マッピング。
    /// - Enabled: bool ↔ 0/1 変換
    /// - TaskId: CSV 上は "TaskID"（大文字 ID）
    /// </summary>
    private sealed class GyotaqTaskMap : ClassMap<GyotaqTask>
    {
        public GyotaqTaskMap()
        {
            Map(m => m.Enabled).TypeConverter<BoolToIntConverter>();
            Map(m => m.TaskId).Name("TaskID");
            Map(m => m.TaskTitle);
            Map(m => m.Instruction);
            Map(m => m.OpenCommand);
            Map(m => m.OpenArgs);
        }
    }

    /// <summary>bool ↔ 0/1 の CSV 型変換。</summary>
    private sealed class BoolToIntConverter : DefaultTypeConverter
    {
        public override object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
            => text?.Trim() == "1";

        public override string? ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
            => value is true ? "1" : "0";
    }
}
