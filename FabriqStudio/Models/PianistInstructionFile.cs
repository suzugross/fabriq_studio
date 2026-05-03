using System.Collections.ObjectModel;

namespace FabriqStudio.Models;

/// <summary>
/// 1 つの instructions/&lt;PhaseID&gt;.txt をパースした結果（4 section + メタ情報）。
///
/// pianist v1.4.0 で導入された section marker DSL に対応:
/// <code>
///   [RPA]
///   ...
///   [Manual]
///   ...
///   [Variables]
///   var1, var2
///   var3
///   [Samples]
///   before.png  キャプション
///   after.png
/// </code>
///
/// パース / シリアライズロジックは <see cref="FabriqStudio.Helpers.PianistInstructionParser"/>
/// に集約（pianist.ps1 の <c>Parse-PianistInstructionFile</c> とバイト単位で同じセマンティクス）。
///
/// <see cref="WasLegacyFormat"/> = ファイル全体に <c>[Section]</c> マーカーが一つも無く、
/// 全文が Manual に折り畳まれた状態を示す。Studio はこのフラグを見て初回保存時に
/// 「新形式に自動変換します」確認ダイアログを出す。
/// </summary>
public class PianistInstructionFile
{
    /// <summary>[RPA] section の本文（trim 済み、空 OK）。</summary>
    public string RPA { get; set; } = "";

    /// <summary>[Manual] section の本文（trim 済み、空 OK）。Pre-section 行や legacy ファイル全文もここに折り込まれる。</summary>
    public string Manual { get; set; } = "";

    /// <summary>
    /// [Variables] section の明示宣言（auto-discover との union 前の状態）。
    /// 列名バリデーション <c>^[A-Za-z_][A-Za-z0-9_]*$</c> 通過分のみ保持。
    /// </summary>
    public ObservableCollection<string> Variables { get; } = new();

    /// <summary>[Samples] section のエントリ列（並び順は CSV / ファイルの記述順を保持）。</summary>
    public ObservableCollection<PianistSampleEntry> Samples { get; } = new();

    /// <summary>
    /// パース時にファイルへ <c>[Section]</c> マーカーが一つも無かった場合に true。
    /// 「全文 Manual 扱い」のレガシー互換動作の根拠。
    /// </summary>
    public bool WasLegacyFormat { get; set; }
}
