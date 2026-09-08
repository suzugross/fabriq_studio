using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// 7. システム関連のうち、テンプレートの辞書参照だけでは表現できないもの:
/// RDP のファイアウォール規則、ファイアウォールプロファイル、Defender の注意、Windows Update の時刻・アクティブ時間、
/// 電源、スクリーンセーバーの秒換算、音量、解像度、DPI、プロキシ、SMB1。
/// </summary>
public sealed class SystemEmitter : IMasterEmitter
{
    public string Name => "システム";

    // 辞書 ID（registry_collection/catalog.json）
    private const string DictScreenSaveActive   = "000000d7";
    private const string DictScreenSaveTimeOut  = "000000d8";
    private const string DictScreenSaverSecure  = "000000d9";
    private const string DictProxyOverride      = "000000e9";
    private const string DictProxyEnable        = "000000ea";
    private const string DictProxyServer        = "000000eb";
    private const string DictWuScheduledTime    = "000000f9";
    private const string DictWuSetActiveHours   = "000000fb";
    private const string DictWuActiveHoursStart = "000000fc";
    private const string DictWuActiveHoursEnd   = "000000fd";
    private const string DictProxyAutoConfigUrl = "000000fe";

    public void Emit(MasterContext ctx)
    {
        EmitRemoteDesktopFirewall(ctx);
        EmitFirewallProfiles(ctx);
        EmitDefenderNote(ctx);
        EmitWindowsUpdate(ctx);
        EmitPower(ctx);
        EmitScreenSaver(ctx);
        EmitVolume(ctx);
        EmitResolutionAndDpi(ctx);
        EmitProxy(ctx);
        EmitSmb1(ctx);
    }

    private static void EmitRemoteDesktopFirewall(MasterContext ctx)
    {
        if (ctx.Get("rdp") == "0") return;

        ctx.AddCsvRow("firewall_rule_make_config", "firewall_rule_make_list.csv", Row(
            ("Enabled", "1"),
            ("DisplayName", "Allow Remote Desktop (TCP 3389)"),
            ("Name", ""),
            ("Description", "Remote Desktop inbound (master)"),
            ("Group", "Fabriq Master"),
            ("Direction", "Inbound"),
            ("Action", "Allow"),
            ("RuleEnabled", "True"),
            ("Profile", "Domain;Private"),
            ("Protocol", "TCP"),
            ("LocalPort", "3389")));
        ctx.AddProfile("firewall_rule_make_config", "firewall_rule_make_config.ps1", ProfileSlot.System, 15, isolated: true);
    }

    private static void EmitFirewallProfiles(MasterContext ctx)
    {
        var values = new (string Profile, string ItemId)[]
        {
            ("Domain", "fw_domain"), ("Private", "fw_private"), ("Public", "fw_public"),
        };

        // 全部「有効(既定)」なら何もしない
        if (values.All(v => ctx.Get(v.ItemId) is "" or "on")) return;

        foreach (var (profile, itemId) in values)
        {
            var v = ctx.Get(itemId);
            var status = v == "off" ? "off" : "on";
            if (v == "partial")
                ctx.Manual($"ファイアウォール {profile} プロファイルの「一部許可」規則を firewall_rule_make_config で定義する（詳細はヒアリング内容）。");

            ctx.AddCsvRow("firewall_config", "firewall_list.csv", Row(
                ("Enabled", "1"),
                ("Profile", profile),
                ("Status", status),
                ("Description", $"{profile} profile {status} (master)")));
        }
        ctx.AddProfile("firewall_config", "firewall_config.ps1", ProfileSlot.System, 10, isolated: true);
    }

    private static void EmitDefenderNote(MasterContext ctx)
    {
        if (ctx.Get("defender") == "0")
            ctx.Manual("Windows Defender を無効化する場合、Windows 11 の「改ざん防止」が有効だとポリシーが無視されるため、事前に設定アプリで改ざん防止をオフにする。");
    }

    private static void EmitWindowsUpdate(MasterContext ctx)
    {
        if (ctx.Get("wu_mode") == "4")
        {
            var hour = ctx.GetInt("wu_install_time");
            if (hour is null or < 0 or > 23)
                ctx.Error("Windows Update のインストール時刻は 0〜23 の整数で指定してください。", "wu_install_time");
            else
                ctx.AddRegistry(DictWuScheduledTime, hour.Value.ToString(), ctx.Label("wu_mode"), itemId: "wu_install_time");
        }

        if (ctx.IsTrue("wu_active_enabled"))
        {
            var start = ctx.GetInt("wu_active_start");
            var end   = ctx.GetInt("wu_active_end");
            if (start is null or < 0 or > 23 || end is null or < 0 or > 23)
            {
                ctx.Error("アクティブ時間は 0〜23 の整数で指定してください。", "wu_active_start");
                return;
            }
            var span = (end.Value - start.Value + 24) % 24;
            if (span == 0 || span > 18)
            {
                ctx.Error("アクティブ時間は 18 時間以内にしてください（Windows の制限）。", "wu_active_end");
                return;
            }
            ctx.AddRegistry(DictWuSetActiveHours,   "1",                    "アクティブ時間", itemId: "wu_active_enabled");
            ctx.AddRegistry(DictWuActiveHoursStart, start.Value.ToString(), "アクティブ時間", itemId: "wu_active_start");
            ctx.AddRegistry(DictWuActiveHoursEnd,   end.Value.ToString(),   "アクティブ時間", itemId: "wu_active_end");
        }
    }

    private static void EmitPower(MasterContext ctx)
    {
        if (!ctx.IsTrue("power_apply")) return;

        string Minutes(string id)
        {
            var v = ctx.Get(id).Trim();
            if (string.IsNullOrEmpty(v)) return "";
            if (!int.TryParse(v, out var n) || n < 0)
            {
                ctx.Error($"「{ctx.Label(id)}」は 0 以上の整数（分、0=適用しない）で指定してください。", id);
                return "";
            }
            return n.ToString();
        }

        var plan = ctx.Get("power_plan");
        if (string.IsNullOrEmpty(plan)) plan = "BALANCED";
        var planLabel = plan switch
        {
            "HIGH_PERFORMANCE" => "高パフォーマンス",
            "POWER_SAVER"      => "省電力",
            _                  => "バランス",
        };

        ctx.AddCsvRow("power_config", "power_list.csv", Row(
            ("Enabled", "1"),
            ("ProfileName", $"{planLabel} ({ctx.MasterName})"),
            ("Description", "Master power settings"),
            ("PowerPlan", plan),
            ("PowerMode", ""),
            ("Display_TurnOff_AC",        Minutes("pw_display_ac")),
            ("Display_TurnOff_Battery",   Minutes("pw_display_dc")),
            ("Sleep_After_AC",            Minutes("pw_sleep_ac")),
            ("Sleep_After_Battery",       Minutes("pw_sleep_dc")),
            ("Hibernate_After_AC",        ""),
            ("Hibernate_After_Battery",   ""),
            ("PowerButton_AC",            ctx.Get("pw_button")),
            ("PowerButton_Battery",       ctx.Get("pw_button")),
            ("SleepButton_AC",            ctx.Get("pw_sleep_button")),
            ("SleepButton_Battery",       ctx.Get("pw_sleep_button")),
            ("LidClose_AC",               ctx.Get("pw_lid")),
            ("LidClose_Battery",          ctx.Get("pw_lid")),
            ("HardDisk_TurnOff_AC",       Minutes("pw_hdd_ac")),
            ("HardDisk_TurnOff_Battery",  Minutes("pw_hdd_dc")),
            ("Processor_MinState_AC",     ""),
            ("Processor_MinState_Battery",""),
            ("Processor_MaxState_AC",     ""),
            ("Processor_MaxState_Battery","")));

        ctx.AddProfile("power_config", "power_config.ps1", ProfileSlot.System, 20, isolated: true);
    }

    private static void EmitScreenSaver(MasterContext ctx)
    {
        if (ctx.Get("ss_enabled") != "1") return;

        var minutes = ctx.GetInt("ss_wait_min");
        if (minutes is null or <= 0)
        {
            ctx.Error("スクリーンセーバーの待ち時間は 1 以上の整数（分）で指定してください。", "ss_wait_min");
            return;
        }

        ctx.AddRegistry(DictScreenSaveActive,  "1",                              "スクリーンセーバー", itemId: "ss_enabled");
        ctx.AddRegistry(DictScreenSaveTimeOut, (minutes.Value * 60).ToString(),  "スクリーンセーバー", itemId: "ss_wait_min");
        ctx.AddRegistry(DictScreenSaverSecure, ctx.IsTrue("ss_secure") ? "1" : "0", "スクリーンセーバー", itemId: "ss_secure");
    }

    private static void EmitVolume(MasterContext ctx)
    {
        if (!ctx.IsTrue("volume_apply")) return;

        var vol = ctx.GetInt("volume");
        if (vol is null or < 0 or > 100)
        {
            ctx.Error("音量は 0〜100 の整数で指定してください。", "volume");
            return;
        }

        ctx.AddCsvRow("volume_config", "volume_list.csv", Row(
            ("Enabled", "1"),
            ("Volume", vol.Value.ToString()),
            ("Mute", "off"),
            ("Description", $"Master volume {vol}%")));
        ctx.AddProfile("volume_config", "volume_config.ps1", ProfileSlot.System, 30, isolated: true);
    }

    private static void EmitResolutionAndDpi(MasterContext ctx)
    {
        var res = ctx.Get("resolution").Trim().ToLowerInvariant().Replace("×", "x");
        if (!string.IsNullOrEmpty(res))
        {
            var parts = res.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h))
            {
                ctx.Error("画面解像度は 1920x1080 の形式で指定してください。", "resolution");
            }
            else
            {
                ctx.AddCsvRow("resolution_api_config", "resolution_list.csv", Row(
                    ("Enabled", "1"),
                    ("Width", w.ToString()),
                    ("Height", h.ToString()),
                    ("Description", $"{w}x{h} (master)")));
                ctx.AddProfile("resolution_api_config", "resolution_api_config.ps1", ProfileSlot.System, 40, isolated: true);
            }
        }

        var dpi = ctx.Get("dpi").Trim().TrimEnd('%');
        if (!string.IsNullOrEmpty(dpi))
        {
            if (!int.TryParse(dpi, out var pct) || pct < 100 || pct > 500)
            {
                ctx.Error("DPI（拡大率）は 100〜500 の整数（%）で指定してください。", "dpi");
                return;
            }

            ctx.AddCsvRow("dpi_api_config", "dpi_list.csv", Row(
                ("Enabled", "1"),
                ("MonitorIndex", "0"),
                ("ScalePercent", pct.ToString()),
                ("Description", $"Primary monitor {pct}% (master)")));
            ctx.AddProfile("dpi_api_config", "dpi_api_config.ps1", ProfileSlot.System, 50, isolated: true);

            // 新規ユーザー向け（レジストリ方式）は拡張モジュール。無ければ警告だけ出して続行。
            if (ctx.Snapshot.HasModule("dpi_config"))
            {
                ctx.AddCsvRow("dpi_config", "dpi_list.csv", Row(
                    ("Enabled", "1"),
                    ("HardwareID", "AUTO"),
                    ("ScalePercent", pct.ToString()),
                    ("Description", $"Primary display {pct}% (master)")));
                ctx.AddProfile("dpi_config", "dpi_config.ps1", ProfileSlot.System, 51, isolated: true);
            }
            else
            {
                ctx.Warn("extended/dpi_config が無いため、DPI は現在ユーザーにのみ適用されます（新規ユーザーには反映されません）。");
            }
        }
    }

    private static void EmitProxy(MasterContext ctx)
    {
        var pac = ctx.Get("proxy_pac").Trim();
        if (!string.IsNullOrEmpty(pac))
            ctx.AddRegistry(DictProxyAutoConfigUrl, pac, "プロキシ", itemId: "proxy_pac");

        if (!ctx.IsTrue("proxy_enabled")) return;

        var server = ctx.Get("proxy_server").Trim();
        var port   = ctx.Get("proxy_port").Trim();
        if (string.IsNullOrEmpty(server))
        {
            ctx.Error("プロキシを使用する場合はアドレスを入力してください（7. プロキシサーバー設定）。", "proxy_server");
            return;
        }
        var serverValue = string.IsNullOrEmpty(port) ? server : $"{server}:{port}";

        var exceptions = ctx.Get("proxy_exceptions")
            .Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (ctx.IsTrue("proxy_bypass_local") && !exceptions.Contains("<local>")) exceptions.Add("<local>");

        ctx.AddRegistry(DictProxyEnable, "1", "プロキシ", itemId: "proxy_enabled");
        ctx.AddRegistry(DictProxyServer, serverValue, "プロキシ", itemId: "proxy_server");
        if (exceptions.Count > 0)
            ctx.AddRegistry(DictProxyOverride, string.Join(";", exceptions), "プロキシ", itemId: "proxy_exceptions");
    }

    private static void EmitSmb1(MasterContext ctx)
    {
        var v = ctx.Get("smb1");
        if (string.IsNullOrEmpty(v)) return;

        var action = v == "1" ? "Enable" : "Disable";
        ctx.AddCsvRow("windows_feature_config", "windows_feature_list.csv", Row(
            ("Enabled", "1"),
            ("Action", action),
            ("FeatureName", "SMB1Protocol"),
            ("IncludeAllSubFeatures", v == "1" ? "1" : ""),
            ("Source", ""),
            ("LimitAccess", ""),
            ("Description", $"{action} SMB1 (master)")));
        ctx.AddProfile("windows_feature_config", "windows_feature_config.ps1", ProfileSlot.System, 60, isolated: true);
    }
}
