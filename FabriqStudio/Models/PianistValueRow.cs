using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// values.csv（wide format）の 1 行。
///
/// 列構成: NewPCName, &lt;Var1&gt;, &lt;Var2&gt;, ..., &lt;VarN&gt;
/// NewPCName='*' の行が全ホスト共通デフォルト（先頭固定 1 行）。個別ホスト行のセルが空のときは
/// pianist.ps1 が `*` 行へフォールバックする（§5.1）。Studio の grid 表示も同じ意味論で
/// `*` 行値を dim italic で継承表示する（§5.2.C）。
///
/// セル値は **生の文字列**（"ENC:&lt;Base64&gt;" prefix 付きの暗号文 or 平文）を保持する。
/// 暗号化／復号は HostDetail と同じ右クリック ContextMenu UX で行い、保存時の自動変換は
/// しない（保存はメモリ表現をそのまま書く）。詳細は <c>feedback_pianist_crypto_ux.md</c>。
/// </summary>
public partial class PianistValueRow : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStar))]
    private string _newPCName = "";

    /// <summary>NewPCName が "*" のときに true（共通デフォルト行 = 先頭固定 / 削除不可）。</summary>
    public bool IsStar => string.Equals(NewPCName, "*", StringComparison.Ordinal);

    /// <summary>
    /// 変数列 → セル生値。列名は <see cref="PianistValueTable.VariableColumns"/> と一致する。
    /// 直接書き換えると PropertyChanged が発火しないため、編集には <see cref="this[string]"/> を使う。
    /// </summary>
    public Dictionary<string, string> Cells { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 親テーブルへの逆参照。`*` 行の同列値を継承表示するために使う。
    /// service 層がロード時 / 行追加時にセットする責務を持つ（Studio 内のみで利用）。
    /// </summary>
    public PianistValueTable? Table { get; set; }

    /// <summary>
    /// セル生値の read/write。WPF binding は <c>{Binding [Foo]}</c> でこのインデクサに到達する。
    /// 値変更時は <c>"Item[col]"</c> + <c>"Item[]"</c> の両通知を発火し、その列にバインドしている
    /// TextBlock / TextBox / MultiBinding sub-binding を再評価させる。
    /// </summary>
    /// <remarks>
    /// <c>"Item[]"</c>（=<see cref="System.ComponentModel.IndexerName"/> の Default）は WPF の
    /// 「任意のインデクサ値が変わった」標準通知。MultiBinding の sub-binding が
    /// <c>"Item[col]"</c> 個別通知を取りこぼすケースに備えて両方発火する（HostDetail の単一 binding
    /// と異なり pianist の grid 表示は MultiBinding 経由のため信頼性を高める）。
    /// </remarks>
    public string this[string column]
    {
        get => Cells.TryGetValue(column, out var v) ? v : "";
        set
        {
            var current = Cells.TryGetValue(column, out var existing) ? existing : "";
            if (string.Equals(current, value, StringComparison.Ordinal)) return;

            Cells[column] = value ?? "";
            OnPropertyChanged($"Item[{column}]");
            OnPropertyChanged("Item[]");
        }
    }

    /// <summary>
    /// 外部（テーブル側など）から「この行のこの列の表示が再評価されるべき」と通知させるための
    /// public ヘルパ。`*` 行のセル変更時に dependent 行へ propagate する用途。
    /// </summary>
    public void RaiseCellChanged(string column)
    {
        OnPropertyChanged($"Item[{column}]");
        OnPropertyChanged("Item[]");
    }
}
