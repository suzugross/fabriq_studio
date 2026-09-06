using System.Xml.Linq;
using FabriqStudio.Models.Master;
using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>9. アプリケーション: ストアアプリ削除、Windows の機能、インストーラ、winget、Office（ODT / ライセンス）。</summary>
public sealed class AppsEmitter : IMasterEmitter
{
    public string Name => "アプリケーション";

    public void Emit(MasterContext ctx)
    {
        EmitStoreApps(ctx);
        EmitWindowsFeatures(ctx);
        EmitInstallers(ctx);
        EmitWinget(ctx);
        EmitOffice(ctx);
    }

    private static void EmitStoreApps(MasterContext ctx)
    {
        var selected = ctx.Multi("storeapps_remove");
        if (selected.Count == 0) return;

        var item = ctx.Item("storeapps_remove");
        var no = 10;
        foreach (var pkg in selected)
        {
            var name = pkg.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var label = item?.Options?.FirstOrDefault(o => o.Value == name)?.Label ?? name;

            ctx.AddCsvRow("storeapp_config", "storeapp_list.csv", Row(
                ("No", no.ToString()),
                ("AppName", name),
                ("Enabled", "1"),
                ("Description", label)));
            no += 10;
        }
        ctx.AddProfile("storeapp_config", "storeapp_config.ps1", ProfileSlot.Apps, 10, isolated: true);
    }

    private static void EmitWindowsFeatures(MasterContext ctx)
    {
        var selected = ctx.Multi("win_features");
        if (selected.Count == 0) return;

        var item = ctx.Item("win_features");
        foreach (var value in selected)
        {
            var feature = value.Trim();
            if (string.IsNullOrEmpty(feature)) continue;

            var opt    = item?.Options?.FirstOrDefault(o => o.Value == feature);
            var data   = opt?.Data ?? new Dictionary<string, string>();
            var action = data.GetValueOrDefault("Action", "Enable");
            var source = data.GetValueOrDefault("Source", "");
            var limit  = data.GetValueOrDefault("LimitAccess", "");
            var all    = data.GetValueOrDefault("IncludeAllSubFeatures", action == "Enable" ? "1" : "");

            // /payload/... 指定は同梱資材の存在を検査
            if (source.StartsWith("/payload/", StringComparison.OrdinalIgnoreCase)
                && ctx.Snapshot.GetModule("windows_feature_config") is { } m
                && !m.HasFile("payload", source["/payload/".Length..]))
            {
                ctx.Warn($"windows_feature_config/payload/{source["/payload/".Length..]} が無いため、{feature} の有効化はオンライン取得（Windows Update）になります。閉域環境では事前に配置してください。", "win_features");
                source = "";
                limit  = "";
            }

            ctx.AddCsvRow("windows_feature_config", "windows_feature_list.csv", Row(
                ("Enabled", "1"),
                ("Action", action),
                ("FeatureName", feature),
                ("IncludeAllSubFeatures", all),
                ("Source", source),
                ("LimitAccess", limit),
                ("Description", $"{opt?.Label ?? $"{action} {feature}"} (master)")));
        }
        ctx.AddProfile("windows_feature_config", "windows_feature_config.ps1", ProfileSlot.System, 60, isolated: true);
    }

    /// <summary>
    /// インストーラは 1 アプリ = 1 副セグメント（マスタ名:app01:GoogleChrome）にして、
    /// プロファイルにも app_config の行をアプリごとに並べる。FlexProfile の [Run] で 1 アプリずつ実行・再実行できる。
    /// </summary>
    private static void EmitInstallers(MasterContext ctx)
    {
        var rows = ctx.Table("apps");
        var menu = ctx.MenuName("app_config", "app_config.ps1");
        var n = 0;
        foreach (var r in rows)
        {
            var file = r.Cell("FileName").Trim();
            if (string.IsNullOrEmpty(file)) continue;

            var type = r.Cell("Type").Trim().ToLowerInvariant();
            if (type is not ("exe" or "msi" or "bat"))
            {
                var ext = System.IO.Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                type = ext is "msi" or "bat" ? ext : "exe";
            }

            if (ctx.Snapshot.GetModule("app_config") is { } m && !m.HasFile("file", file))
                ctx.Warn($"app_config/file/{file} が見つかりません。生成前に配置してください。", "apps");

            var appName = r.Cell("AppName").Trim();
            if (string.IsNullOrEmpty(appName)) appName = System.IO.Path.GetFileNameWithoutExtension(file);

            if (type == "exe" && string.IsNullOrWhiteSpace(r.Cell("SilentArgs")))
                ctx.Warn($"アプリ「{appName}」のサイレント引数が空です。対話画面が出る可能性があります（9 章の表で入力）。", "apps");

            n++;
            var token = ToSegmentToken(appName);
            var sub   = string.IsNullOrEmpty(token) ? $"app{n:00}" : $"app{n:00}:{token}";

            ctx.AddCsvRow("app_config", "app_list.csv", Row(
                ("Enabled", "1"),
                ("AppName", appName),
                ("FileName", file),
                ("Type", type),
                ("SilentArgs", r.Cell("SilentArgs")),
                ("Description", r.Cell("Description"))), subSegment: sub);

            ctx.AddProfile("app_config", "app_config.ps1", ProfileSlot.Apps, 30 + n, isolated: true,
                subSegment: sub, description: $"{menu}: {appName}");
        }
    }

    /// <summary>winget も 1 アプリ = 1 行（マスタ名:winget01:AppId）。</summary>
    private static void EmitWinget(MasterContext ctx)
    {
        var rows = ctx.Table("winget_apps");
        var menu = ctx.MenuName("winget_install", "winget_install.ps1");
        var n = 0;
        foreach (var r in rows)
        {
            var id = r.Cell("AppID").Trim();
            if (string.IsNullOrEmpty(id)) continue;

            n++;
            var desc  = string.IsNullOrEmpty(r.Cell("Description")) ? id : r.Cell("Description");
            var token = ToSegmentToken(id);
            var sub   = string.IsNullOrEmpty(token) ? $"winget{n:00}" : $"winget{n:00}:{token}";

            ctx.AddCsvRow("winget_install", "app_list.csv", Row(
                ("Enabled", "1"),
                ("AppID", id),
                ("Options", r.Cell("Options")),
                ("Description", desc)), subSegment: sub);

            ctx.AddProfile("winget_install", "winget_install.ps1", ProfileSlot.Apps, 60 + n, isolated: true,
                subSegment: sub, description: $"{menu}: {desc}");
        }
        if (n > 0)
            ctx.Info("winget によるインストールにはインターネット接続が必要です。閉域環境ではインストーラ（app_config）を使ってください。");
    }

    // ── ODT（Office Deployment Tool）────────────────────────────────────

    /// <summary>製品 ID / チャネル / 資材フォルダ名（Microsoft Learn「Product IDs supported by the ODT」「Configuration options for the ODT」準拠）。</summary>
    private sealed record OdtProduct(string Id, string Channel, string Folder, string Label, string? VolumeYear);

    private static readonly Dictionary<string, OdtProduct> OdtProducts = new(StringComparer.Ordinal)
    {
        ["m365e"]       = new("O365ProPlusRetail",            "Current",         "M365",         "Microsoft 365 Apps for enterprise",              null),
        ["m365e_nt"]    = new("O365ProPlusEEANoTeamsRetail",  "Current",         "M365",         "Microsoft 365 Apps for enterprise (Teams なし)", null),
        ["m365b"]       = new("O365BusinessRetail",           "Current",         "M365Business", "Microsoft 365 Apps for business",                null),
        ["m365b_nt"]    = new("O365BusinessEEANoTeamsRetail", "Current",         "M365Business", "Microsoft 365 Apps for business (Teams なし)",   null),
        ["ltsc2024pp"]  = new("ProPlus2024Volume",            "PerpetualVL2024", "LTSC2024",     "Office LTSC Professional Plus 2024",             "2024"),
        ["ltsc2024std"] = new("Standard2024Volume",           "PerpetualVL2024", "LTSC2024Std",  "Office LTSC Standard 2024",                      "2024"),
        ["ltsc2021pp"]  = new("ProPlus2021Volume",            "PerpetualVL2021", "LTSC2021",     "Office LTSC Professional Plus 2021",             "2021"),
        ["ltsc2021std"] = new("Standard2021Volume",           "PerpetualVL2021", "LTSC2021Std",  "Office LTSC Standard 2021",                      "2021"),
    };

    /// <summary>Visio / Project は本体製品の種別（サブスク / ボリューム 2024 / 2021）に合わせた ID を使う。</summary>
    private static string VisioId(string? volumeYear)   => volumeYear switch { "2024" => "VisioPro2024Volume",   "2021" => "VisioPro2021Volume",   _ => "VisioProRetail" };
    private static string ProjectId(string? volumeYear) => volumeYear switch { "2024" => "ProjectPro2024Volume", "2021" => "ProjectPro2021Volume", _ => "ProjectProRetail" };

    /// <summary>Visio / Project 側にも付ける除外（OneDrive は Visio でも同梱されるため）。</summary>
    private static readonly HashSet<string> SharedExcludes = new(StringComparer.Ordinal) { "Groove", "Teams", "Lync" };

    private static void EmitOffice(MasterContext ctx)
    {
        if (ctx.IsTrue("office_odt"))
            EmitOdt(ctx);

        var key = ctx.Get("office_key").Trim();
        if (!string.IsNullOrEmpty(key))
        {
            var activation = ctx.Get("office_activation") == "KMS" ? "KMS" : "MAK";
            ctx.AddCsvRow("office_license_config", "office_key.csv", Row(
                ("Enabled", "1"),
                ("ProductKey", ctx.Secret(key)),
                ("ActivationType", activation),
                ("OsppPath", ""),
                ("Description", $"Office product key ({activation}) (master)")));
            ctx.AddProfile("office_license_config", "office_license_install.ps1", ProfileSlot.Apps, 90, isolated: true);
            ctx.AddProfile("office_license_config", "office_license_auth.ps1",    ProfileSlot.Apps, 91, isolated: true, errorMode: "retry");
        }
    }

    private static void EmitOdt(MasterContext ctx)
    {
        if (!ctx.ModuleAvailable("odt_config")) return;
        var module = ctx.Snapshot.GetModule("odt_config")!;

        if (!module.HasFile("assets", "setup.exe"))
            ctx.Warn("odt_config/assets/setup.exe（Office Deployment Tool）がありません。9 章の Office 枠に setup.exe をドロップしてください。", "odt_setup");

        var mode = ctx.Get("odt_mode") == "Online" ? "Online" : "Offline";
        string assetsFolder;
        string label;

        var customXml = ctx.Get("odt_xml").Trim();
        if (!string.IsNullOrEmpty(customXml))
        {
            // 既製の configuration.xml を使う（生成しない）
            assetsFolder = @"assets\custom";
            label        = "Office (custom configuration.xml)";
            if (!module.HasFile(@"assets\custom", "configuration.xml"))
                ctx.Warn("odt_config/assets/custom/configuration.xml が見つかりません。既製 XML を使う場合は Office 枠にドロップし直してください。", "odt_xml");
        }
        else
        {
            var key = ctx.Get("odt_product");
            if (!OdtProducts.TryGetValue(key, out var product))
            {
                ctx.Error("Office の製品を選択してください（9 章 Microsoft Office）。", "odt_product");
                return;
            }

            assetsFolder = $@"assets\{product.Folder}";
            label        = product.Label;

            var excludes = ctx.Multi("odt_exclude").Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
            var xml      = BuildOdtXml(product, ctx.IsTrue("odt_visio"), ctx.IsTrue("odt_project"), excludes);
            ctx.AddTextFile("odt_config", $@"{assetsFolder}\configuration.xml", xml, $"ODT configuration.xml（{product.Label}）");
        }

        if (mode == "Offline" && !module.HasFile(assetsFolder, "Office"))
            ctx.Warn($"odt_config/{assetsFolder.Replace('\\', '/')}/Office/（オフライン資材）がありません。ネット接続のある PC で assets/setup.exe /download <configuration.xml> を実行して取得するか、方式をオンラインにしてください。", "odt_mode");

        ctx.AddCsvRow("odt_config", "odt_list.csv", Row(
            ("Enabled", "1"),
            ("XmlFileName", "configuration.xml"),
            ("Description", $"{label} (master)"),
            ("AssetsFolder", assetsFolder),
            ("Mode", mode)));
        ctx.AddProfile("odt_config", "odt_install.ps1", ProfileSlot.Apps, 80, isolated: true);
    }

    /// <summary>
    /// ODT の configuration.xml を組み立てる。SourcePath は書かない（fabriq の odt_install が Mode に応じて注入／除去する）。
    /// 固定: 64bit / ja-jp / サイレント / EULA 同意 / 旧 MSI 版 Office の削除 / アプリ強制終了 / 更新有効。
    /// </summary>
    private static string BuildOdtXml(OdtProduct product, bool visio, bool project, IReadOnlyList<string> excludes)
    {
        XElement Product(string id, bool main)
        {
            var e = new XElement("Product", new XAttribute("ID", id),
                new XElement("Language", new XAttribute("ID", "ja-jp")));
            foreach (var ex in excludes)
                if (main || SharedExcludes.Contains(ex))
                    e.Add(new XElement("ExcludeApp", new XAttribute("ID", ex)));
            return e;
        }

        var add = new XElement("Add",
            new XAttribute("OfficeClientEdition", "64"),
            new XAttribute("Channel", product.Channel),
            Product(product.Id, main: true));
        if (visio)   add.Add(Product(VisioId(product.VolumeYear),   main: false));
        if (project) add.Add(Product(ProjectId(product.VolumeYear), main: false));

        var doc = new XElement("Configuration",
            add,
            new XElement("Updates", new XAttribute("Enabled", "TRUE")),
            new XElement("RemoveMSI"),
            new XElement("Property", new XAttribute("Name", "FORCEAPPSHUTDOWN"), new XAttribute("Value", "TRUE")),
            new XElement("Display", new XAttribute("Level", "None"), new XAttribute("AcceptEULA", "TRUE")));

        return doc.ToString() + Environment.NewLine;
    }
}
