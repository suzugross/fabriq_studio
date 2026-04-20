namespace FabriqStudio.Models;

/// <summary>
/// <see cref="Services.IPrinterDriverDetectorService.ExportToWorkspaceAsync"/> の結果。
/// レジストリ辞書の <see cref="Services.ExportResult"/> と同じ意味論（0/1 件単位）。
/// </summary>
public sealed class DriverExportResult
{
    /// <summary>新規追加された行数（0 または 1）。</summary>
    public int Added   { get; init; }

    /// <summary>DriverName 重複によりスキップされた行数（0 または 1）。</summary>
    public int Skipped { get; init; }

    /// <summary>エラーメッセージ。null = 正常終了。</summary>
    public string? Error { get; init; }
}
