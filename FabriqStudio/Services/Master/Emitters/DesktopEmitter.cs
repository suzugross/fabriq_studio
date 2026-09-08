using FabriqStudio.Models.Master;
using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>8. レイアウト: デスクトップショートカット、壁紙、タスクバーのピン留め、スタートメニュー（手動）。</summary>
public sealed class DesktopEmitter : IMasterEmitter
{
    public string Name => "デスクトップ";

    public const string PublicDesktop = @"C:\Users\Public\Desktop";

    /// <summary>Windows 既定の壁紙（青帯）。Windows 10 / 11 の標準イメージに含まれる。</summary>
    public const string WindowsDefaultWallpaper = @"C:\Windows\Web\4K\Wallpaper\Windows\img0_1920x1200.jpg";

    /// <summary>レジストリ辞書 ID: デスクトップ スポットライトの有効フラグ（HKCU DesktopSpotlight\Settings\EnabledState）。壁紙の選択肢が options[].registry で出す。</summary>
    public const string SpotlightFlagId = "000000ce";

    /// <summary>レジストリ辞書 ID: Windows スポットライト機能をすべてオフにするポリシー（HKCU Policies\CloudContent\DisableWindowsSpotlightFeatures）。</summary>
    public const string SpotlightPolicyId = "0000009e";

    public void Emit(MasterContext ctx)
    {
        EmitShortcuts(ctx);
        EmitWallpaper(ctx);
        EmitTaskbarPins(ctx);

        var pins = ctx.Get("start_pins").Trim();
        if (!string.IsNullOrEmpty(pins))
            ctx.Manual($"スタートメニューのピン留め（{pins.Replace("\r", "").Replace("\n", " / ")}）: 参照 PC で startlayout_config の Backup → Build → Import を実施する（名前一覧からの自動生成は未対応）。");
    }

    private static void EmitShortcuts(MasterContext ctx)
    {
        var rows = ctx.Table("desk_shortcuts");
        var any = false;
        foreach (var r in rows)
        {
            var file = r.Cell("FileName").Trim();
            if (string.IsNullOrEmpty(file)) continue;

            if (ctx.Snapshot.GetModule("copyfile_config") is { } m && !m.HasFile("source", file))
                ctx.Warn($"copyfile_config/source/{file} が見つかりません。生成前に配置してください。", "desk_shortcuts");

            ctx.AddCsvRow("copyfile_config", "copy_list.csv", Row(
                ("Enabled", "1"),
                ("FileName", file),
                ("DestPath", PublicDesktop),
                ("Overwrite", "1"),
                ("Description", string.IsNullOrEmpty(r.Cell("Description")) ? "Desktop shortcut (master)" : r.Cell("Description"))));
            any = true;
        }
        if (any)
            ctx.AddProfile("copyfile_config", "copyfile_config.ps1", ProfileSlot.Desktop, 30, isolated: true);
    }

    // ── 壁紙 ──────────────────────────────────────────────────────
    // wallpaper: spotlight（変更しない）/ windows（Windows 既定の青帯に固定）/ custom（画像ファイル）。
    // 固定する 2 つはテンプレートの options[].registry で辞書 000000ce（DesktopSpotlight EnabledState=0）も出し、
    // reg_hkcu_config が HKCU と既定のプロファイルに書く（壁紙だけだとスポットライトに戻ることがあるため）。
    // wallpaper_config 自身も適用時に同じフラグを落とすので二重に安全側。
    private static void EmitWallpaper(MasterContext ctx)
    {
        var choice = ctx.Get("wallpaper").Trim();
        if (choice is not ("custom" or "windows"))
        {
            CheckSpotlightKept(ctx);
            return;
        }

        string file, desc;
        if (choice == "windows")
        {
            file = WindowsDefaultWallpaper;
            desc = "Wallpaper (master) - Windows default";
        }
        else
        {
            file = ctx.Get("wallpaper_file").Trim();
            if (string.IsNullOrEmpty(file))
            {
                ctx.Error("壁紙「その他指定」の場合は画像ファイル名を入力してください（8. レイアウト関連設定）。", "wallpaper_file");
                return;
            }
            var isAbsolute = file.Length > 2 && file[1] == ':';
            if (!isAbsolute && ctx.Snapshot.GetModule("wallpaper_config") is { } m && !m.HasFile("wallpaper", file))
                ctx.Warn($"wallpaper_config/wallpaper/{file} が見つかりません。生成前に配置してください。", "wallpaper_file");
            desc = "Wallpaper (master)";
        }

        var style = ctx.Get("wallpaper_style");
        if (string.IsNullOrEmpty(style)) style = "Fill";

        ctx.AddCsvRow("wallpaper_config", "wallpaper_list.csv", Row(
            ("Enabled", "1"),
            ("Type", "Image"),
            ("FileName", file),
            ("Style", style),
            ("Color", ""),
            ("Description", desc)));
        ctx.AddProfile("wallpaper_config", "wallpaper_config.ps1", ProfileSlot.Desktop, 10, isolated: true);

        CheckSpotlightDisabled(ctx);
    }

    /// <summary>壁紙を固定するのに、スポットライトの有効フラグが出ていない／他の章で 1 に戻されていないか。</summary>
    private static void CheckSpotlightDisabled(MasterContext ctx)
    {
        var flag = ctx.RegistryRequests.FirstOrDefault(r =>
            r.SubSegment is null && r.Entry.Id.Equals(SpotlightFlagId, StringComparison.OrdinalIgnoreCase));

        if (flag is null)
            ctx.Warn($"壁紙を固定しますが、デスクトップ スポットライトの有効フラグを外す行（辞書 ID {SpotlightFlagId}）が出ていません。レジストリ辞書にエントリがあるか確認してください。", "wallpaper");
        else if (flag.Value.Trim() != "0")
            ctx.Warn($"壁紙を固定しますが「{flag.SettingTitle}」がデスクトップ スポットライトの有効フラグを {flag.Value} にしています（0 = オフ）。壁紙がスポットライトに戻るため値を見直してください。", "wallpaper");
    }

    /// <summary>壁紙を変更しない（スポットライトのまま）のに、他の章でスポットライトを止めていないか。</summary>
    private static void CheckSpotlightKept(MasterContext ctx)
    {
        foreach (var r in ctx.RegistryRequests.Where(r => r.SubSegment is null))
        {
            if (r.Entry.Id.Equals(SpotlightFlagId, StringComparison.OrdinalIgnoreCase) && r.Value.Trim() == "0")
                ctx.Warn($"壁紙は「変更しない」ですが「{r.SettingTitle}」でデスクトップ スポットライトの有効フラグを外しています。デスクトップは Windows 既定の壁紙になります（意図どおりなら壁紙を「Windows 既定」にしてください）。", "wallpaper");
            if (r.Entry.Id.Equals(SpotlightPolicyId, StringComparison.OrdinalIgnoreCase) && r.Value.Trim() == "1")
                ctx.Warn($"壁紙は「変更しない」ですが「{r.SettingTitle}」で Windows スポットライト機能をすべてオフにしています。デスクトップ スポットライトも止まります。", "wallpaper");
        }

        var gpo = ctx.Plan.CsvOps.FirstOrDefault(o => o.ModuleDir.Equals(GpoEmitter.ModuleDir, StringComparison.OrdinalIgnoreCase));
        if (gpo is not null && gpo.Rows.Any(row =>
                (row.GetValueOrDefault("ValueName", "") ?? "").Equals("DisableWindowsSpotlightFeatures", StringComparison.OrdinalIgnoreCase) &&
                (row.GetValueOrDefault("Action", "") ?? "").Equals("Set", StringComparison.OrdinalIgnoreCase) &&
                (row.GetValueOrDefault("Value", "") ?? "").Trim() == "1"))
            ctx.Warn("壁紙は「変更しない」ですが、グループポリシーで Windows スポットライト機能をすべてオフにしています。デスクトップ スポットライトも止まります。", "wallpaper");
    }

    private static void EmitTaskbarPins(MasterContext ctx)
    {
        if (!ctx.IsTrue("tb_pins_apply")) return;

        var rows  = ctx.Table("tb_pins");
        var order = 10;
        var any   = false;
        foreach (var r in rows)
        {
            var value = r.Cell("Value").Trim();
            if (string.IsNullOrEmpty(value)) continue;

            var kind   = r.Cell("Kind").Trim();
            var isLink = kind.Equals("LinkPath", StringComparison.OrdinalIgnoreCase)
                         || value.Contains('\\') || value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

            ctx.AddCsvRow("taskbar_config", "taskbar_list.csv", Row(
                ("Enabled", "1"),
                ("Order", order.ToString()),
                ("LinkPath", isLink ? value : ""),
                ("AppId",    isLink ? "" : value),
                ("Description", string.IsNullOrEmpty(r.Cell("Description")) ? value : r.Cell("Description"))));
            order += 10;
            any = true;
        }

        // 0 件でも「既定のピン留めを外す」意味があるため、適用フラグが立っていればプロファイル行は出す。
        // LayoutModification.xml は既定のプロファイル（新規ユーザー用）と sysprep_config/source に置かれ、
        // SetupComplete が初回起動時に再配置するため、Sysprep プロファイル側で実行する（見本と同じ）。
        // Sysprep プロファイルを作らない場合だけマスタ プロファイルの Desktop に載せる。
        if (SysprepEmitter.Enabled(ctx))
            ctx.AddProfile("taskbar_config", "taskbar_config.ps1", ProfileSlot.Sysprep, 30, isolated: true, kind: ProfileKind.Sysprep);
        else
            ctx.AddProfile("taskbar_config", "taskbar_config.ps1", ProfileSlot.Desktop, 20, isolated: true);
        if (!any)
            ctx.Info("タスクバーのピン留めが 0 件のため、新規ユーザーの既定ピン（Edge 等）も外れた状態になります。");
    }
}
