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
    // JSON シリアライズを使うことで、フィールド追加時に Clone/ContentEquals を修正不要にする。
    // System.Text.Json は [ObservableProperty] が生成した public プロパティを自動で対象にする。

    /// <summary>全フィールドのディープコピーを返す（Dirty 検知スナップショット用）。</summary>
    public HostEntry Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<HostEntry>(
            System.Text.Json.JsonSerializer.Serialize(this))!;

    /// <summary>全フィールドの値が等しいか比較する（Dirty リセット判定用）。</summary>
    public bool ContentEquals(HostEntry other) =>
        System.Text.Json.JsonSerializer.Serialize(this) ==
        System.Text.Json.JsonSerializer.Serialize(other);
}
