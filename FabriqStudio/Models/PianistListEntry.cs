using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// pianist_list.csv の 1 行を表すモデル。
/// 列: Enabled (1/0), ProfileName, Group, Description, Segment
///
/// pianist.ps1 Step 1 で <c>Import-ModuleCsv -FilterEnabled</c> で読まれ、Enabled=1 行が
/// 候補となる。Profile CSV 経由なら <c>$env:FABRIQ_SEGMENT</c> で <see cref="Segment"/> が
/// フィルタキーとなる。
///
/// <see cref="ProfileName"/> は <c>modules/extended/pianist/profiles/&lt;Name&gt;/</c> に
/// 実在必須（実在しないと pianist.ps1 Step 2 で Error 終了）。Studio 側ではドロップダウン
/// 追加時に自動でフィルタされ、タイポ事故を防ぐ。
///
/// CSV 上の Enabled は "1"/"0" 文字列、Studio 内は <see cref="bool"/> で扱う。
/// </summary>
public partial class PianistListEntry : ObservableObject
{
    [ObservableProperty] private bool   _enabled;
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private string _group       = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _segment     = "";
}
