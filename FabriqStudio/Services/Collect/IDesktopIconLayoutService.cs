namespace FabriqStudio.Services.Collect;

/// <summary>デスクトップのアイコン配置（現在ユーザー）を .reg に書き出す。fabriq の desktop_icon_config Backup と同じキー・形式。</summary>
public interface IDesktopIconLayoutService
{
    /// <summary>成功したら true。キーが無い（アイコンを並べたことがない）ときは false。</summary>
    Task<bool> ExportAsync(string destFile, CancellationToken ct = default);
}
