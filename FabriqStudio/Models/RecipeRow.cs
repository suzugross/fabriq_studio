using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// AutoKey レシピの 1 ステップを表すモデル。
/// CsvHelper の列名マッピング（PascalCase）と ObservableObject を兼用する。
/// Wait 列は INotifyDataErrorInfo で負値を検証し、DataGrid に赤枠を表示する。
/// </summary>
public partial class RecipeRow : ObservableObject, INotifyDataErrorInfo
{
    /// <summary>実行順序（1始まり）。保存・エクスポート時に VM が連番で振り直す。</summary>
    [ObservableProperty] private int    _step;

    /// <summary>アクション種別。ActionType 定数を参照。</summary>
    [ObservableProperty] private string _action = ActionType.Open;

    /// <summary>アクションのパラメータ（パス / ウィンドウタイトル / テキスト / キー記法 / 待機ms）。</summary>
    [ObservableProperty] private string _value = "";

    /// <summary>アクション後の待機時間 (ms)。WaitWin では最大待機タイムアウト。</summary>
    [ObservableProperty] private int    _wait;

    /// <summary>メモ・説明。</summary>
    [ObservableProperty] private string _note = "";

    // ── INotifyDataErrorInfo ──────────────────────────────────────────────

    private readonly Dictionary<string, List<string>> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null) return _errors.Values.SelectMany(e => e);
        return _errors.TryGetValue(propertyName, out var errs) ? errs : [];
    }

    /// <summary>Wait が変化するたびに負値チェックを行う。</summary>
    partial void OnWaitChanged(int value)
    {
        const string prop = nameof(Wait);
        if (value < 0)
        {
            _errors[prop] = ["Wait は 0 以上の値を指定してください。"];
        }
        else
        {
            _errors.Remove(prop);
        }
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(prop));
        OnPropertyChanged(nameof(HasErrors));
    }
}
