using FabriqStudio.Models.Gpo;
using FabriqStudio.Services.Gpo;
using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// 「グループポリシー」章: 選択したポリシー（tables["gpo_policies"]）を GPO 辞書で展開し、
/// gpo_config/gpo_list.csv の行（Segment=マスタ名）とプロファイル行 gpo_config.ps1（Registry スロット）を出す。
/// 辞書が未読込のときは警告してスキップする（プレビューは辞書の読み込み完了後に再計算される）。
/// </summary>
public sealed class GpoEmitter : IMasterEmitter
{
    public const string ItemId    = "gpo_policies";
    public const string ModuleDir = "gpo_config";
    public const string CsvName   = "gpo_list.csv";

    private readonly IGpoCatalogService _catalog;

    public GpoEmitter(IGpoCatalogService catalog)
    {
        _catalog = catalog;
    }

    public string Name => "グループポリシー";

    public void Emit(MasterContext ctx)
    {
        var table = ctx.Table(ItemId);
        if (table.Count == 0 || !ctx.IsVisible(ItemId)) return;
        if (!ctx.ModuleAvailable(ModuleDir)) return;

        var catalog = _catalog.Catalog;
        if (catalog is null)
        {
            ctx.Warn(_catalog.IsLoading
                ? "GPO 辞書（ADMX）を読み込み中のため、グループポリシーの行はまだ計算できません。読み込み完了後に再計算されます。"
                : $"GPO 辞書（ADMX）が読み込めていないため、グループポリシー {table.Count} 件を書けません。{_catalog.LoadError}", ItemId);
            return;
        }

        // 1) 各ポリシーを展開
        var all = new List<GpoRow>();
        foreach (var raw in table)
        {
            var sel = GpoSelection.FromRow(raw);
            if (sel.PolicyId.Length == 0) continue;

            var policy = catalog.FindPolicy(sel.PolicyId);
            if (policy is null)
            {
                ctx.Warn($"GPO 辞書にポリシー {sel.PolicyId}（{sel.DisplayName}）が見つかりません（ADMX の版差）。この設定は書きません。", ItemId);
                continue;
            }

            var result = GpoCompiler.Compile(policy, sel);
            foreach (var e in result.Errors)   ctx.Error($"GPO「{policy.DisplayName}」: {e}", ItemId);
            foreach (var w in result.Warnings) ctx.Warn($"GPO「{policy.DisplayName}」: {w}", ItemId);
            all.AddRange(result.Rows);
        }

        // 2) ポリシー間で同じエントリを書く場合は後勝ちにして警告（fabriq 側は重複を Error にする）
        var rows = new List<GpoRow>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in all)
        {
            if (index.TryGetValue(r.DedupeKey, out var i))
            {
                var prev = rows[i];
                if (!prev.PolicyRef.Equals(r.PolicyRef, StringComparison.OrdinalIgnoreCase))
                    ctx.Warn($"GPO「{prev.Title}」と「{r.Title}」が同じ値（{r.Scope}: {r.KeyPath}\\{r.ValueName}）を書きます。後の設定を採用します。", ItemId);
                rows[i] = r;
            }
            else
            {
                index[r.DedupeKey] = rows.Count;
                rows.Add(r);
            }
        }
        if (rows.Count == 0) return;

        // 3) CSV 行（AdminID はマスタ内の連番。Segment は AddCsvRow が付ける）
        var id = 1;
        foreach (var r in rows)
        {
            ctx.AddCsvRow(ModuleDir, CsvName, Row(
                ("Enabled",      "1"),
                ("AdminID",      (id++).ToString()),
                ("SettingTitle", r.Title),
                ("Scope",        r.Scope),
                ("KeyPath",      r.KeyPath),
                ("ValueName",    r.ValueName),
                ("Action",       r.Action),
                ("Type",         r.Type),
                ("Value",        r.Value),
                ("PolicyRef",    r.PolicyRef)));
        }

        ctx.AddProfile(ModuleDir, "gpo_config.ps1", ProfileSlot.Registry, 30, isolated: true);

        if (rows.Any(r => r.Scope == GpoPolicyClass.User))
            ctx.Info("ユーザーの構成 (User) のポリシーは各ユーザーのサインイン時に適用されます。マスタ作成中の Administrator にも次回サインイン時（または gpupdate 後）に反映されます。");
    }
}
