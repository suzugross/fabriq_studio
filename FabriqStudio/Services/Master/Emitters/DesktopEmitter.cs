using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>8. インターフェース: デスクトップショートカット、壁紙、タスクバーのピン留め、スタートメニュー（手動）。</summary>
public sealed class DesktopEmitter : IMasterEmitter
{
    public string Name => "デスクトップ";

    public const string PublicDesktop = @"C:\Users\Public\Desktop";

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

    private static void EmitWallpaper(MasterContext ctx)
    {
        if (ctx.Get("wallpaper") != "custom") return;

        var file = ctx.Get("wallpaper_file").Trim();
        if (string.IsNullOrEmpty(file))
        {
            ctx.Error("壁紙「その他指定」の場合は画像ファイル名を入力してください（8. インターフェース）。", "wallpaper_file");
            return;
        }

        var isAbsolute = file.Length > 2 && file[1] == ':';
        if (!isAbsolute && ctx.Snapshot.GetModule("wallpaper_config") is { } m && !m.HasFile("wallpaper", file))
            ctx.Warn($"wallpaper_config/wallpaper/{file} が見つかりません。生成前に配置してください。", "wallpaper_file");

        var style = ctx.Get("wallpaper_style");
        if (string.IsNullOrEmpty(style)) style = "Fill";

        ctx.AddCsvRow("wallpaper_config", "wallpaper_list.csv", Row(
            ("Enabled", "1"),
            ("Type", "Image"),
            ("FileName", file),
            ("Style", style),
            ("Color", ""),
            ("Description", "Wallpaper (master)")));
        ctx.AddProfile("wallpaper_config", "wallpaper_config.ps1", ProfileSlot.Desktop, 10, isolated: true);
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

        // 0 件でも「既定のピン留めを外す」意味があるため、適用フラグが立っていればプロファイル行は出す
        ctx.AddProfile("taskbar_config", "taskbar_config.ps1", ProfileSlot.Desktop, 20, isolated: true);
        if (!any)
            ctx.Info("タスクバーのピン留めが 0 件のため、新規ユーザーの既定ピン（Edge 等）も外れた状態になります。");
    }
}
