using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// HostDetailView の「プリンター」DataGrid 用に
/// HostEntry のフラットなプリンターフィールドを行単位に変換したモデル。
/// ObservableObject を継承し、DataGrid での双方向バインディングをサポートする。
/// </summary>
public partial class PrinterInfo : ObservableObject
{
    [ObservableProperty] private int    _number;
    [ObservableProperty] private string _name   = "";
    [ObservableProperty] private string _driver = "";
    [ObservableProperty] private string _port   = "";
}
