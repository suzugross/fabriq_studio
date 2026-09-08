using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Gpo;

namespace FabriqStudio.Services.Master;

public sealed class MasterSheetService : IMasterSheetService
{
    // 秘密情報（パスワード・プロダクトキー）も帳票では平文で出す（ユーザー指示 2026-09-08。伏せ字は使わない）。

    private readonly IRegistryCollectionService? _registry;
    private readonly IGpoCatalogService?         _gpo;
    private readonly IAppAssocService?           _appAssoc;

    /// <param name="appAssoc">既定のアプリの分類辞書（呼び出し側で EnsureLoadedAsync 済みであること）。</param>
    public MasterSheetService(IRegistryCollectionService? registry, IGpoCatalogService? gpo, IAppAssocService? appAssoc)
    {
        _registry = registry;
        _gpo      = gpo;
        _appAssoc = appAssoc;
    }

    // ═══════════════════════════════════════════════════════════════
    //  文書の組み立て
    // ═══════════════════════════════════════════════════════════════

    public SheetDocument Build(MasterTemplate template, MasterAnswers answers, MasterPlan plan)
    {
        var items = new Dictionary<string, MasterItem>(StringComparer.Ordinal);
        foreach (var it in template.Sections.SelectMany(s => s.Items)) items[it.Id] = it;

        // 項目 ID → 実際に書くレジストリ行（一時ポリシーなど副セグメントの行は除く）
        var regByItem = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var op in plan.RegistryOps)
            foreach (var r in op.Rows)
            {
                if (r.ItemId.Length == 0 || r.Segment != answers.MasterName) continue;
                if (!regByItem.TryGetValue(r.ItemId, out var list)) regByItem[r.ItemId] = list = [];
                list.Add($"{op.Hive}\\{StripHive(r.KeyPath)}\\{r.KeyName} = {(r.Value.Length == 0 ? "(空)" : r.Value)}");
            }
        _regByItem = regByItem;

        // グループポリシー: ポリシー ID → Registry.pol への書き込み先
        var gpoRows = plan.CsvOps.FirstOrDefault(o => o.ModuleDir.Equals("gpo_config", StringComparison.OrdinalIgnoreCase))?.Rows ?? [];
        _gpoWrites = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in gpoRows)
        {
            var reference = row.GetValueOrDefault("PolicyRef", "") ?? "";
            var eq = reference.IndexOf('=');
            var policyId = eq > 0 ? reference[..eq] : reference;
            if (policyId.Length == 0) continue;
            var hive   = (row.GetValueOrDefault("Scope", "") ?? "").Equals("User", StringComparison.OrdinalIgnoreCase) ? "HKCU" : "HKLM";
            var action = row.GetValueOrDefault("Action", "") ?? "";
            var key    = StripHive(row.GetValueOrDefault("KeyPath", "") ?? "");
            var name   = row.GetValueOrDefault("ValueName", "") ?? "";
            var value  = row.GetValueOrDefault("Value", "") ?? "";
            var text = action.Equals("Set", StringComparison.OrdinalIgnoreCase)
                ? $"{hive}\\{key}\\{name} = {value}"
                : $"{hive}\\{key}\\{name}（{action}）";
            if (!_gpoWrites.TryGetValue(policyId, out var list)) _gpoWrites[policyId] = list = [];
            list.Add(text);
        }

        var doc = new SheetDocument
        {
            MasterName    = answers.MasterName,
            ProjectName   = answers.ProjectName,
            Version       = answers.Version,
            Worker        = answers.Worker,
            Notes         = answers.Notes,
            GeneratedAt   = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            LastGenerated = answers.LastGenerated ?? "",
        };

        foreach (var section in template.Sections)
        {
            var sec = new SheetSection { Id = section.Id, Title = string.IsNullOrWhiteSpace(section.SheetTitle) ? section.Title : section.SheetTitle };
            SheetBlock? block = null;
            foreach (var item in section.Items)
            {
                if (item.Type is MasterItemTypes.Info or MasterItemTypes.Action) continue;
                if (item.Sheet?.Hide == true) continue;
                if (!IsVisible(item, items, answers)) continue;

                var subgroup = item.Subgroup ?? "";
                if (block is null || !block.Title.Equals(subgroup, StringComparison.Ordinal))
                {
                    block = new SheetBlock { Title = subgroup };
                    sec.Blocks.Add(block);
                }
                block.Rows.Add(BuildRow(item, answers));

                // 既定のアプリを適用する場合は、関連付けの内容をアプリ別の表で続けて出す
                if (item.Id == "sp_default_apps" && answers.IsTrue("sp_default_apps") && BuildAppAssocRow(plan) is { } assoc)
                    block.Rows.Add(assoc);
            }
            if (sec.Blocks.Count > 0) doc.Sections.Add(sec);
        }

        doc.ManualTasks.AddRange(plan.ManualTasks);
        return doc;
    }

    private Dictionary<string, List<string>> _regByItem = new(StringComparer.Ordinal);
    private Dictionary<string, List<string>> _gpoWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>visibleWhen の連鎖を回答で評価する（MasterContext.IsVisible と同じ規則）。</summary>
    private static bool IsVisible(MasterItem item, Dictionary<string, MasterItem> items, MasterAnswers answers)
    {
        var current = item;
        var guard = 0;
        while (current?.VisibleWhen is not null && guard++ < 16)
        {
            var src = current.VisibleWhen.Item;
            if (!current.VisibleWhen.Values.Contains(answers.GetValue(src))) return false;
            items.TryGetValue(src, out current);
        }
        return true;
    }

    private SheetRow BuildRow(MasterItem item, MasterAnswers answers)
    {
        var kind = item.Kind switch
        {
            MasterItemKinds.Dict   => "辞書",
            MasterItemKinds.Manual => "手動",
            MasterItemKinds.Fabriq => "fabriq側",
            _                      => "対応",
        };
        var target = item.Target ?? "";
        var value  = answers.GetValue(item.Id);
        var spec   = item.Sheet;
        var label  = string.IsNullOrWhiteSpace(spec?.Label) ? item.Label : spec!.Label!;
        var method = !string.IsNullOrWhiteSpace(spec?.Method) ? spec!.Method! : item.Kind switch
        {
            MasterItemKinds.Dict   => "レジストリ",
            MasterItemKinds.Manual => "手動",
            MasterItemKinds.Fabriq => "手動",
            _                      => "",
        };
        // 実際に書くレジストリ（キー \ 値名 = 値）を設定方法に添える
        if (_regByItem.TryGetValue(item.Id, out var regLines) && regLines.Count > 0)
            method = (method.Length > 0 ? method + "\n" : "") + string.Join("\n", regLines);

        SheetRow Row(string text = "", List<string>? lines = null, SheetTable? table = null, bool secret = false) => new()
        {
            ItemId = item.Id, Label = label, Kind = kind, Target = target, Method = method,
            Text = text, Lines = lines, Table = table, IsSecret = secret,
        };

        // sheet.values に書かれた値の表現（choice の値 / bool の 1・0 / multi の値 / text・number の値そのもの）
        string? Wording(string v) => spec?.Values is not null && spec.Values.TryGetValue(v, out var w) ? w : null;

        switch (item.Type)
        {
            case MasterItemTypes.Bool:
            {
                var on = value.Trim() == "1";
                return Row(Wording(on ? "1" : "0") ?? (on ? "○" : "－"));
            }

            case MasterItemTypes.Choice:
            {
                var opt = item.Options?.FirstOrDefault(o => o.Value == value)
                          ?? item.Options?.FirstOrDefault(o => o.Value == (item.Default ?? ""));
                var key = opt?.Value ?? value;
                return Row(Wording(key) ?? (opt is null ? (value.Length > 0 ? value : "—") : PlainLabel(opt.Label)));
            }

            case MasterItemTypes.Number:
            {
                var v = value.Trim();
                if (v.Length == 0) return Row("—");
                return Row(Wording(v) ?? (string.IsNullOrEmpty(item.Unit) ? v : $"{v} {item.Unit}"));
            }

            case MasterItemTypes.Text:
            case MasterItemTypes.Multiline:
            {
                if (Wording(value.Trim()) is { } worded) return Row(worded);
                var lines = value.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                if (lines.Count == 0) return Row("—");
                return lines.Count == 1 ? Row(lines[0]) : Row(lines: lines);
            }

            case MasterItemTypes.File:
                return Row(string.IsNullOrWhiteSpace(value) ? "（未配置）" : value.Trim());

            case MasterItemTypes.Multi:
            {
                var selected = answers.GetMulti(item.Id);
                if (selected.Count == 0) return Row("（なし）");
                var lines = selected.Select(v => Wording(v) ?? (item.Options?.FirstOrDefault(o => o.Value == v) is { } o ? PlainLabel(o.Label) : v)).ToList();
                return Row(lines: lines);
            }

            case MasterItemTypes.Table:
                return BuildTableRow(item, answers, Row);

            case MasterItemTypes.Gpo:
                return BuildGpoRow(item, answers, Row);

            case MasterItemTypes.Registry:
                return BuildRegistryRow(item, answers, Row);

            default:
                return Row(value.Length > 0 ? value : "—");
        }
    }

    /// <summary>画面用の選択肢ラベルから「（既定）」「（推奨）」などの注記を落とす（お客様向けの帳票では不要）。</summary>
    private static string PlainLabel(string label)
    {
        var l = label;
        foreach (var suffix in new[] { "（既定）", "（推奨）", "（既定・変更しない）" })
            l = l.Replace(suffix, "");
        return l.Trim();
    }

    private static SheetRow BuildTableRow(MasterItem item, MasterAnswers answers,
        Func<string, List<string>?, SheetTable?, bool, SheetRow> row)
    {
        var rows = answers.GetTable(item.Id);
        var allColumns = item.Columns ?? [];
        if (rows.Count == 0 || allColumns.Count == 0) return row("（なし）", null, null, false);

        // 出す列: sheet.columns（"A|B" = A が空なら B）か全列
        var picks = new List<(List<MasterColumn> Candidates, string Header)>();
        if (item.Sheet?.Columns is { Count: > 0 } wanted)
        {
            foreach (var w in wanted)
            {
                var cands = w.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(n => allColumns.FirstOrDefault(c => c.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    .Where(c => c is not null).Select(c => c!).ToList();
                if (cands.Count > 0) picks.Add((cands, string.IsNullOrEmpty(cands[0].Label) ? cands[0].Name : cands[0].Label));
            }
        }
        if (picks.Count == 0)
            picks = allColumns.Select(c => (new List<MasterColumn> { c }, string.IsNullOrEmpty(c.Label) ? c.Name : c.Label)).ToList();

        var table = new SheetTable();
        foreach (var p in picks) table.Headers.Add(p.Header);

        var any = false;
        foreach (var r in rows)
        {
            var cells = new List<string>();
            var empty = true;
            foreach (var p in picks)
            {
                var v = "";
                MasterColumn? used = null;
                foreach (var c in p.Candidates)
                {
                    v = r.TryGetValue(c.Name, out var cv) ? (cv ?? "").Trim() : "";
                    used = c;
                    if (v.Length > 0) break;
                }
                if (v.Length > 0) empty = false;
                if (used is not null && item.Sheet?.CellValues is { } cvMap
                         && cvMap.TryGetValue(used.Name, out var map) && map.TryGetValue(v, out var worded))
                    v = worded;
                cells.Add(v);
            }
            if (empty) continue;
            table.Rows.Add(cells);
            any = true;
        }
        return any ? row("", null, table, false) : row("（なし）", null, null, false);
    }

    /// <summary>グループポリシー: 1 ポリシー = 表の 1 行（分類 / ポリシー名 / 状態と適用対象 / 設定内容 / レジストリの書き込み先）。</summary>
    private SheetRow BuildGpoRow(MasterItem item, MasterAnswers answers,
        Func<string, List<string>?, SheetTable?, bool, SheetRow> row)
    {
        var table = new SheetTable();
        table.Headers.AddRange(["分類（パス）", "ポリシー名", "状態（適用対象）", "設定内容", "レジストリ（Registry.pol）"]);

        foreach (var raw in answers.GetTable(item.Id))
        {
            var sel = GpoSelection.FromRow(raw);
            if (sel.PolicyId.Length == 0) continue;
            var policy = _gpo?.FindPolicy(sel.PolicyId);
            var name   = policy?.DisplayName ?? (sel.DisplayName.Length > 0 ? sel.DisplayName : sel.PolicyId);
            var cls    = policy is null ? sel.Scope : (policy.IsBoth ? sel.Scope : policy.Class);
            var state  = $"{GpoStates.Label(sel.State)}（{GpoPolicyClass.Label(cls)}）";
            var detail = policy is not null && GpoStates.Normalize(sel.State) == GpoStates.Enabled ? ElementSummary(policy, sel) : "";
            var writes = _gpoWrites.TryGetValue(sel.PolicyId, out var w) ? Cap(w, 10) : "";
            table.Rows.Add([policy?.CategoryPath ?? "", name, state, detail, writes]);
        }
        return table.Rows.Count == 0 ? row("（なし）", null, null, false) : row("", null, table, false);
    }

    /// <summary>有効時に設定した要素を「ラベル: 値」で 1 行ずつ。</summary>
    private static string ElementSummary(GpoPolicy policy, GpoSelection sel)
    {
        var parts = new List<string>();
        foreach (var e in policy.ElementsForUi)
        {
            var v = sel.GetElementValue(e) ?? e.DefaultValueString();
            switch (e.Type)
            {
                case GpoElementType.Boolean:
                    parts.Add($"{e.DisplayLabel}: {(v.Trim() == "1" ? "オン" : "オフ")}");
                    break;
                case GpoElementType.Enum:
                    var it = e.Items.FirstOrDefault(i => i.Value.ToString() == v.Trim());
                    parts.Add($"{e.DisplayLabel}: {it?.DisplayName ?? v.Trim()}");
                    break;
                case GpoElementType.List:
                case GpoElementType.MultiText:
                    var entries = v.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    parts.Add($"{e.DisplayLabel}: {(entries.Length == 0 ? "（なし）" : string.Join("、", entries))}");
                    break;
                default:
                    if (v.Trim().Length > 0) parts.Add($"{e.DisplayLabel}: {v.Trim()}");
                    break;
            }
        }
        return string.Join("\n", parts);
    }

    private static string Cap(List<string> lines, int max)
        => lines.Count <= max ? string.Join("\n", lines) : string.Join("\n", lines.Take(max)) + $"\n…他 {lines.Count - max} 件";

    /// <summary>レジストリ設定: 1 件 = 表の 1 行（登録名 / キー / 値名 / 種類 / 値）。</summary>
    private SheetRow BuildRegistryRow(MasterItem item, MasterAnswers answers,
        Func<string, List<string>?, SheetTable?, bool, SheetRow> row)
    {
        var table = new SheetTable();
        table.Headers.AddRange(["設定（辞書の登録名）", "キー", "値名", "種類", "値"]);

        foreach (var raw in answers.GetTable(item.Id))
        {
            var sel = RegistrySelection.FromRow(raw);
            if (sel.Id.Length == 0) continue;
            var entry = _registry?.Entries.FirstOrDefault(e => e.Id.Equals(sel.Id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                table.Rows.Add([sel.Title.Length > 0 ? sel.Title : sel.Id, "（辞書に無し）", "", "", sel.Value]);
                continue;
            }
            table.Rows.Add([entry.Title, $"{entry.Hive}\\{StripHive(entry.KeyPath)}", entry.KeyName, entry.Type, sel.Value.Length == 0 ? "(空)" : sel.Value]);
        }
        return table.Rows.Count == 0 ? row("（なし）", null, null, false) : row("", null, table, false);
    }

    /// <summary>
    /// 既定のアプリの関連付け（計画に含まれる AppAssoc.xml の内容）を「分類 / アプリ / 対象（拡張子・プロトコル）」の表にする。
    /// 分類は appassoc_apps.json のカテゴリ（ブラウザー / PDF / メール …）で括り、どれにも当たらないものは「その他」にアプリ別でまとめる。
    /// </summary>
    private SheetRow? BuildAppAssocRow(MasterPlan plan)
    {
        SheetRow Row(string text = "", SheetTable? table = null) => new()
        {
            ItemId = "sp_default_apps_list", Label = "関連付けの内容（アプリ別）", Kind = "対応",
            Target = "default_app_config/xml", Method = "既定のアプリの関連付け（DISM）", Text = text, Table = table,
        };

        var file = plan.TextFiles.FirstOrDefault(t =>
        {
            var rel = t.RelPath.Replace('\\', '/');
            return rel.Contains("sysprep_config/source/", StringComparison.OrdinalIgnoreCase)
                   && rel.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        });
        if (file is null || string.IsNullOrWhiteSpace(file.Content)) return Row("（関連付け XML が未配置）");

        AppAssocDocument doc;
        try { doc = AppAssocDocument.Parse(file.Content); }
        catch { return Row("（関連付け XML を読めません）"); }
        if (doc.Entries.Count == 0) return Row("（関連付けなし）");

        var categories = _appAssoc?.Categories ?? [];
        string? CategoryOf(string id)
            => categories.FirstOrDefault(c => c.Identifiers.Any(i => i.Equals(id, StringComparison.OrdinalIgnoreCase)))?.Label;
        static string AppOf(AppAssocEntry e)
            => string.IsNullOrWhiteSpace(e.ApplicationName) ? e.ProgId : e.ApplicationName;

        var table = new SheetTable();
        table.Headers.AddRange(["分類", "アプリ", "対象（拡張子・プロトコル）"]);

        var groups = doc.Entries
            .GroupBy(e => (Category: CategoryOf(e.Identifier) ?? "その他", App: AppOf(e)))
            .Select(g => (g.Key.Category, g.Key.App, Ids: g.Select(e => e.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        var order = categories.Select(c => c.Label).ToList();
        int Rank(string category) { var i = order.IndexOf(category); return i < 0 ? int.MaxValue : i; }

        foreach (var g in groups.OrderBy(g => Rank(g.Category)).ThenBy(g => g.App, StringComparer.OrdinalIgnoreCase))
            table.Rows.Add([g.Category, g.App, string.Join(" ", g.Ids)]);

        return Row("", table);
    }

    private static string StripHive(string keyPath)
    {
        var k = keyPath.Trim().Trim('\\');
        foreach (var prefix in new[] { "HKEY_LOCAL_MACHINE\\", "HKEY_CURRENT_USER\\", "HKLM\\", "HKCU\\" })
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return k[prefix.Length..];
        return k;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Excel（パラメータシート）
    // ═══════════════════════════════════════════════════════════════

    private static readonly XLColor Accent    = XLColor.FromHtml("#2B4C7E");
    private static readonly XLColor HeadFill  = XLColor.FromHtml("#EEF3FA");
    private static readonly XLColor SubFill   = XLColor.FromHtml("#F5F7FB");
    private static readonly XLColor MutedText = XLColor.FromHtml("#6B7280");

    private const int LabelCol   = 1;   // A: 項目
    private const int ValueCol   = 2;   // B: 設定値（表はここから右へ、F まで）
    private const int LastCol    = 6;   // F: 設定値の右端
    private const int MethodCol  = 7;   // G: 設定方法
    private const double LabelWidth  = 30;
    private const double ValueWidth  = 40;
    private const double SubWidth    = 16;
    private const double MethodWidth = 42;

    public void SaveParameterSheetXlsx(SheetDocument doc, string path)
    {
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Yu Gothic";
        wb.Style.Font.FontSize = 10;
        wb.Properties.Title    = (doc.ProjectName.Length > 0 ? doc.ProjectName + " " : "") + "設定パラメータシート";
        wb.Properties.Author   = "fabriq studio";

        WriteParameterSheet(wb.Worksheets.Add("設定パラメータ"), doc);

        wb.SaveAs(path);
    }

    private static void WriteParameterSheet(IXLWorksheet ws, SheetDocument doc)
    {
        ws.Column(LabelCol).Width  = LabelWidth;
        ws.Column(ValueCol).Width  = ValueWidth;
        for (var c = ValueCol + 1; c <= LastCol; c++) ws.Column(c).Width = SubWidth;
        ws.Column(MethodCol).Width = MethodWidth;
        ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var r = 1;
        var title = ws.Cell(r, 1);
        title.Value = "設定パラメータシート";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 16;
        title.Style.Font.FontColor = Accent;
        ws.Range(r, 1, r, MethodCol).Merge();
        ws.Row(r).Height = 26;
        r++;

        var sub = ws.Cell(r, 1);
        sub.Value = "マスタ イメージの設定内容";
        sub.Style.Font.FontColor = MutedText;
        ws.Range(r, 1, r, MethodCol).Merge();
        r += 2;

        // 案件メタ
        void Meta(string label, string value)
        {
            var l = ws.Cell(r, 1);
            l.Value = label;
            l.Style.Font.Bold = true;
            l.Style.Fill.BackgroundColor = HeadFill;
            var v = ws.Cell(r, 2);
            v.Value = value;
            ws.Range(r, 2, r, MethodCol).Merge();
            ws.Range(r, 1, r, MethodCol).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(r, 1, r, MethodCol).Style.Border.InsideBorder  = XLBorderStyleValues.Thin;
            r++;
        }
        Meta("案件名",  doc.ProjectName.Length > 0 ? doc.ProjectName : "—");
        Meta("マスタ名", doc.MasterName);
        Meta("版",      doc.Version.Length > 0 ? doc.Version : "1");
        Meta("担当",    doc.Worker.Length > 0 ? doc.Worker : "—");
        if (doc.Notes.Length > 0) Meta("メモ", doc.Notes);
        Meta("作成日",  doc.GeneratedAt);
        if (doc.LastGenerated.Length > 0) Meta("最終生成", doc.LastGenerated);
        r++;

        var legend = ws.Cell(r, 1);
        legend.Value = "○ = 実施する　－ = 実施しない";
        legend.Style.Font.FontColor = MutedText;
        legend.Style.Font.FontSize = 9;
        ws.Range(r, 1, r, MethodCol).Merge();
        r += 2;

        foreach (var sec in doc.Sections)
        {
            var h = ws.Cell(r, 1);
            h.Value = sec.Title;
            h.Style.Font.Bold = true;
            h.Style.Font.FontSize = 12;
            h.Style.Font.FontColor = XLColor.White;
            h.Style.Fill.BackgroundColor = Accent;
            ws.Range(r, 1, r, MethodCol).Merge();
            ws.Row(r).Height = 20;
            r++;

            var th1 = ws.Cell(r, LabelCol);  th1.Value = "項目";
            var th2 = ws.Cell(r, ValueCol);  th2.Value = "設定値";
            var th3 = ws.Cell(r, MethodCol); th3.Value = "設定方法";
            ws.Range(r, ValueCol, r, LastCol).Merge();
            var head = ws.Range(r, 1, r, MethodCol);
            head.Style.Font.Bold = true;
            head.Style.Fill.BackgroundColor = HeadFill;
            head.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            head.Style.Border.InsideBorder  = XLBorderStyleValues.Thin;
            r++;

            foreach (var block in sec.Blocks)
            {
                if (block.Title.Length > 0)
                {
                    var b = ws.Cell(r, 1);
                    b.Value = "■ " + block.Title;
                    b.Style.Font.Bold = true;
                    b.Style.Font.FontColor = Accent;
                    b.Style.Fill.BackgroundColor = SubFill;
                    ws.Range(r, 1, r, MethodCol).Merge();
                    ws.Range(r, 1, r, MethodCol).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    r++;
                }

                foreach (var row in block.Rows)
                {
                    var label = ws.Cell(r, LabelCol);
                    label.Value = row.Label;
                    label.Style.Alignment.WrapText = true;

                    var methodCell = ws.Cell(r, MethodCol);
                    methodCell.Value = row.Method;
                    methodCell.Style.Alignment.WrapText = true;
                    methodCell.Style.Font.FontColor = MutedText;
                    methodCell.Style.Font.FontSize = 9;

                    if (row.Table is not null)
                    {
                        // 見出し行: 項目名 + 件数 + 設定方法、続く行に表（B 列から右へ、F 列まで）
                        var count = ws.Cell(r, ValueCol);
                        count.Value = $"{row.Table.Rows.Count} 件";
                        count.Style.Font.FontColor = MutedText;
                        ws.Range(r, ValueCol, r, LastCol).Merge();
                        Border(ws, r, MethodCol);
                        ws.Row(r).Height = Math.Max(RowHeight(row.Label, LabelWidth), RowHeight(row.Method, MethodWidth));
                        r++;

                        var cols = Math.Min(row.Table.Headers.Count, LastCol - ValueCol + 1);
                        var lastTableCol = ValueCol + cols - 1;
                        for (var c = 0; c < cols; c++)
                        {
                            var hc = ws.Cell(r, ValueCol + c);
                            hc.Value = row.Table.Headers[c];
                            hc.Style.Font.Bold = true;
                            hc.Style.Fill.BackgroundColor = SubFill;
                            hc.Style.Alignment.WrapText = true;
                        }
                        if (lastTableCol < LastCol) ws.Range(r, lastTableCol + 1, r, LastCol).Style.Fill.BackgroundColor = SubFill;
                        Border(ws, r, MethodCol);
                        r++;
                        foreach (var cells in row.Table.Rows)
                        {
                            double height = 15;
                            for (var c = 0; c < cols && c < cells.Count; c++)
                            {
                                var cell = ws.Cell(r, ValueCol + c);
                                cell.Value = cells[c];
                                cell.Style.Alignment.WrapText = true;
                                height = Math.Max(height, RowHeight(cells[c], c == 0 ? ValueWidth : SubWidth));
                            }
                            Border(ws, r, MethodCol);
                            ws.Row(r).Height = height;
                            r++;
                        }
                        continue;
                    }

                    var value = ws.Cell(r, ValueCol);
                    var text  = row.Lines is not null ? string.Join("\n", row.Lines.Select(l => "・" + l)) : row.Text;
                    value.Value = text;
                    value.Style.Alignment.WrapText = true;
                    if (row.IsSecret) value.Style.Font.FontColor = MutedText;
                    ws.Range(r, ValueCol, r, LastCol).Merge();
                    Border(ws, r, MethodCol);
                    ws.Row(r).Height = Math.Max(Math.Max(RowHeight(row.Label, LabelWidth), RowHeight(text, ValueWidth + (LastCol - ValueCol) * SubWidth)), RowHeight(row.Method, MethodWidth));
                    r++;
                }
            }
            r++;
        }

        // 印刷設定: A4 横、横 1 ページに収める、ページ番号
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PagesWide = 1;
        ws.PageSetup.PagesTall = 0;
        ws.PageSetup.Margins.Top = 0.6; ws.PageSetup.Margins.Bottom = 0.6;
        ws.PageSetup.Margins.Left = 0.5; ws.PageSetup.Margins.Right = 0.5;
        ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
        ws.PageSetup.Footer.Right.AddText(" / ");
        ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.NumberOfPages);
        ws.PageSetup.Footer.Left.AddText(wbTitle(doc));
        ws.PageSetup.PrintAreas.Clear();
        ws.PageSetup.PrintAreas.Add(1, 1, Math.Max(r - 1, 1), MethodCol);

        static string wbTitle(SheetDocument d) => (d.ProjectName.Length > 0 ? d.ProjectName + " " : "") + "設定パラメータシート";
    }

    private static void Border(IXLWorksheet ws, int row, int lastCol)
    {
        var range = ws.Range(row, 1, row, lastCol);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder  = XLBorderStyleValues.Thin;
    }

    /// <summary>折り返しを考えた行の高さ（Excel は開いたときに自動調整しないため概算で決める）。全角 = 幅 2、1 行 = 15pt。</summary>
    private static double RowHeight(string text, double columnWidth)
    {
        if (string.IsNullOrEmpty(text)) return 15;
        var lines = 0;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            double width = 0;
            foreach (var ch in line) width += ch > 0xFF ? 2 : 1;
            lines += Math.Max(1, (int)Math.Ceiling(width / Math.Max(columnWidth - 1, 1)));
        }
        return Math.Min(15 * lines + 2, 400);
    }

    // ═══════════════════════════════════════════════════════════════
    //  HTML（チェックリスト）
    // ═══════════════════════════════════════════════════════════════

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private const string Css = """
        :root{--ink:#1f2937;--muted:#6b7280;--line:#cfd6e0;--head:#eef3fa;--accent:#2b4c7e}
        *{box-sizing:border-box}
        body{margin:0;padding:28px 36px;font-family:"Yu Gothic UI","Meiryo UI","Meiryo","Segoe UI",sans-serif;color:var(--ink);font-size:12.5px;line-height:1.55;background:#fff}
        h1{font-size:22px;margin:0 0 2px;color:var(--accent)}
        p.sub{color:var(--muted);margin:0 0 16px}
        table.meta{border-collapse:collapse;margin:0 0 16px;min-width:460px}
        table.meta th,table.meta td{border:1px solid var(--line);padding:5px 10px;text-align:left;vertical-align:top}
        table.meta th{background:var(--head);width:110px;font-weight:600;white-space:nowrap}
        h2{font-size:15.5px;margin:24px 0 8px;padding:6px 10px;background:var(--accent);color:#fff;border-radius:3px;page-break-after:avoid;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        h2 .secdone{float:right;font-weight:400;font-size:12px;opacity:.9}
        h3{font-size:13px;margin:12px 0 6px;color:var(--accent);border-left:4px solid var(--accent);padding-left:8px;page-break-after:avoid}
        table.items{width:100%;border-collapse:collapse;margin-bottom:6px}
        table.items th,table.items td{border:1px solid var(--line);padding:5px 9px;vertical-align:top;text-align:left}
        table.items th{background:var(--head);font-weight:600;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        table.items tr{page-break-inside:avoid}
        td.label{width:30%}
        td.chk{width:34px;text-align:center}
        td.chk input{width:18px;height:18px;cursor:pointer}
        td.target{width:18%;color:var(--muted);font-size:11px;white-space:pre-line;word-break:break-all}
        td.memo{width:18%}
        td.memo input{width:100%;border:1px solid var(--line);border-radius:3px;padding:3px 6px;font:inherit;font-size:12px}
        tr.done td{background:#f2faf5}
        tr.done td.label,tr.done td.value{color:var(--muted)}
        ul.lines{margin:0;padding-left:18px}
        table.sub{border-collapse:collapse;margin:1px 0;font-size:12px}
        table.sub th,table.sub td{border:1px solid var(--line);padding:3px 8px;vertical-align:top}
        table.sub th{background:#f5f7fb;font-weight:600}
        .secret{color:var(--muted)}
        .bar{display:flex;align-items:center;gap:14px;margin:0 0 16px;padding:10px 14px;background:var(--head);border-radius:4px}
        .bar .count{font-weight:700;font-size:15px;color:var(--accent);min-width:150px}
        .bar progress{width:240px;height:12px}
        .bar button{font:inherit;font-size:12px;padding:5px 12px;border:1px solid var(--line);border-radius:3px;background:#fff;cursor:pointer}
        .sign{display:flex;gap:18px;margin:0 0 14px}
        .sign label{display:flex;align-items:center;gap:6px}
        .sign input{border:1px solid var(--line);border-radius:3px;padding:4px 8px;font:inherit}
        footer{margin-top:28px;color:var(--muted);font-size:11px;border-top:1px solid var(--line);padding-top:8px}
        @page{size:A4;margin:15mm 13mm}
        @media print{body{padding:0}.bar button{display:none}td.memo input,.sign input{border:none;border-bottom:1px solid var(--line);border-radius:0}}
        """;

    /// <summary>チェックリストのスクリプト（__KEY__ は localStorage のキー）。チェックと備考を保存し、進捗を出す。</summary>
    private const string ChecklistScript = """
        <script>
        (function () {
          var KEY = __KEY__;
          var state = {};
          try { state = JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { state = {}; }
          var rows = Array.prototype.slice.call(document.querySelectorAll('tr[data-key]'));
          function save() { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) {} }
          function refresh() {
            var done = 0, perSection = {};
            rows.forEach(function (tr) {
              var cb = tr.querySelector('input.check');
              var sec = tr.getAttribute('data-section');
              perSection[sec] = perSection[sec] || { done: 0, total: 0 };
              perSection[sec].total++;
              if (cb.checked) { done++; perSection[sec].done++; tr.classList.add('done'); } else { tr.classList.remove('done'); }
            });
            document.getElementById('count').textContent = done + ' / ' + rows.length + ' 確認済み';
            var p = document.getElementById('progress'); p.max = rows.length || 1; p.value = done;
            Array.prototype.slice.call(document.querySelectorAll('h2[data-section]')).forEach(function (h) {
              var s = perSection[h.getAttribute('data-section')];
              var span = h.querySelector('.secdone');
              if (s && span) span.textContent = s.done + ' / ' + s.total;
            });
          }
          rows.forEach(function (tr) {
            var key = tr.getAttribute('data-key');
            var cb = tr.querySelector('input.check');
            var note = tr.querySelector('input.note');
            var st = state[key] || {};
            cb.checked = !!st.c;
            if (note) note.value = st.n || '';
            cb.addEventListener('change', function () { state[key] = { c: cb.checked, n: note ? note.value : '' }; save(); refresh(); });
            if (note) note.addEventListener('input', function () { state[key] = { c: cb.checked, n: note.value }; save(); });
          });
          ['checker', 'checkdate'].forEach(function (id) {
            var el = document.getElementById(id);
            el.value = (state['_' + id] || '');
            el.addEventListener('input', function () { state['_' + id] = el.value; save(); });
          });
          document.getElementById('clear').addEventListener('click', function () {
            if (!confirm('すべてのチェックと備考を消します。よろしいですか？')) return;
            state = {}; save();
            rows.forEach(function (tr) { tr.querySelector('input.check').checked = false; var n = tr.querySelector('input.note'); if (n) n.value = ''; });
            refresh();
          });
          refresh();
        })();
        </script>
        """;

    public string ToChecklistHtml(SheetDocument doc)
    {
        var sb = new StringBuilder(96 * 1024);
        var title = (doc.ProjectName.Length > 0 ? doc.ProjectName + " " : "") + "設定確認チェックリスト";
        var storageKey = "fabriq-checklist:" + doc.MasterName;

        sb.Append("<!doctype html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(E(title)).Append("</title>\n<style>\n").Append(Css).Append("\n</style>\n</head>\n<body>\n");
        sb.Append("<h1>設定確認チェックリスト</h1>\n<p class=\"sub\">設定が反映されていることを目視で確認し、チェックを入れてください。チェックと備考はこのブラウザーに保存されます。</p>\n");

        sb.Append("<table class=\"meta\">\n");
        sb.Append("<tr><th>案件名</th><td>").Append(E(doc.ProjectName.Length > 0 ? doc.ProjectName : "—")).Append("</td></tr>\n");
        sb.Append("<tr><th>マスタ名</th><td>").Append(E(doc.MasterName)).Append("</td></tr>\n");
        sb.Append("<tr><th>版</th><td>").Append(E(doc.Version.Length > 0 ? doc.Version : "1")).Append("</td></tr>\n");
        sb.Append("<tr><th>担当</th><td>").Append(E(doc.Worker.Length > 0 ? doc.Worker : "—")).Append("</td></tr>\n");
        sb.Append("<tr><th>作成日</th><td>").Append(E(doc.GeneratedAt)).Append("</td></tr>\n");
        if (doc.LastGenerated.Length > 0) sb.Append("<tr><th>最終生成</th><td>").Append(E(doc.LastGenerated)).Append("</td></tr>\n");
        sb.Append("</table>\n");

        sb.Append("<div class=\"sign\"><label>確認者 <input type=\"text\" id=\"checker\" size=\"16\"></label><label>確認日 <input type=\"date\" id=\"checkdate\"></label></div>\n");
        sb.Append("<div class=\"bar\"><span class=\"count\" id=\"count\">0 / 0</span><progress id=\"progress\" value=\"0\" max=\"1\"></progress>")
          .Append("<button type=\"button\" onclick=\"window.print()\">印刷</button><button type=\"button\" id=\"clear\">すべて解除</button></div>\n");

        foreach (var sec in doc.Sections)
        {
            sb.Append("<h2 data-section=\"").Append(E(sec.Id)).Append("\">").Append(E(sec.Title)).Append("<span class=\"secdone\"></span></h2>\n");
            foreach (var block in sec.Blocks)
            {
                if (block.Title.Length > 0) sb.Append("<h3>").Append(E(block.Title)).Append("</h3>\n");
                sb.Append("<table class=\"items\">\n<tr><th class=\"chk\">確認</th><th class=\"label\">項目</th><th>設定値</th><th class=\"target\">設定方法 / 反映先</th><th class=\"memo\">備考</th></tr>\n");
                foreach (var r in block.Rows)
                {
                    sb.Append("<tr data-key=\"chk-").Append(E(r.ItemId)).Append("\" data-section=\"").Append(E(sec.Id)).Append("\">");
                    sb.Append("<td class=\"chk\"><input type=\"checkbox\" class=\"check\"></td>");
                    sb.Append("<td class=\"label\">").Append(E(r.Label)).Append("</td><td class=\"value\">");
                    AppendValue(sb, r);
                    sb.Append("</td><td class=\"target\">").Append(E(r.Method.Length > 0 ? r.Method : r.Target.Length > 0 ? r.Target : r.Kind)).Append("</td>");
                    sb.Append("<td class=\"memo\"><input type=\"text\" class=\"note\"></td></tr>\n");
                }
                sb.Append("</table>\n");
            }
        }

        if (doc.ManualTasks.Count > 0)
        {
            sb.Append("<h2 data-section=\"manual\">手作業で実施する項目<span class=\"secdone\"></span></h2>\n");
            sb.Append("<table class=\"items\">\n<tr><th class=\"chk\">実施</th><th>内容</th><th class=\"memo\">備考</th></tr>\n");
            var n = 0;
            foreach (var t in doc.ManualTasks)
            {
                sb.Append("<tr data-key=\"chk-manual-").Append(n++).Append("\" data-section=\"manual\"><td class=\"chk\"><input type=\"checkbox\" class=\"check\"></td><td>")
                  .Append(E(t)).Append("</td><td class=\"memo\"><input type=\"text\" class=\"note\"></td></tr>\n");
            }
            sb.Append("</table>\n");
        }

        sb.Append("<footer>fabriq studio マスタ設計から出力　").Append(E(doc.GeneratedAt)).Append("</footer>\n");
        sb.Append(ChecklistScript.Replace("__KEY__", System.Text.Json.JsonSerializer.Serialize(storageKey)));
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    private static void AppendValue(StringBuilder sb, SheetRow r)
    {
        if (r.Table is not null)
        {
            sb.Append("<table class=\"sub\">\n<tr>");
            foreach (var h in r.Table.Headers) sb.Append("<th>").Append(E(h)).Append("</th>");
            sb.Append("</tr>\n");
            foreach (var row in r.Table.Rows)
            {
                sb.Append("<tr>");
                foreach (var c in row) sb.Append("<td>").Append(E(c)).Append("</td>");
                sb.Append("</tr>\n");
            }
            sb.Append("</table>");
            return;
        }
        if (r.Lines is not null)
        {
            sb.Append("<ul class=\"lines\">");
            foreach (var l in r.Lines) sb.Append("<li>").Append(E(l)).Append("</li>");
            sb.Append("</ul>");
            return;
        }
        if (r.IsSecret) sb.Append("<span class=\"secret\">").Append(E(r.Text)).Append("</span>");
        else sb.Append(E(r.Text));
    }
}
