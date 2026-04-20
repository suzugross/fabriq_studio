namespace FabriqStudio.Models;

/// <summary>
/// INF 解析で検出された 1 つのプリンタドライバ（モデル名）を表す DTO。
/// <para>
/// <see cref="DriverName"/> は INF ファイル内の <c>[*.NTamd64]</c> セクションに記載されている
/// モデル名文字列そのもの（例: <c>"EPSON PX-S505 Series"</c>）。
/// fabriq の <c>printer_driver_list.csv</c> の <c>DriverName</c> カラムにそのまま転記できる値。
/// </para>
/// </summary>
public sealed class PrinterDriverInfo
{
    /// <summary>INF 内の <c>"Model" =</c> 左辺から抽出したモデル名。</summary>
    public string DriverName   { get; init; } = "";

    /// <summary>INF ファイルの短い名前（例: <c>E1WF1CAJ.INF</c>）。</summary>
    public string InfFileName  { get; init; } = "";

    /// <summary>INF ファイルの絶対パス。</summary>
    public string InfFilePath  { get; init; } = "";

    /// <summary>スキャン起点ディレクトリ直下のトップフォルダ名（例: <c>EPSON PX-S505 Series</c>）。</summary>
    public string FolderName   { get; init; } = "";

    /// <summary>対応アーキテクチャ（現状は <c>NTamd64</c> 固定）。</summary>
    public string Architecture { get; init; } = "";
}
