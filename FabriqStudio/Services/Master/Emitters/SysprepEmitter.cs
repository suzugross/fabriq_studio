using System.IO;
using FabriqStudio.Models.Master;
using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// C. Sysprep プロファイル（profiles/&lt;マスタ名&gt;_sysprep.csv）: マスタ作成後に Administrator で実行する仕上げ。
/// 順序は見本（sysprep.csv）と同じ:
///   一時ポリシー削除 → ストアアプリ削除 → タスクバー配置（DesktopEmitter）→ 既定のアプリ → 直前コマンド → 履歴削除 → Sysprep。
/// __GATE__ は置かない。一時ポリシーはマスタ プロファイル先頭で設定（AssembleRegistryFiles）し、ここで削除行を出す。
/// </summary>
public sealed class SysprepEmitter : IMasterEmitter
{
    public string Name => "Sysprep";

    /// <summary>一時ポリシー行の副セグメント（Segment = マスタ名:temp）。</summary>
    public const string TempSubSegment = "temp";

    /// <summary>
    /// マスタ作成中だけ効かせるレジストリ辞書 ID（見本の reg_hklm_list.csv と同じ 4 件）:
    /// CloudContent DisableWindowsConsumerFeatures=1 / Policies\WindowsStore AutoDownload=2 /
    /// WindowsStore\WindowsUpdate AutoDownload=5 / Policies\WindowsStore DisableOSUpgrade=1。
    /// </summary>
    private static readonly string[] TempPolicyIds = ["00000080", "000000a1", "000000aa", "000000ad"];

    /// <summary>OOBE のアカウント画面を飛ばすために unattend で自動作成し、SetupComplete で必ず削除するテストユーザー（固定）。</summary>
    public const string TestUserName = "Test-User";

    private const string SysprepExe = @"C:\Windows\System32\Sysprep\sysprep.exe";
    private const string SetupSourceDir = @"C:\Windows\Setup\Scripts\source";

    public static bool Enabled(MasterContext ctx) => ctx.IsTrue("sysprep_profile");

    public void Emit(MasterContext ctx)
    {
        if (!Enabled(ctx)) return;

        EmitTempPolicies(ctx);
        EmitStoreApps(ctx);
        var appAssoc = EmitDefaultApps(ctx);
        EmitCommands(ctx);
        EmitHistory(ctx);
        EmitSysprep(ctx, appAssoc);
    }

    // ── 1. 一時ポリシー（設定はマスタ側、ここでは削除行）────────────
    private static void EmitTempPolicies(MasterContext ctx)
    {
        if (!ctx.IsTrue("sp_temp_policies")) return;

        foreach (var id in TempPolicyIds)
            ctx.AddRegistry(id, null, "一時ポリシー", TempSubSegment);

        ctx.AddProfile("reg_hklm_config", "reg_hklm_delete.ps1", ProfileSlot.Sysprep, 10, isolated: true,
            subSegment: TempSubSegment, description: "Registry Delete (HKLM) - 一時ポリシーの解除",
            kind: ProfileKind.Sysprep);
    }

    // ── 2. ストアアプリ削除 ──────────────────────────────────────────
    private static void EmitStoreApps(MasterContext ctx)
    {
        var selected = ctx.Multi("sp_storeapps");
        if (selected.Count == 0) return;

        var item = ctx.Item("sp_storeapps");
        var no   = 10;
        foreach (var pkg in selected)
        {
            var name = pkg.Trim();
            if (name.Length == 0) continue;
            var label = item?.Options?.FirstOrDefault(o => o.Value == name)?.Label ?? name;

            ctx.AddCsvRow("storeapp_config", "storeapp_list.csv", Row(
                ("No",          no.ToString()),
                ("AppName",     name),
                ("Enabled",     "1"),
                ("Description", label)));
            no += 10;
        }
        ctx.AddProfile("storeapp_config", "storeapp_config.ps1", ProfileSlot.Sysprep, 20, isolated: true, kind: ProfileKind.Sysprep);
    }

    // ── 4. 既定のアプリ（AppAssoc.xml）────────────────────────────────
    /// <returns>適用する XML ファイル名（出さない場合は null）。</returns>
    private static string? EmitDefaultApps(MasterContext ctx)
    {
        if (!ctx.IsTrue("sp_default_apps")) return null;
        if (!ctx.ModuleAvailable("default_app_config")) return null;

        var name = ctx.Get("sp_appassoc").Trim();
        if (name.Length == 0) name = "AppAssoc.xml";

        var module = ctx.Snapshot.GetModule("default_app_config")!;
        if (!module.HasFile("xml", name))
        {
            ctx.Warn($"default_app_config/xml/{name} がありません。マスタ PC で既定のアプリを設定して「Export App Associations」を実行し、出来た XML をドロップするか、「既定のアプリを編集」で作成してください。既定のアプリの行は出しません。", "sp_appassoc");
            ctx.Manual("マスタ PC で既定のアプリを設定し、Fabriq の「Export App Associations」で xml/AppAssoc.xml を作成して Studio の Sysprep 章にドロップする（または「既定のアプリを編集」で作成する）");
            return null;
        }

        ctx.AddCsvRow("default_app_config", "default_app_list.csv", Row(
            ("Enabled",     "1"),
            ("XmlFile",     name),
            ("Description", "Default App Associations (master)")));
        ctx.AddProfile("default_app_config", "default_app_config.ps1", ProfileSlot.Sysprep, 40, isolated: true, kind: ProfileKind.Sysprep);

        // SetupComplete の再適用用に sysprep_config/source/ にも同じ XML を置く（モジュールは自動では配置しない）
        var abs = ctx.ModuleFile("default_app_config", $"xml/{name}");
        try
        {
            var content = File.ReadAllText(abs!);
            ctx.AddTextFile("sysprep_config", $"source/{name}", content, "既定のアプリの関連付け（SetupComplete 用）");
        }
        catch (Exception ex)
        {
            ctx.Warn($"{name} を読めないため SetupComplete 用のコピーを出せません: {ex.Message}", "sp_appassoc");
        }
        return name;
    }

    // ── 5. Sysprep 直前に実行するプログラム ──────────────────────────
    private static void EmitCommands(MasterContext ctx)
    {
        var rows = ctx.Table("sp_commands")
            .Where(r => r.Cell("ExecutablePath").Trim().Length > 0)
            .ToList();
        if (rows.Count == 0) return;

        foreach (var r in rows)
        {
            var exe  = r.Cell("ExecutablePath").Trim();
            var desc = r.Cell("Description").Trim();
            if (desc.Length == 0) desc = Path.GetFileNameWithoutExtension(exe);
            var timeout = r.Cell("TimeoutSec").Trim();
            var codes   = r.Cell("SuccessCodes").Trim();

            ctx.AddCsvRow("generic_process_runner", "process_list.csv", Row(
                ("Enabled",          "1"),
                ("Description",      desc),
                ("ExecutablePath",   exe),
                ("Arguments",        r.Cell("Arguments").Trim()),
                ("WorkingDirectory", r.Cell("WorkingDirectory").Trim()),
                ("TimeoutSec",       timeout.Length == 0 ? "0" : timeout),
                ("SuccessCodes",     codes.Length == 0 ? "0" : codes),
                ("NoNewWindow",      "0"),
                ("WaitProcessName",  "")));
        }

        // EXE が無ければモジュール側が Skip、終了コード≠0 でも AutoPilot を止めない
        ctx.AddProfile("generic_process_runner", "process_runner.ps1", ProfileSlot.Sysprep, 50, isolated: true,
            errorMode: "skip", kind: ProfileKind.Sysprep);
    }

    // ── 6. 履歴削除（モジュール同梱の一覧をそのまま使う）─────────────
    private static void EmitHistory(MasterContext ctx)
    {
        if (!ctx.IsTrue("sp_history")) return;
        ctx.AddProfile("history_destroyer", "history_destroyer.ps1", ProfileSlot.Sysprep, 60, isolated: false, kind: ProfileKind.Sysprep);
    }

    // ── 7. Sysprep（応答ファイル + SetupComplete + 実行）───────────────
    private static void EmitSysprep(MasterContext ctx, string? appAssoc)
    {
        if (!ctx.ModuleAvailable("sysprep_config")) return;

        var mode     = ctx.Get("sp_mode").Trim() == "audit" ? "audit" : "oobe";
        var shutdown = ctx.Get("sp_shutdown").Trim() switch
        {
            "reboot" => "reboot",
            "quit"   => "quit",
            _        => "shutdown",
        };
        var setupComplete = ctx.IsTrue("sp_setupcomplete");

        ctx.AddCsvRow("sysprep_config", "sysprep_list.csv", Row(
            ("Enabled",             "1"),
            ("SysprepExe",          SysprepExe),
            ("Mode",                mode),
            ("Shutdown",            shutdown),
            ("DeploySetupComplete", setupComplete ? "true" : "false"),
            ("Description",         "Sysprep execution settings (master)")));

        // ── unattend.xml ──
        void U(string name, string value, string desc)
            => ctx.AddCsvRow("sysprep_config", "unattend_list.csv", Row(
                ("Enabled", "1"), ("SettingName", name), ("Value", value), ("Description", desc)));

        var computerName = ctx.Get("sp_computer_name").Trim();
        U("ComputerName", computerName.Length == 0 ? "*" : computerName, "Computer name (* = auto-generate)");
        U("CopyProfile", Bool(ctx.IsTrue("sp_copyprofile")), "Copy Administrator profile to Default");

        // テストユーザーは常に作る（OOBE のアカウント画面を飛ばすため）。SetupComplete で必ず削除する。
        U("TestUserName", TestUserName, "Test user (auto-created in OOBE, deleted by SetupComplete)");

        var enableAdmin = ctx.IsTrue("sp_enable_admin");
        if (enableAdmin)
        {
            var pw = ctx.Get("sp_admin_password");
            if (string.IsNullOrEmpty(pw) && ctx.IsTrue("admin_enable") && !ctx.IsEmpty("admin_password"))
            {
                pw = ctx.Get("admin_password");
                ctx.Info("Sysprep の Administrator パスワードには 6 章の Administrator パスワードを使います。");
            }
            if (string.IsNullOrEmpty(pw))
                ctx.Warn("Administrator を有効化しますがパスワードが空です（パスワードなしになります）。", "sp_admin_password");

            U("EnableAdministrator", "true", "Enable Administrator account");
            U("AdminPassword", ctx.Secret(pw), "Administrator password");
        }

        U("HideEULAPage",              Bool(ctx.IsTrue("sp_hide_eula")),     "Skip EULA page");
        U("ProtectYourPC",             ctx.Get("sp_protect_pc").Trim() == "1" ? "1" : "3", "Privacy settings (3 = do not send)");
        U("HideWirelessSetupInOOBE",   Bool(ctx.IsTrue("sp_hide_wireless")), "Skip Wi-Fi setup page");
        U("HideOnlineAccountScreens",  Bool(ctx.IsTrue("sp_hide_online")),   "Skip online account page");
        U("HideOEMRegistrationScreen", Bool(ctx.IsTrue("sp_hide_oem")),      "Skip OEM registration page");

        var persist = Bool(ctx.IsTrue("sp_persist_drivers"));
        U("DoNotCleanUpNonPresentDevices", persist, "Preserve non-present device drivers");
        U("PersistAllDeviceInstalls",      persist, "Persist device installs across generalize");

        // ── SetupComplete.cmd ──
        if (setupComplete)
        {
            void S(int order, string type, string target, string dest, string desc)
                => ctx.AddCsvRow("sysprep_config", "setupcomplete_list.csv", Row(
                    ("Enabled", "1"), ("Order", order.ToString()), ("ActionType", type),
                    ("Target", target), ("Destination", dest), ("Description", desc)));

            if (enableAdmin && ctx.IsTrue("sp_sc_activate_admin"))
                S(5, "Command", "net user Administrator /active:yes", "", "Activate Administrator account");

            S(10, "DeleteUser", TestUserName, "", "Delete test user");

            if (ctx.IsTrue("tb_pins_apply"))
                S(20, "CopyFile", "LayoutModification.xml", @"C:\Users\Default\AppData\Local\Microsoft\Windows\Shell\", "Deploy taskbar layout");

            if (appAssoc is not null)
                S(30, "Command", $@"Dism /online /Import-DefaultAppAssociations:{SetupSourceDir}\{appAssoc}", "", "Set default app associations");

            var order = 40;
            foreach (var r in ctx.Table("sp_sc_packages"))
            {
                var folder = r.Cell("FolderName").Trim().Trim('\\');
                if (folder.Length == 0) continue;

                var sysprepModule = ctx.Snapshot.GetModule("sysprep_config");
                if (sysprepModule is not null && !sysprepModule.HasFile("source", folder))
                    ctx.Warn($"sysprep_config/source/{folder} がありません。フォルダーをドロップして配置してください。", "sp_sc_packages");

                var dest = r.Cell("Destination").Trim();
                if (dest.Length == 0) dest = $@"%HOMEDRIVE%\Users\Default\AppData\Local\Packages\{folder}";
                var desc = r.Cell("Description").Trim();
                if (desc.Length == 0) desc = $"{folder} settings";

                S(order++, "CopyFile", folder, dest, desc);
            }

            if (ctx.IsTrue("sp_sc_clean_default"))
            {
                S(60, "Command", "ATTRIB -H \"C:\\Users\\Default\"",                                          "", "Unhide Default profile");
                S(61, "Command", "ATTRIB -H \"C:\\Users\\Default\\AppData\"",                                 "", "Unhide AppData");
                S(62, "Command", "rd /Q /S \"C:\\Users\\Default\\AppData\\Local\\Microsoft\\Windows\\INetCache\"", "", "Remove INetCache");
                S(63, "Command", "rd /Q /S \"C:\\Users\\Default\\AppData\\Local\\Microsoft\\Windows\\WebCache\"",  "", "Remove WebCache");
                S(64, "Command", "rd /Q /S \"C:\\Users\\Default\\AppData\\LocalLow\"",                        "", "Remove LocalLow");
                S(65, "Command", "ATTRIB +H \"C:\\Users\\Default\\AppData\"",                                 "", "Rehide AppData");
                S(66, "Command", "ATTRIB +H \"C:\\Users\\Default\"",                                          "", "Rehide Default profile");
            }

            if (ctx.IsTrue("sp_sc_reset_stopper"))
            {
                S(67, "Command", "reg load \"HKU\\DefTmp\" \"C:\\Users\\Default\\NTUSER.DAT\"", "", "DefaultApp_reset_stopper");
                S(68, "Command", "reg delete \"HKU\\DefTmp\\Software\\Microsoft\\Windows\\Shell\\Associations\" /v FileAssociationsUpdateVersion /f", "", "DefaultApp_reset_stopper");
                S(69, "Command", "reg delete \"HKU\\DefTmp\\Software\\Microsoft\\Windows\\Shell\\Associations\\FileAssociationsUpdateVersion\" /f", "", "DefaultApp_reset_stopper");
                S(70, "Command", "reg delete \"HKU\\DefTmp\\Software\\Microsoft\\Windows\\Shell\\Associations\\UrlAssociations\" /f", "", "DefaultApp_reset_stopper");
                S(71, "Command", "reg delete \"HKU\\DefTmp\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\" /f", "", "DefaultApp_reset_stopper");
                S(72, "Command", "reg unload \"HKU\\DefTmp\"", "", "DefaultApp_reset_stopper");
            }

            var extra = 90;
            foreach (var r in ctx.Table("sp_sc_commands"))
            {
                var cmd = r.Cell("Command").Trim();
                if (cmd.Length == 0) continue;
                var desc = r.Cell("Description").Trim();
                S(extra++, "Command", cmd, "", desc.Length == 0 ? "Custom command" : desc);
            }
        }
        else
        {
            ctx.Warn($"SetupComplete.cmd を配置しないため、OOBE で自動作成されるテストユーザー {TestUserName} が納品端末に残ります。", "sp_setupcomplete");
        }

        ctx.AddProfile("sysprep_config", "sysprep_config.ps1", ProfileSlot.Sysprep, 70, isolated: true, kind: ProfileKind.Sysprep);

        var after = shutdown switch { "reboot" => "再起動", "quit" => "終了（シャットダウンなし）", _ => "シャットダウン" };
        ctx.Info($"Sysprep プロファイルはマスタ作成完了後に Administrator でサインインした状態で実行します。Sysprep（/generalize /{mode}）の後に PC は{after}します。");
    }

    private static string Bool(bool v) => v ? "true" : "false";
}
