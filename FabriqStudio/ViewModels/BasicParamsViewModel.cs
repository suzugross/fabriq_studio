using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 基本パラメータモード（Phase 1 はプレースホルダー）
/// </summary>
public partial class BasicParamsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = "基本パラメータモードは今後実装予定です。";
}
