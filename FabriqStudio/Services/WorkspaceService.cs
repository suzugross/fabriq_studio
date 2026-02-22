using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabriqStudio.Services;

/// <summary>
/// fabriq ワークスペース（ルートディレクトリ）の管理実装。
/// <para>
/// 永続化先: &lt;exe と同じフォルダ&gt;\config\workspace.json
/// （ポータブル運用対応: AppDomain.CurrentDomain.BaseDirectory を起点とする）
/// </para>
/// </summary>
public class WorkspaceService : IWorkspaceService
{
    // ── 永続化パス ────────────────────────────────────────────────────────────

    private static readonly string PersistPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "config",
        "workspace.json");

    // ── 状態 ─────────────────────────────────────────────────────────────────

    private string? _rootPath;

    public string? RootPath => _rootPath;
    public bool    IsOpen   => _rootPath is not null;

    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;

    // ── バリデーション ────────────────────────────────────────────────────────

    /// <summary>
    /// fabriq ルートディレクトリの判定条件:
    ///   必須: kernel/ ディレクトリが存在する（fabriq コア関数群）
    ///   必須: modules/ ディレクトリが存在する（モジュール格納先）
    /// </summary>
    public string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "フォルダが指定されていません。";

        if (!Directory.Exists(path))
            return "指定されたフォルダが存在しません。";

        if (!Directory.Exists(Path.Combine(path, "kernel")))
            return "fabriq のルートフォルダではありません。\n（kernel/ フォルダが見つかりません）";

        if (!Directory.Exists(Path.Combine(path, "modules")))
            return "fabriq のルートフォルダではありません。\n（modules/ フォルダが見つかりません）";

        return null; // OK
    }

    // ── Open ─────────────────────────────────────────────────────────────────

    public void Open(string path)
    {
        var error = Validate(path);
        if (error is not null)
            throw new ArgumentException(error, nameof(path));

        var old   = _rootPath;
        _rootPath = path.TrimEnd('\\', '/');

        Persist(_rootPath);
        WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs(_rootPath, old));
    }

    // ── Close ────────────────────────────────────────────────────────────────

    public void Close()
    {
        var old   = _rootPath;
        _rootPath = null;
        ClearPersisted();
        WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs(null, old));
    }

    // ── Reload ───────────────────────────────────────────────────────────────

    public void Reload()
    {
        if (_rootPath is null) return;
        WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs(_rootPath, _rootPath));
    }

    // ── 永続化復元 ────────────────────────────────────────────────────────────

    /// <summary>
    /// VM 構築前（App.OnStartup）に呼び出すこと。
    /// WorkspaceChanged は発火しない — VM は自身のコンストラクタで IsOpen を確認する。
    /// </summary>
    public void TryRestorePersisted()
    {
        try
        {
            if (!File.Exists(PersistPath)) return;

            var json = File.ReadAllText(PersistPath);
            var data = JsonSerializer.Deserialize<WorkspacePersistData>(json);
            if (data?.RootPath is null) return;

            // 検証 OK の場合のみサイレントに復元（イベント発火なし）
            if (Validate(data.RootPath) is null)
                _rootPath = data.RootPath.TrimEnd('\\', '/');
        }
        catch
        {
            // 読み込み失敗は無視（初回起動扱い）
        }
    }

    // ── 永続化書き込み ────────────────────────────────────────────────────────

    private static void Persist(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PersistPath)!);
            var json = JsonSerializer.Serialize(
                new WorkspacePersistData { RootPath = path },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PersistPath, json);
        }
        catch
        {
            // 永続化に失敗しても動作は継続
        }
    }

    // ── 永続化クリア ─────────────────────────────────────────────────────────

    private static void ClearPersisted()
    {
        try
        {
            if (File.Exists(PersistPath))
                File.Delete(PersistPath);
        }
        catch
        {
            // クリアに失敗しても動作は継続
        }
    }

    // ── テンプレートから新規作成 ──────────────────────────────────────────────

    /// <summary>
    /// ビルド出力に含まれるテンプレートフォルダのパス。
    /// template/template_fabriq/fabriq/ の中身を targetPath に再帰コピーする。
    /// </summary>
    private static readonly string TemplateFabriqPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "template", "template_fabriq", "fabriq");

    /// <summary>
    /// ディレクトリ再帰コピー時にスキップするフォルダ名。
    /// .git はバージョン管理メタデータ、.claude / dev は fabriq 開発用途のため除外。
    /// </summary>
    private static readonly HashSet<string> ExcludedDirNames =
        new(StringComparer.OrdinalIgnoreCase) { ".git", ".claude", "dev" };

    public async Task<string?> CreateFromTemplateAsync(string targetPath)
    {
        if (!Directory.Exists(TemplateFabriqPath))
            return "テンプレートフォルダが見つかりません。\n" +
                   "アプリケーションを再インストールしてください。\n" +
                   $"（{TemplateFabriqPath}）";

        try
        {
            await Task.Run(() => CopyDirectoryRecursive(TemplateFabriqPath, targetPath));
            return null; // 成功
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"アクセスが拒否されました。\n{ex.Message}";
        }
        catch (IOException ex)
        {
            return $"コピー中にエラーが発生しました。\n{ex.Message}";
        }
        catch (Exception ex)
        {
            return $"予期しないエラーが発生しました。\n{ex.Message}";
        }
    }

    private static void CopyDirectoryRecursive(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);

        foreach (var dir in Directory.GetDirectories(source))
        {
            var dirName = Path.GetFileName(dir);
            if (ExcludedDirNames.Contains(dirName)) continue;
            CopyDirectoryRecursive(dir, Path.Combine(target, dirName));
        }
    }

    // ── 内部 DTO ──────────────────────────────────────────────────────────────

    private sealed class WorkspacePersistData
    {
        [JsonPropertyName("rootPath")]
        public string? RootPath { get; set; }
    }
}
