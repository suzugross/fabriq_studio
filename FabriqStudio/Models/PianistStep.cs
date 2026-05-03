using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// procedure.csv の 1 行を表すモデル。
/// カラム順序は CSV と一致: PhaseID, PhaseLabel, Color, StepNo, Action, Value, Wait, Note, Screenshot
///
/// Action / Color は CSV には英名で書く（pianist.ps1 の switch が英名前提）。
/// Studio UI は日本語ラベルで提示し、保存時に英名へ書き戻す（変換は ViewModel 側）。
///
/// Wait 列は Action によって意味が変わる（§4.4）:
///   - WaitWin: タイムアウト ms
///   - Wait   : 停止 ms（ただし Value 列が優先）
///   - その他 : 後続待機 ms
/// CSV スキーマは触らず、Studio UI で吸収する（pianist 改修なし）。
/// </summary>
public partial class PianistStep : ObservableObject
{
    [ObservableProperty] private string _phaseID    = "";
    [ObservableProperty] private string _phaseLabel = "";
    [ObservableProperty] private string _color      = "";
    [ObservableProperty] private int    _stepNo;
    [ObservableProperty] private string _action     = "";
    [ObservableProperty] private string _value      = "";
    [ObservableProperty] private string _wait       = "";
    [ObservableProperty] private string _note       = "";
    [ObservableProperty] private string _screenshot = "";
}
