namespace FabriqStudio.Models;

/// <summary>
/// HostDetailView の「プリンター」DataGrid 用に
/// HostEntry のフラットなプリンターフィールドを行単位に変換したモデル。
/// </summary>
public class PrinterInfo
{
    public int    Number { get; set; }
    public string Name   { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Port   { get; set; } = "";
}
