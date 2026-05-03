using System.Collections.ObjectModel;

namespace FabriqStudio.Models;

/// <summary>
/// 1 つの Pianist Profile フォルダから読み込んだ全データを束ねるアグリゲート。
/// 編集 UI（メタタブ / Phase 一覧 / 変数 / ショートカット）はこのオブジェクトを共有する。
///
/// Instructions は PhaseID → 本文 の辞書として保持する。procedure.csv 上の PhaseID と
/// 1 対 1 対応するが、孤児ファイル（procedure に存在しない PhaseID の .txt）も
/// pianist.ps1 は無視するため、Studio も読み込んでおくだけで実害なし（§7.1）。
/// </summary>
public class PianistProfileData
{
    public PianistProfileEntry      Entry      { get; set; } = new();
    public PianistProfileMetadata   Metadata   { get; set; } = new();
    public ObservableCollection<PianistStep>     Steps      { get; } = new();
    public PianistValueTable        Values     { get; set; } = new();
    public ObservableCollection<PianistShortcut> Shortcuts  { get; } = new();

    /// <summary>PhaseID → instructions/&lt;PhaseID&gt;.txt の本文。</summary>
    public Dictionary<string, string> Instructions { get; } = new(StringComparer.Ordinal);
}
