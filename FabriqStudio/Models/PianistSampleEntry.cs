using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// instructions/&lt;PhaseID&gt;.txt の <c>[Samples]</c> section に列挙される 1 行（=1 画像エントリ）。
///
/// シリアライズ書式（pianist.ps1 の <c>Parse-PianistInstructionFile</c> 互換）:
/// <code>
///   filename.png  optional caption text
/// </code>
/// 正規表現 <c>^(\S+)(\s+(.+))?$</c>:
/// - グループ 1（必須）: ファイル名トークン（空白を含まない）
/// - グループ 3（任意）: キャプション（残りの行全体、空白で区切る）
///
/// <see cref="File"/> は <c>&lt;profile&gt;/screenshots/</c> 配下の相対ファイル名のみ。
/// 物理ファイルが欠損していても保存は許可（pianist runtime が <c>(missing)</c> placeholder
/// で表示する仕様）— Studio 上では <see cref="Exists"/> で警告アイコン表示する。
/// </summary>
public partial class PianistSampleEntry : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _file = "";

    [ObservableProperty] private string _caption = "";

    /// <summary>
    /// 物理ファイルが <c>&lt;profile&gt;/screenshots/&lt;File&gt;</c> に存在するか。
    /// ロード時 / Add 時 / 保存時に呼び出し側が外部判定して set する（モデル自身は IO しない）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    private bool _exists;

    public bool IsMissing => !Exists;

    /// <summary>UI バインド用のショート表示（File そのまま、欠損時は "(missing) "プレフィクス）。</summary>
    public string DisplayName => Exists ? File : $"(missing) {File}";
}
