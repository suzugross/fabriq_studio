using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// values.csv 全体を表すテーブル（wide format 専用）。
///
/// VariableColumns は CSV ヘッダー順を保持する（保存時の diff 安定のため）。
/// Rows は CSV 上の出現順を保持するが、`*` 行は <see cref="EnsureStarRow"/> で先頭固定にする
/// （§5.2.B）。
///
/// 旧 long format（Key,Value,Encrypted,Note）の検出は <see cref="WasLegacyFormat"/> で
/// 通知する。Phase 1 では読み込みのみ対応し、移行ダイアログは Phase 7 で実装。
///
/// <see cref="Star"/> は `*` 行への直接参照で、grid 上の dim italic 継承表示の
/// バインドターゲット（<c>Table.Star[col]</c>）として使う。
/// </summary>
public partial class PianistValueTable : ObservableObject
{
    /// <summary>
    /// 変数列名（NewPCName を除く）。CSV ヘッダー順を保持し、列追加 / 削除 / リネームの
    /// CollectionChanged 通知で View 側 DataGrid を再構築する。
    /// </summary>
    public ObservableCollection<string> VariableColumns { get; } = new();

    /// <summary>各行（`*` 行 + ホスト別行）。`*` 行は <see cref="EnsureStarRow"/> 適用後は index 0 に固定。</summary>
    public ObservableCollection<PianistValueRow> Rows { get; } = new();

    /// <summary>
    /// 旧 long format（Key,Value,Encrypted,Note）として検出されたか。
    /// true のときは <see cref="VariableColumns"/> / <see cref="Rows"/> は空のまま返り、
    /// Studio は「新形式に変換しますか？」ダイアログを出す責務がある（§5.5）。
    /// </summary>
    public bool WasLegacyFormat { get; set; }

    /// <summary>
    /// `*` 行（共通デフォルト）への参照。<see cref="EnsureStarRow"/> 後は必ず非 null。
    /// dim italic 継承表示の binding パス <c>Table.Star[col]</c> 用。
    /// </summary>
    [ObservableProperty] private PianistValueRow? _star;

    /// <summary>
    /// `*` 行が存在することを保証する。既に存在すれば先頭に移動、無ければ新規作成。
    /// 各行の <see cref="PianistValueRow.Table"/> 逆参照もここで貼り直す。
    /// </summary>
    public PianistValueRow EnsureStarRow()
    {
        // 既存行に Table 逆参照を貼る（service ロード後に呼ばれた場合に備えて）
        foreach (var row in Rows)
            row.Table = this;

        // 既存の `*` 行を探す
        var existing = Rows.FirstOrDefault(r => r.IsStar);
        if (existing is not null)
        {
            if (Rows.IndexOf(existing) != 0)
            {
                Rows.Remove(existing);
                Rows.Insert(0, existing);
            }
            Star = existing;
            SubscribeStarChanges();
            return existing;
        }

        // 新規生成: 全変数列に空セルを敷く
        var star = new PianistValueRow { NewPCName = "*", Table = this };
        foreach (var col in VariableColumns)
            star.Cells[col] = "";

        Rows.Insert(0, star);
        Star = star;
        SubscribeStarChanges();
        return star;
    }

    /// <summary>
    /// `*` 行のセル変更を購読し、依存行（非 *）の同列に対して再評価通知を伝播する。
    /// これにより Studio 上で `*` 行のセルを編集すると、空の依存行セルが新しい
    /// 継承値で即座に再描画される。
    /// </summary>
    private bool _starSubscribed;
    private void SubscribeStarChanges()
    {
        if (Star is null || _starSubscribed) return;
        Star.PropertyChanged += OnStarPropertyChanged;
        _starSubscribed = true;
    }

    private void OnStarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName ?? "";
        if (!name.StartsWith("Item[", StringComparison.Ordinal)) return;
        if (!name.EndsWith("]", StringComparison.Ordinal))       return;
        if (name.Length < 6)                                     return; // 不正形式

        // "Item[]"（インデクサ全体変更）と "Item[Foo]"（特定列変更）の両ケース
        // 後者は col を抽出して列単位で伝播、前者は空 col のまま伝播（受け側で
        // RaiseCellChanged が Item[] も発火するので結果的に全列 refresh される）。
        var col = name.Substring(5, name.Length - 6);
        foreach (var row in Rows)
        {
            if (row.IsStar) continue;
            row.RaiseCellChanged(col);
        }
    }
}
