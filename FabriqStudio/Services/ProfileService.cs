using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

public class ProfileService : IProfileService
{
    private readonly IWorkspaceService _workspace;
    private readonly ICsvService       _csvService;

    public ProfileService(IWorkspaceService workspace, ICsvService csvService)
    {
        _workspace  = workspace;
        _csvService = csvService;
    }

    private string GetRoot() =>
        _workspace.RootPath
            ?? throw new InvalidOperationException(
                "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    public Task<IReadOnlyList<ProfileEntry>> GetProfilesAsync()
    {
        // ディレクトリスキャンは高速（数ms以下）なため Task.Run は不要。
        // Task.Run を使うと _wpftmp ビルドプロジェクトで戻り型推論が失敗するため避ける。
        var profilesDir = Path.Combine(GetRoot(), "profiles");

        if (!Directory.Exists(profilesDir))
            return Task.FromResult<IReadOnlyList<ProfileEntry>>(Array.Empty<ProfileEntry>());

        // profiles/ 直下の .csv のみ（easy_template/ 等のサブディレクトリは除外）
        IReadOnlyList<ProfileEntry> result = Directory
            .GetFiles(profilesDir, "*.csv", SearchOption.TopDirectoryOnly)
            .Select(f => new ProfileEntry
            {
                Name     = Path.GetFileNameWithoutExtension(f),
                FilePath = f
            })
            .OrderBy(p => p.Name)
            .ToArray();

        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<ProfileScriptEntry>> GetProfileModulesAsync(ProfileEntry profile)
    {
        // ProfileEntry.FilePath は絶対パスのため、RootPath からの相対パスに変換して
        // 既存の ICsvService.ReadAsync を再利用する。
        var relativePath = Path.GetRelativePath(GetRoot(), profile.FilePath);
        var modules      = await _csvService.ReadAsync<ProfileScriptEntry>(relativePath);

        // ファイルの記述順ではなく Order カラムで確実にソートして返す
        return modules.OrderBy(m => m.Order).ToList();
    }

    public async Task SaveProfileModulesAsync(ProfileEntry profile, IEnumerable<ProfileScriptEntry> modules)
    {
        // 渡された表示順で Order を 10 刻みで振り直し、その順番で書き込む
        var ordered = modules.ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = (i + 1) * 10;

        var relativePath = Path.GetRelativePath(GetRoot(), profile.FilePath);
        await _csvService.WriteAsync(relativePath, ordered);
    }

    public async Task<ProfileEntry> CreateProfileAsync(string profileName)
    {
        // ─── バリデーション ──────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("プロファイル名を入力してください。", nameof(profileName));

        // OS のファイル名禁則文字チェック
        if (profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(
                "ファイル名に使用できない文字が含まれています。", nameof(profileName));

        // .csv 拡張子を自動付与（大文字小文字問わず既にあればそのまま）
        var fileName = profileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? profileName
            : profileName + ".csv";

        var profilesDir = Path.Combine(GetRoot(), "profiles");
        var fullPath    = Path.Combine(profilesDir, fileName);

        if (File.Exists(fullPath))
            throw new InvalidOperationException(
                $"プロファイル「{Path.GetFileNameWithoutExtension(fileName)}」は既に存在します。");

        // ─── ファイル作成: ヘッダー行のみの空プロファイル ────────────
        // profiles/ ディレクトリが存在しない場合も作成する
        Directory.CreateDirectory(profilesDir);

        await Task.Run(() =>
        {
            // CsvService と同様に BOM 付き UTF-8 で書き込む
            using var writer = new StreamWriter(
                fullPath, append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // ヘッダー行だけを書き込む（Order,ScriptPath,Enabled,Description）
            csv.WriteHeader<ProfileScriptEntry>();
            csv.NextRecord();
        });

        return new ProfileEntry
        {
            Name     = Path.GetFileNameWithoutExtension(fileName),
            FilePath = fullPath
        };
    }
}
