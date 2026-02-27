using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CsvHelper.Configuration.Attributes;

namespace FabriqStudio.Models;

/// <summary>
/// looper_list.csv の 1 行を表すモデル。
/// カラム: Enabled, ScriptPath, MaxRetry, IntervalSec, Condition, Description, Segment
/// </summary>
public partial class LooperEntry : ObservableObject, INotifyDataErrorInfo
{
    // ── 定数 ─────────────────────────────────────────────────────────────

    public const string ConditionOnError = "OnError";
    public const string ConditionAlways  = "Always";

    /// <summary>UI の ComboBox に表示する全 Condition 一覧。</summary>
    public static readonly IReadOnlyList<string> AllConditions =
        [ConditionOnError, ConditionAlways];

    // ── プロパティ ───────────────────────────────────────────────────────

    /// <summary>有効フラグ（1=実行, 0=スキップ）</summary>
    [ObservableProperty] private int _enabled;

    /// <summary>対象スクリプトのパス（fabriqルート相対 or 絶対パス）</summary>
    [ObservableProperty] private string _scriptPath = "";

    /// <summary>最大実行回数（1以上）。無限ループ防止の安全装置。</summary>
    [ObservableProperty] private int _maxRetry = 1;

    /// <summary>リトライ間隔（秒, 0以上）</summary>
    [ObservableProperty] private int _intervalSec;

    /// <summary>リトライ条件（"OnError" or "Always"）</summary>
    [ObservableProperty] private string _condition = "OnError";

    /// <summary>管理用の説明文（コンソール表示にも使用）</summary>
    [ObservableProperty] private string _description = "";

    /// <summary>実行セグメント（fabriq のセグメント分離機能で使用）</summary>
    [ObservableProperty] [property: Optional] private string _segment = "";

    // ── INotifyDataErrorInfo ──────────────────────────────────────────────

    private readonly Dictionary<string, List<string>> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null) return _errors.Values.SelectMany(e => e);
        return _errors.TryGetValue(propertyName, out var errs) ? errs : [];
    }

    /// <summary>MaxRetry が変化するたびに 1 以上チェックを行う。</summary>
    partial void OnMaxRetryChanged(int value)
    {
        const string prop = nameof(MaxRetry);
        if (value < 1)
        {
            _errors[prop] = ["MaxRetry は 1 以上の値を指定してください。"];
        }
        else
        {
            _errors.Remove(prop);
        }
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(prop));
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>IntervalSec が変化するたびに 0 以上チェックを行う。</summary>
    partial void OnIntervalSecChanged(int value)
    {
        const string prop = nameof(IntervalSec);
        if (value < 0)
        {
            _errors[prop] = ["IntervalSec は 0 以上の値を指定してください。"];
        }
        else
        {
            _errors.Remove(prop);
        }
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(prop));
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>Condition が変化するたびに OnError / Always チェックを行う。</summary>
    partial void OnConditionChanged(string value)
    {
        const string prop = nameof(Condition);
        if (value is not ("OnError" or "Always"))
        {
            _errors[prop] = ["Condition は OnError または Always を指定してください。"];
        }
        else
        {
            _errors.Remove(prop);
        }
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(prop));
        OnPropertyChanged(nameof(HasErrors));
    }
}
