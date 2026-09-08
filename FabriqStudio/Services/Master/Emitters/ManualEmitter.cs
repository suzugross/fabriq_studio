using System.Text.RegularExpressions;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// fabriq で自動化できない記入欄を手動作業リストへ写す。
/// 複数行の欄（BIOS 設定 / その他）は 1 行 = 1 件にする（Excel 出力で 1 行ずつ並ぶ形。行頭の箇条書き記号や番号は落とす）。
/// </summary>
public sealed class ManualEmitter : IMasterEmitter
{
    public string Name => "手動作業";

    /// <summary>行頭の箇条書き記号（・ • - * – — ※）や番号（1. / 1) / ①〜⑳）。</summary>
    private static readonly Regex BulletPrefix = new(@"^\s*(?:[・•\-\*–—※]|\d+[.)．）]|[①-⑳])\s*", RegexOptions.Compiled);

    public void Emit(MasterContext ctx)
    {
        AddLines(ctx, "bios_note",      "BIOS 設定");
        AddLines(ctx, "os_info",        "OS 情報の確認（エディション / バージョン）");
        AddLines(ctx, "optical_letter", "光学ドライブのドライブレター変更");
        AddLines(ctx, "other_note",     "その他");
    }

    private static void AddLines(MasterContext ctx, string itemId, string title)
    {
        foreach (var line in SplitLines(ctx.Get(itemId)))
            ctx.Manual($"{title}: {line}");
    }

    /// <summary>複数行テキストを 1 件ずつに分ける（空行は捨て、箇条書き記号・番号は落とす）。</summary>
    public static IEnumerable<string> SplitLines(string text)
    {
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = BulletPrefix.Replace(raw, "").Trim();
            if (line.Length > 0) yield return line;
        }
    }
}
