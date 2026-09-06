using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>B. マスタ仕上げ: BitLocker 解除、エビデンス、履歴削除・仕上げ。Sysprep は対象外（既存の sysprep プロファイルを別途使う）。</summary>
public sealed class FinalizeEmitter : IMasterEmitter
{
    public string Name => "マスタ仕上げ";

    public void Emit(MasterContext ctx)
    {
        if (ctx.IsTrue("bitlocker_disable"))
        {
            AddBitLockerRow(ctx);
            ctx.AddProfile("bitlocker_config", "bitlocker_disable.ps1", ProfileSlot.Finalize, 10, isolated: true);
        }

        if (ctx.IsTrue("evidence"))
            ctx.AddProfile("evidence_config", "evidence_config.ps1", ProfileSlot.Finalize, 20, isolated: false);

        if (ctx.IsTrue("cleanup"))
        {
            // これらはモジュール同梱のカタログ行（Segment 空）をそのまま使うため Segment を付けない
            ctx.AddProfile("directory_cleaner", "directory_cleaner.ps1", ProfileSlot.Finalize, 30, isolated: false);
            ctx.AddProfile("history_destroyer", "history_destroyer.ps1", ProfileSlot.Finalize, 40, isolated: false);
            ctx.AddProfile("system_finalize",   "system_finalize.ps1",   ProfileSlot.Finalize, 50, isolated: false);
        }
    }

    /// <summary>bitlocker_list の C: 行（disable / 配備時の enable の両方が同じ行を読む）。</summary>
    public static void AddBitLockerRow(MasterContext ctx)
    {
        ctx.AddCsvRow("bitlocker_config", "bitlocker_list.csv", Row(
            ("Enabled", "1"),
            ("TargetDrive", "C:"),
            ("EncryptionMethod", "XtsAes128"),
            ("UsedSpaceOnly", "FALSE"),
            ("SkipHardwareTest", "TRUE"),
            ("AutoUnlock", "FALSE"),
            ("Description", "System drive (master)"),
            ("Pin", "")));
    }
}
