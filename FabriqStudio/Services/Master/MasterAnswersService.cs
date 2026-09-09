using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// 回答ファイルの読み書き。置き場は <c>profiles/&lt;マスタ名&gt;/master.json</c>
/// （データフォルダ直下の modules/ 以外はツール用に予約された名前空間。fabriq カーネルは見ない）。
/// 旧置き場 <c>profiles/&lt;マスタ名&gt;.master.json</c> は読めるが、保存時に新置き場へ移す。
/// </summary>
public sealed class MasterAnswersService : IMasterAnswersService
{
    /// <summary>旧置き場の拡張子（profiles/&lt;名&gt;.master.json）。</summary>
    public const string Suffix = ".master.json";

    /// <summary>新置き場のファイル名（profiles/&lt;名&gt;/master.json）。</summary>
    public const string FileName = "master.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        // 日本語をそのまま書く（\uXXXX にしない）。ファイルは人が読む前提。
        Encoder                     = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IWorkspaceService _workspace;

    public MasterAnswersService(IWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    private string ProfilesDir => Path.Combine(
        _workspace.RootPath ?? throw new InvalidOperationException(
            "ワークスペースが開かれていません。fabriq フォルダを選択してください。"),
        "profiles");

    public string GetAnswersPath(string masterName)
        => Path.Combine(ProfilesDir, masterName, FileName);

    private string LegacyPath(string masterName)
        => Path.Combine(ProfilesDir, masterName + Suffix);

    public bool Exists(string masterName)
        => MasterAnswers.IsValidMasterName(masterName)
           && (File.Exists(GetAnswersPath(masterName)) || File.Exists(LegacyPath(masterName)));

    public Task<IReadOnlyList<string>> ListMasterNamesAsync()
    {
        var dir = ProfilesDir;
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 新置き場: profiles/<名>/master.json
        foreach (var sub in Directory.GetDirectories(dir))
        {
            if (!File.Exists(Path.Combine(sub, FileName))) continue;
            var name = Path.GetFileName(sub);
            if (MasterAnswers.IsValidMasterName(name)) names.Add(name);
        }

        // 旧置き場: profiles/<名>.master.json
        foreach (var f in Directory.GetFiles(dir, "*" + Suffix, SearchOption.TopDirectoryOnly))
        {
            var file = Path.GetFileName(f);
            if (!file.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var name = file[..^Suffix.Length];
            if (MasterAnswers.IsValidMasterName(name)) names.Add(name);
        }

        IReadOnlyList<string> result = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult(result);
    }

    public async Task<MasterAnswers?> LoadAsync(string masterName)
    {
        var path = GetAnswersPath(masterName);
        if (!File.Exists(path)) path = LegacyPath(masterName);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        var answers = await JsonSerializer.DeserializeAsync<MasterAnswers>(stream, JsonOptions);
        if (answers is null) return null;

        // ファイル名／フォルダ名を正とする（手コピーでリネームされた場合の不整合を吸収）
        answers.MasterName = masterName;
        return answers;
    }

    public async Task SaveAsync(MasterAnswers answers)
    {
        if (!MasterAnswers.IsValidMasterName(answers.MasterName))
            throw new ArgumentException("マスタ名は半角英数字・アンダースコア・ハイフンのみ使用できます。");

        var path = GetAnswersPath(answers.MasterName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        if (string.IsNullOrEmpty(answers.CreatedAt)) answers.CreatedAt = now;
        answers.UpdatedAt = now;

        var json = JsonSerializer.Serialize(answers, JsonOptions);
        await File.WriteAllTextAsync(path, json);

        // 旧置き場が残っていれば移行（新置き場に書けた後で消す）
        var legacy = LegacyPath(answers.MasterName);
        if (File.Exists(legacy))
        {
            try { File.Delete(legacy); } catch { /* 消せなくても新置き場が正 */ }
        }
    }
}
