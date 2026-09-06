namespace FabriqStudio.Models.Master;

/// <summary>ドロップ枠へ資材を配置した結果。1 ファイル（またはフォルダ）= 1 エントリ。</summary>
public sealed class AssetDropResult
{
    public List<AssetDropEntry> Entries { get; } = [];
    public List<string>         Errors  { get; } = [];
    public List<string>         Skipped { get; } = [];

    /// <summary>配置先フォルダ（ワークスペースルートからの相対、表示用）。</summary>
    public string TargetRelPath { get; set; } = "";
}

public sealed class AssetDropEntry
{
    /// <summary>配置後のファイル名（フォルダの場合はフォルダ名）。</summary>
    public string FileName { get; init; } = "";
    public bool   IsFolder { get; init; }

    /// <summary>インストーラの補完結果（installer 種別のみ）。</summary>
    public string  AppName    { get; init; } = "";
    public string  Type       { get; init; } = "";
    public string  SilentArgs { get; init; } = "";
    public string  Version    { get; init; } = "";
    /// <summary>引数の出所（カタログ名 / Inno Setup / NSIS / InstallShield / MSI / 要確認）。</summary>
    public string  Source     { get; init; } = "";

    /// <summary>プリンタドライバ（printerDriver 種別）: INF から抽出したモデル名。</summary>
    public List<string> DriverNames { get; init; } = [];
}

/// <summary>インストーラ 1 本に対する補完提案。</summary>
public sealed class InstallerSuggestion
{
    public string AppName    { get; init; } = "";
    public string Type       { get; init; } = "exe";
    public string SilentArgs { get; init; } = "";
    public string Version    { get; init; } = "";
    public string Source     { get; init; } = "";
    /// <summary>引数の確度が低い（要確認バッジ）。</summary>
    public bool   NeedsReview { get; init; }
}
