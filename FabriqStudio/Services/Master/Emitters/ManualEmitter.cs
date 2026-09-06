namespace FabriqStudio.Services.Master.Emitters;

/// <summary>fabriq で自動化できない記入欄を手動作業リストへ写す。</summary>
public sealed class ManualEmitter : IMasterEmitter
{
    public string Name => "手動作業";

    public void Emit(MasterContext ctx)
    {
        AddIfFilled(ctx, "bios_note",      "BIOS 設定");
        AddIfFilled(ctx, "os_info",        "OS 情報の確認（エディション / バージョン）");
        AddIfFilled(ctx, "optical_letter", "光学ドライブのドライブレター変更");
        AddIfFilled(ctx, "other_note",     "その他");
    }

    private static void AddIfFilled(MasterContext ctx, string itemId, string title)
    {
        var text = ctx.Get(itemId).Trim();
        if (string.IsNullOrEmpty(text)) return;
        ctx.Manual($"{title}: {text.Replace("\r", "").Replace("\n", " / ")}");
    }
}
