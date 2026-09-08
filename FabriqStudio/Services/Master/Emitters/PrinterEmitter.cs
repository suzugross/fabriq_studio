using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// 7. システム関連設定「プリンター」: プリンター ドライバー（printer_driver_list.csv）と、案件によっては
/// プリンターそのもの（printer_list.csv、ポート IP 付き）をマスタに含める。
/// printer_mode = driver（ドライバーのみ）/ printer（ドライバー + プリンター作成）。
/// プロファイルは Printer スロット（デスクトップの後、仕上げの前）に printer_driver_install.ps1 → printer_config.ps1。
/// </summary>
public sealed class PrinterEmitter : IMasterEmitter
{
    public const string ModuleDir = "printer_driver_config";

    public string Name => "プリンター";

    public void Emit(MasterContext ctx)
    {
        var rows = ctx.Table("printers")
            .Where(r => r.Cell("DriverName").Trim().Length > 0 || r.Cell("PrinterName").Trim().Length > 0)
            .ToList();
        if (rows.Count == 0) return;
        if (!ctx.ModuleAvailable(ModuleDir)) return;

        var createPrinters = ctx.Get("printer_mode").Trim() == "printer";
        var drivers        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var printerCount   = 0;
        var ignoredNames   = 0;

        foreach (var r in rows)
        {
            var driver = r.Cell("DriverName").Trim();
            var name   = r.Cell("PrinterName").Trim();
            var port   = r.Cell("PortAddress").Trim();

            if (driver.Length == 0)
            {
                ctx.Warn($"プリンター一覧の行「{name}」にドライバ名（INF の DriverName）がありません。この行は出力しません。", "printers");
                continue;
            }

            if (drivers.Add(driver))
                ctx.AddCsvRow(ModuleDir, "printer_driver_list.csv", Row(
                    ("Enabled",     "1"),
                    ("TargetHost",  ""),
                    ("DriverName",  driver),
                    ("Description", "Printer driver (master)")));

            if (!createPrinters)
            {
                if (name.Length > 0 || port.Length > 0) ignoredNames++;
                continue;
            }
            if (name.Length == 0)
            {
                ctx.Info($"ドライバー「{driver}」はプリンター名が空のためドライバーのみ登録します。");
                continue;
            }
            if (port.Length == 0)
                ctx.Warn($"プリンター {name} のポート IP が未入力です（printer_config はポート無しでは作成できません）。", "printers");

            ctx.AddCsvRow(ModuleDir, "printer_list.csv", Row(
                ("Enabled",     "1"),
                ("TargetHost",  ""),
                ("PrinterName", name),
                ("DriverName",  driver),
                ("PortAddress", port),
                ("Description", string.IsNullOrEmpty(r.Cell("Description")) ? "Printer (master)" : r.Cell("Description"))));
            printerCount++;
        }

        if (ignoredNames > 0)
            ctx.Info($"プリンターの登録は「ドライバーのみ」のため、プリンター名／ポート IP（{ignoredNames} 行）は使いません。");

        if (ctx.Snapshot.GetModule(ModuleDir) is { } m)
        {
            var files = m.SubDirFiles.GetValueOrDefault("INF");
            foreach (var d in drivers)
            {
                // INF/ に同名フォルダ（または exe/zip）があるかの緩い確認
                if (files is null || !files.Any(f => f.Contains(d, StringComparison.OrdinalIgnoreCase)
                                                  || d.Contains(System.IO.Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase)))
                    ctx.Warn($"{ModuleDir}/INF/ にドライバ「{d}」に対応するフォルダ／EXE／ZIP が見当たりません。配置を確認してください。", "printers");
            }
        }

        if (drivers.Count > 0)
            ctx.AddProfile(ModuleDir, "printer_driver_install.ps1", ProfileSlot.Printer, 10, isolated: false);
        if (printerCount > 0)
            ctx.AddProfile(ModuleDir, "printer_config.ps1", ProfileSlot.Printer, 20, isolated: false);

        if (createPrinters)
        {
            var defaults = rows.Where(r => r.Cell("IsDefault").Trim() == "1" && r.Cell("PrinterName").Trim().Length > 0)
                               .Select(r => r.Cell("PrinterName").Trim()).ToList();
            if (defaults.Count > 0)
                ctx.Manual($"既定プリンターの設定（{string.Join(" / ", defaults)}）: fabriq の printer_config は既定化に未対応のため手動で設定する。");
        }
    }
}
