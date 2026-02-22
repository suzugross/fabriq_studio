namespace FabriqStudio.Models;

/// <summary>
/// kernel/csv/hostlist.csv の1行を表すモデル（全43カラム）
/// </summary>
public class HostEntry
{
    // --- 基本情報 ---
    public string AdminID          { get; set; } = "";
    public string OldPCName        { get; set; } = "";
    public string NewPCName        { get; set; } = "";

    // --- 有線LAN ---
    public string EthernetIP       { get; set; } = "";
    public string EthernetSubnet   { get; set; } = "";
    public string EthernetGateway  { get; set; } = "";

    // --- 無線LAN ---
    public string WifiIP           { get; set; } = "";
    public string WifiSubnet       { get; set; } = "";
    public string WifiGateway      { get; set; } = "";

    // --- DNS ---
    public string DNS1             { get; set; } = "";
    public string DNS2             { get; set; } = "";
    public string DNS3             { get; set; } = "";
    public string DNS4             { get; set; } = "";

    // --- プリンター 1〜10 ---
    public string Printer1Name     { get; set; } = "";
    public string Printer1Driver   { get; set; } = "";
    public string Printer1Port     { get; set; } = "";

    public string Printer2Name     { get; set; } = "";
    public string Printer2Driver   { get; set; } = "";
    public string Printer2Port     { get; set; } = "";

    public string Printer3Name     { get; set; } = "";
    public string Printer3Driver   { get; set; } = "";
    public string Printer3Port     { get; set; } = "";

    public string Printer4Name     { get; set; } = "";
    public string Printer4Driver   { get; set; } = "";
    public string Printer4Port     { get; set; } = "";

    public string Printer5Name     { get; set; } = "";
    public string Printer5Driver   { get; set; } = "";
    public string Printer5Port     { get; set; } = "";

    public string Printer6Name     { get; set; } = "";
    public string Printer6Driver   { get; set; } = "";
    public string Printer6Port     { get; set; } = "";

    public string Printer7Name     { get; set; } = "";
    public string Printer7Driver   { get; set; } = "";
    public string Printer7Port     { get; set; } = "";

    public string Printer8Name     { get; set; } = "";
    public string Printer8Driver   { get; set; } = "";
    public string Printer8Port     { get; set; } = "";

    public string Printer9Name     { get; set; } = "";
    public string Printer9Driver   { get; set; } = "";
    public string Printer9Port     { get; set; } = "";

    public string Printer10Name    { get; set; } = "";
    public string Printer10Driver  { get; set; } = "";
    public string Printer10Port    { get; set; } = "";
}
