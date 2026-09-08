using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// テンプレート JSON に書かれたレジストリ辞書参照（choice の options[].registry、
/// bool の registryTrue/registryFalse、multi の options[].registry）を汎用的に処理する。
/// 表示条件（visibleWhen）で隠れている質問の値は無視する。
/// </summary>
public sealed class RegistryTemplateEmitter : IMasterEmitter
{
    public string Name => "レジストリ（テンプレート）";

    public void Emit(MasterContext ctx)
    {
        foreach (var item in ctx.Template.Sections.SelectMany(s => s.Items))
        {
            if (!ctx.IsVisible(item.Id)) continue;

            switch (item.Type)
            {
                case MasterItemTypes.Choice:
                {
                    var choice = ctx.SelectedChoice(item.Id);
                    if (choice?.Registry is null) break;
                    foreach (var r in choice.Registry)
                        ctx.AddRegistry(r.Id, r.Value, item.Label, itemId: item.Id);
                    break;
                }
                case MasterItemTypes.Bool:
                {
                    var list = ctx.IsTrue(item.Id) ? item.RegistryTrue : item.RegistryFalse;
                    if (list is null) break;
                    foreach (var r in list)
                        ctx.AddRegistry(r.Id, r.Value, item.Label, itemId: item.Id);
                    break;
                }
                case MasterItemTypes.Multi:
                {
                    if (item.Options is null) break;
                    var selected = ctx.Multi(item.Id);
                    foreach (var opt in item.Options)
                    {
                        if (opt.Registry is null || !selected.Contains(opt.Value)) continue;
                        foreach (var r in opt.Registry)
                            ctx.AddRegistry(r.Id, r.Value, opt.Label, itemId: item.Id);
                    }
                    break;
                }
            }
        }
    }
}
