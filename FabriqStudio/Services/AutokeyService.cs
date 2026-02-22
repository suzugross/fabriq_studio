using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class AutokeyService : IAutokeyService
{
    private readonly IWorkspaceService _workspace;

    public AutokeyService(IWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    private string GetRoot() =>
        _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    // ── Load ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RecipeRow>> LoadRecipeAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
            return (IReadOnlyList<RecipeRow>)csv.GetRecords<RecipeRow>().ToList();
        });
    }

    // ── Save (絶対パス指定) ──────────────────────────────────────────────────

    public async Task SaveRecipeAsync(string filePath, IEnumerable<RecipeRow> rows)
    {
        var list = rows.ToList();
        for (int i = 0; i < list.Count; i++)
            list[i].Step = i + 1;

        await Task.Run(() =>
        {
            using var writer = new StreamWriter(filePath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteRecords(list);
        });
    }

    // ── Export ───────────────────────────────────────────────────────────────

    public async Task<string> ExportModuleAsync(
        string                moduleName,
        IEnumerable<RecipeRow> rows,
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
        var fabriqRoot  = GetRoot();
        var destDir     = Path.Combine(fabriqRoot, "modules", "extended", sanitized);

        // 3. 既存チェック
        if (Directory.Exists(destDir))
        {
            if (!overwrite)
                throw new InvalidOperationException($"モジュール「{sanitized}」は既に存在します。");

            await Task.Run(() => Directory.Delete(destDir, recursive: true));
        }

        await Task.Run(() => Directory.CreateDirectory(destDir));

        // 4. autokey_config.ps1 テンプレートをコピー（リネーム）
        var templateDir    = Path.Combine(fabriqRoot, "apps", "autokey_recipe_editor", "autokey_template");
        var templateScript = Path.Combine(templateDir, "autokey_config.ps1");
        var destScriptName = $"{sanitized}_autokey_config.ps1";
        var destScript     = Path.Combine(destDir, destScriptName);
        await Task.Run(() => File.Copy(templateScript, destScript, overwrite: true));

        // 5. module.csv を生成
        var moduleCsvPath = Path.Combine(destDir, "module.csv");
        await Task.Run(() =>
        {
            using var writer = new StreamWriter(moduleCsvPath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("MenuName,Category,Script,Order,Enabled");
            writer.WriteLine($"{sanitized},Automation,{destScriptName},50,0");
        });

        // 6. recipe.csv を保存
        var recipePath = Path.Combine(destDir, "recipe.csv");
        await SaveRecipeAsync(recipePath, rows);

        return destDir;
    }

    // ── Test Run ─────────────────────────────────────────────────────────────
    // TODO: autokey_config.ps1 は fabriq カーネル関数（New-ModuleResult 等）に依存しており、
    //       fabriq 基盤外では単独実行できない。将来的にカーネル統合後に実装する。

    public Task TestRunAsync(IEnumerable<RecipeRow> rows)
    {
        throw new NotSupportedException(
            "テスト実行は現在未対応です。\n" +
            "エクスポート後、fabriq 環境からモジュールを実行して動作確認してください。");
    }
}
