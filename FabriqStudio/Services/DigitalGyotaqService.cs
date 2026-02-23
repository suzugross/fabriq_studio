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

    /// <summary>テンプレートベースパス（アプリ同梱）。</summary>
    private static readonly string TemplateFabriqPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "template", "template_fabriq", "fabriq");

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
    /// gyotaq_template ディレクトリのパスを解決する（Dual Resolution）。
    /// 1. ワークスペース内 apps/digital_gyotaq_editor/gyotaq_template/
    /// 2. フォールバック: アプリ同梱テンプレート
    /// </summary>
    private string ResolveGyotaqTemplateDir()
    {
        const string relativePath = @"apps\digital_gyotaq_editor\gyotaq_template";

        if (_workspace.RootPath is not null)
        {
            var workspacePath = Path.Combine(_workspace.RootPath, relativePath);
            if (Directory.Exists(workspacePath))
                return workspacePath;
        }

        var templatePath = Path.Combine(TemplateFabriqPath, relativePath);
        if (Directory.Exists(templatePath))
            return templatePath;

        throw new DirectoryNotFoundException(
            "Gyotaq テンプレートディレクトリが見つかりません。\n" +
            "ワークスペースまたはアプリテンプレートにテンプレートが存在することを確認してください。");
    }

    /// <summary>
    /// uri_list.csv のパスを解決する（Dual Resolution）。
    /// 1. ワークスペース内 apps/digital_gyotaq_editor/material/uri_list.csv
    /// 2. フォールバック: アプリ同梱テンプレート
    /// </summary>
    private string ResolveUriListPath()
    {
        const string relativePath = @"apps\digital_gyotaq_editor\material\uri_list.csv";

        if (_workspace.RootPath is not null)
        {
            var workspacePath = Path.Combine(_workspace.RootPath, relativePath);
            if (File.Exists(workspacePath))
                return workspacePath;
        }

        var templatePath = Path.Combine(TemplateFabriqPath, relativePath);
        if (File.Exists(templatePath))
            return templatePath;

        throw new FileNotFoundException(
            "コマンドライブラリ (uri_list.csv) が見つかりません。\n" +
            "ワークスペースまたはアプリテンプレートにファイルが存在することを確認してください。");
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
