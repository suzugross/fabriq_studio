using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// C. 配備プロファイル（profiles/&lt;マスタ名&gt;_deploy.csv）: ホスト名 → IP → 再起動 → プリンタ → BitLocker。
/// プリンタ一覧はマスタ側にも含められる（printers_in_master）。
/// </summary>
public sealed class DeployEmitter : IMasterEmitter
{
    public string Name => "配備";

    public void Emit(MasterContext ctx)
    {
        var deploy = ctx.IsTrue("deploy_profile");

        if (deploy)
        {
            ctx.AddProfile("hostname_config",  "hostname_config.ps1",  ProfileSlot.Deploy, 10, isolated: false, deploy: true);
            ctx.AddProfile("ipaddress_config", "ipaddress_config.ps1", ProfileSlot.Deploy, 20, isolated: false, errorMode: "retry", deploy: true);

            if (ctx.IsTrue("deploy_bitlocker"))
            {
                FinalizeEmitter.AddBitLockerRow(ctx);
                ctx.AddProfile("bitlocker_config", "bitlocker_config.ps1", ProfileSlot.Deploy, 130, isolated: true, deploy: true);
                ctx.Info("配備時の BitLocker PIN は端末一覧（hostlist.csv）の Pin 列から読まれます。");
            }
        }

        EmitPrinters(ctx, deploy);
    }

    private static void EmitPrinters(MasterContext ctx, bool deploy)
    {
        var rows = ctx.Table("printers")
            .Where(r => !string.IsNullOrWhiteSpace(r.Cell("PrinterName")))
            .ToList();
        if (rows.Count == 0) return;

        var inMaster = ctx.IsTrue("printers_in_master");
        if (!deploy && !inMaster)
        {
            ctx.Warn("プリンター一覧は配備プロファイルまたは「マスタにも含める」で出力されます。どちらも無効のため出力しません。", "printers");
            return;
        }

        var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var driver = r.Cell("DriverName").Trim();
            var port   = r.Cell("PortAddress").Trim();
            var name   = r.Cell("PrinterName").Trim();
            if (string.IsNullOrEmpty(driver))
                ctx.Warn($"プリンター {name} のドライバ名が未入力です（INF の DriverName と一致させる）。", "printers");
            if (string.IsNullOrEmpty(port))
                ctx.Warn($"プリンター {name} のポート IP が未入力です。", "printers");

            if (!string.IsNullOrEmpty(driver) && drivers.Add(driver))
                ctx.AddCsvRow("printer_driver_config", "printer_driver_list.csv", Row(
                    ("Enabled", "1"),
                    ("TargetHost", ""),
                    ("DriverName", driver),
                    ("Description", "Printer driver (master)")));

            ctx.AddCsvRow("printer_driver_config", "printer_list.csv", Row(
                ("Enabled", "1"),
                ("TargetHost", ""),
                ("PrinterName", name),
                ("DriverName", driver),
                ("PortAddress", port),
                ("Description", string.IsNullOrEmpty(r.Cell("Description")) ? "Printer (master)" : r.Cell("Description"))));
        }

        if (ctx.Snapshot.GetModule("printer_driver_config") is { } m)
        {
            foreach (var d in drivers)
            {
                // INF/ に同名フォルダ（または exe/zip）があるかの緩い確認
                var files = m.SubDirFiles.GetValueOrDefault("INF");
                if (files is null || !files.Any(f => f.Contains(d, StringComparison.OrdinalIgnoreCase) || d.Contains(System.IO.Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase)))
                    ctx.Warn($"printer_driver_config/INF/ にドライバ「{d}」に対応するフォルダ／EXE／ZIP が見当たりません。配置を確認してください。", "printers");
            }
        }

        if (deploy)
        {
            ctx.AddProfile("printer_driver_config", "printer_driver_install.ps1", ProfileSlot.Deploy, 110, isolated: false, deploy: true);
            ctx.AddProfile("printer_driver_config", "printer_config.ps1",         ProfileSlot.Deploy, 120, isolated: false, deploy: true);
        }
        if (inMaster)
        {
            ctx.AddProfile("printer_driver_config", "printer_driver_install.ps1", ProfileSlot.Printer, 10, isolated: false);
            ctx.AddProfile("printer_driver_config", "printer_config.ps1",         ProfileSlot.Printer, 20, isolated: false);
        }

        var defaults = rows.Where(r => r.Cell("IsDefault").Trim() == "1").Select(r => r.Cell("PrinterName")).ToList();
        if (defaults.Count > 0)
            ctx.Manual($"既定プリンターの設定（{string.Join(" / ", defaults)}）: fabriq の printer_config は既定化に未対応のため手動で設定する。");
    }
}
