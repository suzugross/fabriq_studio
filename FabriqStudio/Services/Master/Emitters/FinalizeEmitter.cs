using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// B. マスタ仕上げ: BitLocker 解除、エビデンス。
/// 履歴・一時ファイルの削除とシェルの仕上げ（directory_cleaner / history_destroyer / system_finalize）は
/// Sysprep と同じく本画面の対象外（既存の sysprep プロファイルで実施する）。
/// </summary>
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
