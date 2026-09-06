using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

public sealed class MasterAnswersService : IMasterAnswersService
{
    public const string Suffix = ".master.json";

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
        => Path.Combine(ProfilesDir, masterName + Suffix);

    public bool Exists(string masterName)
        => MasterAnswers.IsValidMasterName(masterName) && File.Exists(GetAnswersPath(masterName));

    public Task<IReadOnlyList<string>> ListMasterNamesAsync()
    {
        var dir = ProfilesDir;
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        IReadOnlyList<string> names = Directory
            .GetFiles(dir, "*" + Suffix, SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileName(f))
            .Where(f => f.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
            .Select(f => f[..^Suffix.Length])
            .Where(MasterAnswers.IsValidMasterName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(names);
    }

    public async Task<MasterAnswers?> LoadAsync(string masterName)
    {
        var path = GetAnswersPath(masterName);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        var answers = await JsonSerializer.DeserializeAsync<MasterAnswers>(stream, JsonOptions);
        if (answers is null) return null;

        // ファイル名を正とする（手コピーでリネームされた場合の不整合を吸収）
        answers.MasterName = masterName;
        return answers;
    }

    public async Task SaveAsync(MasterAnswers answers)
    {
        if (!MasterAnswers.IsValidMasterName(answers.MasterName))
            throw new ArgumentException("マスタ名は半角英数字・アンダースコア・ハイフンのみ使用できます。");

        Directory.CreateDirectory(ProfilesDir);

        var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        if (string.IsNullOrEmpty(answers.CreatedAt)) answers.CreatedAt = now;
        answers.UpdatedAt = now;

        var json = JsonSerializer.Serialize(answers, JsonOptions);
        await File.WriteAllTextAsync(GetAnswersPath(answers.MasterName), json);
    }
}
