using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// bundle に対する更新アクション種別。fabriq 公開契約 § 9.4 / § 9.6 準拠。
/// </summary>
public enum UpdateAction
{
    /// <summary>template &gt; target、UPDATE 対象（デフォルト選択）。</summary>
    Update,
    /// <summary>template あり / target 無し、lazy seed で新規追加（デフォルト選択）。</summary>
    New,
    /// <summary>両 version 同一。SKIP。</summary>
    SkipSame,
    /// <summary>template &lt; target。SKIP（警告表示）。</summary>
    SkipTargetNewer,
    /// <summary>template に VERSION 無し。SKIP。</summary>
    SkipNoTemplate,
    /// <summary>target にあり template に無いモジュール。site-custom として保持。</summary>
    Preserve,
}

/// <summary>
/// Preview DataGrid の 1 行。通常の MVVM 流儀で IsSelected だけを可変にし、その他は init-only。
/// </summary>
public partial class BundleUpdateItem : ObservableObject
{
    /// <summary>一意キー。`kernel` もしくは `modules/{std,ext}/<name>`。</summary>
    public string  BundleKey   { get; init; } = "";

    /// <summary>DataGrid での表示名。</summary>
    public string  DisplayName { get; init; } = "";

    /// <summary>グルーピング用（"kernel" / "modules/standard" / "modules/extended"）。</summary>
    public string  GroupName   { get; init; } = "";

    public SemVer?    TargetVersion   { get; init; }
    public SemVer?    TemplateVersion { get; init; }
    public SemVer?    RequiresKernel  { get; init; }
    public UpdateAction Action        { get; init; }

    /// <summary>表示用にアクション文字列を返す。</summary>
    public string ActionText => Action switch
    {
        UpdateAction.Update           => "UPDATE",
        UpdateAction.New              => "NEW",
        UpdateAction.SkipSame         => "SKIP (same)",
        UpdateAction.SkipTargetNewer  => "SKIP (target newer)",
        UpdateAction.SkipNoTemplate   => "SKIP (no template)",
        UpdateAction.Preserve         => "PRESERVE (site-custom)",
        _                             => "—",
    };

    public string TargetVersionText   => TargetVersion?.ToString()   ?? "(none)";
    public string TemplateVersionText => TemplateVersion?.ToString() ?? "(none)";

    /// <summary>Preview 時に表示する警告（REQUIRES_KERNEL 不整合など）。</summary>
    public string? WarningMessage { get; init; }

    /// <summary>ユーザーが手動でチェックを操作できるか。Preserve / SkipNoTemplate は不可。</summary>
    public bool CanSelect => Action is UpdateAction.Update or UpdateAction.New
                                or UpdateAction.SkipSame or UpdateAction.SkipTargetNewer;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isBlocked;
}

/// <summary>
/// Plan 計算結果。<see cref="Bundles"/> は kernel を先頭、続いて modules/standard, modules/extended を名前順。
/// </summary>
public record FabriqUpdatePlan(
    string                      TemplateRoot,
    string                      TargetRoot,
    OverlayRules                Rules,
    SemVer?                     TargetKernel,
    SemVer?                     TemplateKernel,
    IReadOnlyList<BundleUpdateItem> Bundles);
