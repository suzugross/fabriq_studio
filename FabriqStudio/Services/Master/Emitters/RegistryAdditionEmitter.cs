using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// 「7-3. レジストリ追加」章: レジストリ辞書から選んだ設定（tables["registry_entries"]）を
/// 他の章のレジストリ設定と同じ reg_hklm_list_&lt;名&gt;.csv / reg_hkcu_list_&lt;名&gt;.csv に出す。
/// RegistryTemplateEmitter の直後に走り、同じ値を別の内容で書く場合は追加分を優先して警告する
/// （<see cref="MasterContext.AddRegistry"/> は同じ KeyPath + KeyName を後勝ちで 1 行にまとめる）。
/// </summary>
public sealed class RegistryAdditionEmitter : IMasterEmitter
{
    public const string ItemId = "registry_entries";

    public string Name => "レジストリ（追加）";

    public void Emit(MasterContext ctx)
    {
        var table = ctx.Table(ItemId);
        if (table.Count == 0 || !ctx.IsVisible(ItemId)) return;

        foreach (var row in table)
        {
            var sel = RegistrySelection.FromRow(row);
            if (sel.Id.Length == 0) continue;

            var entry = ctx.DictionaryEntry(sel.Id);
            if (entry is null)
            {
                ctx.Warn($"レジストリ辞書に ID {sel.Id}（{sel.Title}）がありません（辞書から削除された可能性）。この設定は書きません。7-3 章で削除するか選び直してください。", ItemId);
                continue;
            }

            var value = sel.Value;
            var existing = ctx.RegistryRequests.FirstOrDefault(r =>
                r.SubSegment is null &&
                r.Entry.Hive.Equals(entry.Hive, StringComparison.OrdinalIgnoreCase) &&
                r.Entry.KeyPath.Equals(entry.KeyPath, StringComparison.OrdinalIgnoreCase) &&
                r.Entry.KeyName.Equals(entry.KeyName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (existing.Value.Trim().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                    ctx.Info($"レジストリ追加「{entry.Title}」は「{existing.SettingTitle}」と同じ設定のため 1 行にまとめます。");
                else
                    ctx.Warn($"レジストリ追加「{entry.Title}」= {value} は「{existing.SettingTitle}」= {existing.Value} と同じ値（{entry.Hive} {entry.KeyPath}\\{entry.KeyName}）を書きます。追加分の値を優先します。意図した値か確認してください。", ItemId);
            }

            ctx.AddRegistry(entry.Id, value, "レジストリ追加", itemId: ItemId);
        }
    }
}
