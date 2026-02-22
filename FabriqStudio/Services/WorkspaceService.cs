using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabriqStudio.Services;

/// <summary>
/// fabriq ワークスペース（ルートディレクトリ）の管理実装。
/// <para>
/// 永続化先: %LOCALAPPDATA%\FabriqStudio\workspace.json
/// </para>
/// </summary>
public class WorkspaceService : IWorkspaceService
{
    // ── 永続化パス ────────────────────────────────────────────────────────────

    private static readonly string PersistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FabriqStudio",
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

    // ── 内部 DTO ──────────────────────────────────────────────────────────────

    private sealed class WorkspacePersistData
    {
        [JsonPropertyName("rootPath")]
        public string? RootPath { get; set; }
    }
}
