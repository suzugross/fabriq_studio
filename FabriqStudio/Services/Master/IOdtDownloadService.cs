namespace FabriqStudio.Services.Master;

/// <summary>ODT のダウンロードモード（setup.exe /download）の実行結果。</summary>
public sealed class OdtDownloadResult
{
    public bool    Success         { get; init; }
    public int     ExitCode        { get; init; }
    public string  Message         { get; init; } = "";
    /// <summary>取得できた Office\Data\&lt;version&gt; のバージョン（無ければ null）。</summary>
    public string? DataVersion     { get; init; }
    /// <summary>実行に使ったダウンロード用 XML の絶対パス。</summary>
    public string? DownloadXmlPath { get; init; }
}

/// <summary>
/// Office Deployment Tool の setup.exe を子プロセスで実行し、オフライン資材（Office\）を
/// 製品フォルダ直下へダウンロードする。管理者権限は不要。インターネット接続が必要。
/// </summary>
public interface IOdtDownloadService
{
    /// <param name="setupExePath">odt_config/assets/setup.exe の絶対パス。</param>
    /// <param name="productFolder">configuration.xml のあるフォルダ（ここに Office\ が作られる）。</param>
    /// <param name="configurationXml">生成済み configuration.xml の内容（SourcePath は本サービスが付ける）。</param>
    /// <param name="progress">経過表示（経過時間・フォルダサイズ）。</param>
    Task<OdtDownloadResult> DownloadAsync(
        string setupExePath, string productFolder, string configurationXml,
        IProgress<string>? progress, CancellationToken ct);
}
