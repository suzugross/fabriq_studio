using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// kernel/csv/hostlist.csv の1行を表すモデル（全43カラム）
/// ObservableObject を継承することで WPF TwoWay バインドと
/// PropertyChanged 通知（per-field Dirty 検知）を実現する。
/// </summary>
public partial class HostEntry : ObservableObject
{
    // --- 基本情報 ---
    [ObservableProperty] private string _adminID         = "";
    [ObservableProperty] private string _oldPCName       = "";
    [ObservableProperty] private string _newPCName       = "";

    // --- 有線LAN ---
    [ObservableProperty] private string _ethernetIP      = "";
    [ObservableProperty] private string _ethernetSubnet  = "";
    [ObservableProperty] private string _ethernetGateway = "";

    // --- 無線LAN ---
    [ObservableProperty] private string _wifiIP          = "";
    [ObservableProperty] private string _wifiSubnet      = "";
    [ObservableProperty] private string _wifiGateway     = "";

    // --- DNS ---
    [ObservableProperty] private string _dNS1            = "";
    [ObservableProperty] private string _dNS2            = "";
    [ObservableProperty] private string _dNS3            = "";
    [ObservableProperty] private string _dNS4            = "";

    // --- プリンター 1〜10 ---
    [ObservableProperty] private string _printer1Name    = "";
    [ObservableProperty] private string _printer1Driver  = "";
    [ObservableProperty] private string _printer1Port    = "";

    [ObservableProperty] private string _printer2Name    = "";
    [ObservableProperty] private string _printer2Driver  = "";
    [ObservableProperty] private string _printer2Port    = "";

    [ObservableProperty] private string _printer3Name    = "";
    [ObservableProperty] private string _printer3Driver  = "";
    [ObservableProperty] private string _printer3Port    = "";

    [ObservableProperty] private string _printer4Name    = "";
    [ObservableProperty] private string _printer4Driver  = "";
    [ObservableProperty] private string _printer4Port    = "";

    [ObservableProperty] private string _printer5Name    = "";
    [ObservableProperty] private string _printer5Driver  = "";
    [ObservableProperty] private string _printer5Port    = "";

    [ObservableProperty] private string _printer6Name    = "";
    [ObservableProperty] private string _printer6Driver  = "";
    [ObservableProperty] private string _printer6Port    = "";

    [ObservableProperty] private string _printer7Name    = "";
    [ObservableProperty] private string _printer7Driver  = "";
    [ObservableProperty] private string _printer7Port    = "";

    [ObservableProperty] private string _printer8Name    = "";
    [ObservableProperty] private string _printer8Driver  = "";
    [ObservableProperty] private string _printer8Port    = "";

    [ObservableProperty] private string _printer9Name    = "";
    [ObservableProperty] private string _printer9Driver  = "";
    [ObservableProperty] private string _printer9Port    = "";

    [ObservableProperty] private string _printer10Name   = "";
    [ObservableProperty] private string _printer10Driver = "";
    [ObservableProperty] private string _printer10Port   = "";

    // ────────────────────────────────────────────────────────────
    /// <summary>全フィールドのディープコピーを返す（Dirty 検知スナップショット用）。</summary>
    public HostEntry Clone() => new()
    {
        AdminID         = AdminID,
        OldPCName       = OldPCName,
        NewPCName       = NewPCName,
        EthernetIP      = EthernetIP,
        EthernetSubnet  = EthernetSubnet,
        EthernetGateway = EthernetGateway,
        WifiIP          = WifiIP,
        WifiSubnet      = WifiSubnet,
        WifiGateway     = WifiGateway,
        DNS1            = DNS1,
        DNS2            = DNS2,
        DNS3            = DNS3,
        DNS4            = DNS4,
        Printer1Name    = Printer1Name,  Printer1Driver  = Printer1Driver,  Printer1Port  = Printer1Port,
        Printer2Name    = Printer2Name,  Printer2Driver  = Printer2Driver,  Printer2Port  = Printer2Port,
        Printer3Name    = Printer3Name,  Printer3Driver  = Printer3Driver,  Printer3Port  = Printer3Port,
        Printer4Name    = Printer4Name,  Printer4Driver  = Printer4Driver,  Printer4Port  = Printer4Port,
        Printer5Name    = Printer5Name,  Printer5Driver  = Printer5Driver,  Printer5Port  = Printer5Port,
        Printer6Name    = Printer6Name,  Printer6Driver  = Printer6Driver,  Printer6Port  = Printer6Port,
        Printer7Name    = Printer7Name,  Printer7Driver  = Printer7Driver,  Printer7Port  = Printer7Port,
        Printer8Name    = Printer8Name,  Printer8Driver  = Printer8Driver,  Printer8Port  = Printer8Port,
        Printer9Name    = Printer9Name,  Printer9Driver  = Printer9Driver,  Printer9Port  = Printer9Port,
        Printer10Name   = Printer10Name, Printer10Driver = Printer10Driver, Printer10Port = Printer10Port,
    };

    /// <summary>全フィールドの値が等しいか比較する（Dirty リセット判定用）。</summary>
    public bool ContentEquals(HostEntry other) =>
        AdminID         == other.AdminID         &&
        OldPCName       == other.OldPCName       &&
        NewPCName       == other.NewPCName       &&
        EthernetIP      == other.EthernetIP      &&
        EthernetSubnet  == other.EthernetSubnet  &&
        EthernetGateway == other.EthernetGateway &&
        WifiIP          == other.WifiIP          &&
        WifiSubnet      == other.WifiSubnet      &&
        WifiGateway     == other.WifiGateway     &&
        DNS1            == other.DNS1            &&
        DNS2            == other.DNS2            &&
        DNS3            == other.DNS3            &&
        DNS4            == other.DNS4            &&
        Printer1Name  == other.Printer1Name  && Printer1Driver  == other.Printer1Driver  && Printer1Port  == other.Printer1Port  &&
        Printer2Name  == other.Printer2Name  && Printer2Driver  == other.Printer2Driver  && Printer2Port  == other.Printer2Port  &&
        Printer3Name  == other.Printer3Name  && Printer3Driver  == other.Printer3Driver  && Printer3Port  == other.Printer3Port  &&
        Printer4Name  == other.Printer4Name  && Printer4Driver  == other.Printer4Driver  && Printer4Port  == other.Printer4Port  &&
        Printer5Name  == other.Printer5Name  && Printer5Driver  == other.Printer5Driver  && Printer5Port  == other.Printer5Port  &&
        Printer6Name  == other.Printer6Name  && Printer6Driver  == other.Printer6Driver  && Printer6Port  == other.Printer6Port  &&
        Printer7Name  == other.Printer7Name  && Printer7Driver  == other.Printer7Driver  && Printer7Port  == other.Printer7Port  &&
        Printer8Name  == other.Printer8Name  && Printer8Driver  == other.Printer8Driver  && Printer8Port  == other.Printer8Port  &&
        Printer9Name  == other.Printer9Name  && Printer9Driver  == other.Printer9Driver  && Printer9Port  == other.Printer9Port  &&
        Printer10Name == other.Printer10Name && Printer10Driver == other.Printer10Driver && Printer10Port == other.Printer10Port;
}
