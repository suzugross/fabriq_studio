using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// Variables sub-tab の 1 行（変数 1 つ）の編集状態。
///
/// values.csv の VariableColumns を絶対参照源として、各列に対し以下を保持する:
/// <list type="bullet">
///   <item><see cref="IsIncluded"/>: instructions/&lt;PhaseID&gt;.txt の <c>[Variables]</c> section に
///   この変数名を書き出すか（チェックボックス相当）</item>
///   <item><see cref="IsAutoDiscovered"/>: 同 Phase の procedure.csv で
///   <c>$&lt;Name&gt;</c> として参照されているか（pianist は auto-union するので [Variables]
///   に書かなくても Copy Values に出る、という情報バッジ用）</item>
///   <item><see cref="SampleValue"/>: values.csv の <c>*</c> 行のセル値プレビュー
///   （内容を一目で確認できるよう）</item>
///   <item><see cref="IsOrphan"/>: 既存の [Variables] section にあるが values.csv に列が
///   存在しない孤児（タイポ or 列削除残骸）。orphan セクションに分離表示する</item>
/// </list>
///
/// シリアライズ規則: <see cref="IsIncluded"/> = true のエントリの <see cref="Name"/> を
/// [Variables] セクションへ 1 行 1 変数で書き出す（auto / 通常 / orphan 問わず一律）。
/// pianist 側は auto-union するので auto 変数を [Variables] に書いても書かなくても挙動同じ
/// だが、ユーザの明示的な意思を尊重して round-trip を破壊しない。
/// </summary>
public partial class PianistVariableSelection : ObservableObject
{
    /// <summary>変数名。values.csv の列名（NewPCName 以外）または [Variables] section の宣言名。</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// [Variables] section に書き出すか。チェックボックスの双方向バインドターゲット。
    /// 変更時に ViewModel が raw 再シリアライズ + Dirty 化する。
    /// </summary>
    [ObservableProperty] private bool _isIncluded;

    /// <summary>
    /// 同 Phase の procedure.csv 内 Value/Note 列に <c>$Name</c> 参照があるか。
    /// true のとき UI に「🔍 auto」バッジ表示。pianist 側は auto-union するため
    /// [Variables] に明示的に書かなくても Copy Values に出るが、UI でユーザに
    /// 「これは Step が使っているので含めるのが自然」と示す情報用。
    /// </summary>
    public bool IsAutoDiscovered { get; init; }

    /// <summary>
    /// values.csv の <c>*</c> 行のセル値プレビュー（先頭 60 文字程度に切り詰めて UI 表示）。
    /// orphan の場合は空文字。
    /// </summary>
    public string SampleValue { get; init; } = "";

    /// <summary>
    /// [Variables] section に宣言されているが values.csv に該当列が存在しない孤児フラグ。
    /// true のとき orphan セクション（赤い背景）に分離表示し、削除ボタンを提示する。
    /// false のとき通常リスト（values.csv 列由来）に表示。
    /// </summary>
    public bool IsOrphan { get; init; }
}
