namespace FabriqStudio.Services;

/// <summary>
/// ユーザーが選択した fabriq ルートディレクトリ（ワークスペース）を管理する。
/// アプリ起動時に前回のパスを復元し、変更時にイベントで通知する。
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// 現在開いているワークスペースの絶対パス。
    /// 未設定（初回起動 / 未選択）の場合は null。
    /// </summary>
    string? RootPath { get; }

    /// <summary>ワークスペースが有効に開かれているか。</summary>
    bool IsOpen { get; }

    /// <summary>
    /// ワークスペースが変更されたときに発火する。
    /// ViewModel はこのイベントを購読してデータを再ロードする。
    /// </summary>
    event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;

    /// <summary>
    /// 指定パスが fabriq ルートディレクトリとして有効かを検証する。
    /// </summary>
    /// <returns>エラーメッセージ（null = 検証 OK）</returns>
    string? Validate(string path);

    /// <summary>
    /// 指定パスを検証して現在のワークスペースとして設定し、
    /// <see cref="WorkspaceChanged"/> を発火する。
    /// </summary>
    /// <exception cref="ArgumentException">Validate が非 null を返した場合</exception>
    void Open(string path);

    /// <summary>
    /// %LOCALAPPDATA%\FabriqStudio\workspace.json に保存された前回のパスを復元する。
    /// パスが存在しないか検証 NG の場合は無視して未設定のまま。
    /// このメソッドは WorkspaceChanged を発火しない（VM 構築前に呼ばれるため）。
    /// </summary>
    void TryRestorePersisted();
}

/// <summary>ワークスペース変更イベントの引数。</summary>
public sealed class WorkspaceChangedEventArgs : EventArgs
{
    /// <summary>新しいパス（null = クローズ）</summary>
    public string? NewPath { get; }

    /// <summary>変更前のパス（null = 未設定から変更）</summary>
    public string? OldPath { get; }

    public WorkspaceChangedEventArgs(string? newPath, string? oldPath)
        => (NewPath, OldPath) = (newPath, oldPath);
}
