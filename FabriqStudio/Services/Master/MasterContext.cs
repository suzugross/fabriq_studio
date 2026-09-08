using FabriqStudio.Models;
using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>プロファイル内の配置スロット。値の小さい順に並び、Group 列の名前にもなる。</summary>
public enum ProfileSlot
{
    Base     = 100,
    Account  = 200,
    Registry = 300,
    System   = 400,
    Apps     = 500,
    Desktop  = 600,
    Printer  = 700,
    Finalize = 800,
    /// <summary>Sysprep プロファイル側（Order 順に並べるだけ）。</summary>
    Sysprep  = 900,
}

/// <summary>Emitter がプロファイルへ要求する 1 行。最終的な Order・マーカーは Assembler が決める。</summary>
public sealed class ProfileRequest
{
    public string      Module    { get; init; } = "";
    public string      Script    { get; init; } = "";
    public ProfileSlot Slot      { get; init; }
    public int         Order     { get; init; }
    /// <summary>true = Segment=マスタ名 を付ける（このモジュールの CSV に生成行を書いた）。</summary>
    public bool        Isolated  { get; init; }
    public string      ErrorMode { get; init; } = "";
    /// <summary>どのプロファイルに載せるか。</summary>
    public ProfileKind Kind      { get; init; } = ProfileKind.Master;
    /// <summary>副セグメント（例: app01:GoogleChrome）。指定時は Segment=マスタ名:副セグメント になる。</summary>
    public string?     SubSegment  { get; init; }
    /// <summary>Description の上書き（省略時は module.csv の MenuName）。</summary>
    public string?     Description { get; init; }
    public int         Sequence  { get; set; }
}

/// <summary>レジストリ辞書由来の 1 行（ハイブ別ファイルに振り分ける前）。</summary>
public sealed class RegistryRequest
{
    public RegistryTemplateEntry Entry { get; init; } = new();
    public string Value        { get; init; } = "";
    public string SettingTitle { get; init; } = "";
    /// <summary>副セグメント（null = 通常のマスタ行。"temp" = マスタ作成中だけの一時ポリシー）。</summary>
    public string? SubSegment  { get; init; }
    /// <summary>この行を出したテンプレート項目の ID（帳票で「実際に書いたレジストリ」を項目に結び付ける）。</summary>
    public string? ItemId      { get; init; }
}

/// <summary>
/// Emitter に渡す作業文脈。回答の参照ヘルパと、計画への追加ヘルパをまとめる。
/// Emitter は本クラス経由でのみ Plan を触る（書き込み先の解決・隔離・暗号化を一元化するため）。
/// </summary>
public sealed class MasterContext
{
    public MasterTemplate          Template   { get; }
    public MasterAnswers           Answers    { get; }
    public MasterWorkspaceSnapshot Snapshot   { get; }
    public MasterPlan              Plan       { get; }
    public IMasterTargetResolver   Resolver   { get; }
    public string                  MasterName { get; }

    /// <summary>[master:名] 形式のタグ（Segment 列が無い CSV の隔離用）。</summary>
    public string Tag => $"[master:{MasterName}]";

    /// <summary>
    /// その Segment 値がマスタの所有行か。マスタ名と一致、またはマスタ名 + ":" で始まるもの
    /// （副セグメント。例: M_x:app01:GoogleChrome）。マスタ名にコロンは使えないため他マスタと衝突しない。
    /// </summary>
    public static bool OwnsSegment(string masterName, string? segment)
    {
        var seg = segment?.Trim() ?? "";
        return seg == masterName || seg.StartsWith(masterName + ":", StringComparison.Ordinal);
    }

    /// <summary>CSV 内でマスタが所有する行数（副セグメントを含む）。</summary>
    public static int CountOwnedRows(MasterCsvInfo csv, string masterName)
        => csv.SegmentCounts.Where(kv => OwnsSegment(masterName, kv.Key)).Sum(kv => kv.Value);

    /// <summary>副セグメントの完全な Segment 値。</summary>
    public string SegmentFor(string? subSegment)
        => string.IsNullOrEmpty(subSegment) ? MasterName : $"{MasterName}:{subSegment}";

    /// <summary>module.csv の MenuName（無ければスクリプト名）。</summary>
    public string MenuName(string moduleDir, string script)
        => Snapshot.GetModule(moduleDir)?.ScriptMenuNames.GetValueOrDefault(script)
           ?? System.IO.Path.GetFileNameWithoutExtension(script);

    public List<ProfileRequest>  ProfileRequests  { get; } = [];
    public List<RegistryRequest> RegistryRequests { get; } = [];

    private readonly IReadOnlyDictionary<string, RegistryTemplateEntry> _dictionary;
    private readonly IReadOnlyDictionary<string, MasterItem>            _items;
    private readonly Func<string, string>?                              _encrypt;
    private readonly HashSet<string> _missingModulesWarned = new(StringComparer.OrdinalIgnoreCase);
    private bool _plainSecretWarned;
    private int  _sequence;

    public MasterContext(
        MasterTemplate template,
        MasterAnswers answers,
        MasterWorkspaceSnapshot snapshot,
        IReadOnlyList<RegistryTemplateEntry> dictionary,
        IMasterTargetResolver resolver,
        Func<string, string>? encrypt)
    {
        Template   = template;
        Answers    = answers;
        Snapshot   = snapshot;
        Resolver   = resolver;
        MasterName = answers.MasterName;
        Plan       = new MasterPlan { MasterName = answers.MasterName };
        _encrypt   = encrypt;

        var dict = new Dictionary<string, RegistryTemplateEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in dictionary) dict[e.Id] = e;
        _dictionary = dict;

        var items = new Dictionary<string, MasterItem>(StringComparer.Ordinal);
        foreach (var item in template.Sections.SelectMany(s => s.Items)) items[item.Id] = item;
        _items = items;
    }

    // ── 回答の参照 ────────────────────────────────────────────────

    public MasterItem? Item(string id) => _items.TryGetValue(id, out var i) ? i : null;

    /// <summary>レジストリ辞書のエントリ（無ければ null）。Emitter が衝突確認や表示名の解決に使う。</summary>
    public RegistryTemplateEntry? DictionaryEntry(string id)
        => _dictionary.TryGetValue(id, out var e) ? e : null;

    public string Label(string id) => Item(id)?.Label ?? id;

    /// <summary>値（未回答ならテンプレートの既定値、それも無ければ空）。</summary>
    public string Get(string id)
    {
        if (Answers.Values.TryGetValue(id, out var v)) return v ?? "";
        return Item(id)?.Default ?? "";
    }

    public bool IsTrue(string id) => Get(id).Trim() == "1";

    public bool IsEmpty(string id) => string.IsNullOrWhiteSpace(Get(id));

    public int? GetInt(string id)
        => int.TryParse(Get(id).Trim(), out var n) ? n : null;

    public IReadOnlyList<string> Multi(string id) => Answers.GetMulti(id);

    public IReadOnlyList<Dictionary<string, string>> Table(string id) => Answers.GetTable(id);

    /// <summary>選択肢オブジェクト（choice の現在値に対応するもの）。</summary>
    public MasterChoice? SelectedChoice(string id)
    {
        var item = Item(id);
        if (item?.Options is null) return null;
        var v = Get(id);
        return item.Options.FirstOrDefault(o => o.Value == v);
    }

    /// <summary>visibleWhen を辿って、その質問が画面上で有効（表示）かを返す。非表示の値は無視するために使う。</summary>
    public bool IsVisible(string id)
    {
        var guard = 0;
        var current = Item(id);
        while (current?.VisibleWhen is not null && guard++ < 16)
        {
            var src = current.VisibleWhen.Item;
            if (!current.VisibleWhen.Values.Contains(Get(src))) return false;
            current = Item(src);
        }
        return true;
    }

    // ── モジュール ───────────────────────────────────────────────

    /// <summary>モジュールがワークスペースに存在するか。無ければ 1 回だけ警告を出す。</summary>
    public bool ModuleAvailable(string moduleDir)
    {
        if (Snapshot.HasModule(moduleDir)) return true;
        if (_missingModulesWarned.Add(moduleDir))
            Warn($"モジュール {moduleDir} がワークスペースにありません。関連する設定はスキップします。");
        return false;
    }

    // ── 計画への追加 ─────────────────────────────────────────────

    /// <summary>
    /// モジュール CSV に 1 行追加する。Segment 列があれば Segment=マスタ名（<paramref name="subSegment"/> 指定時は
    /// マスタ名:副セグメント）、無ければ Description にタグを付ける。
    /// CSV が無い／列が無い場合は警告してスキップする（fabriq 側の版差を黙って壊さない）。
    /// 同一内容の行は 1 回だけ追加する（複数 Emitter が同じ行を要求しても重複しない）。
    /// </summary>
    public void AddCsvRow(string moduleDir, string csvName, Dictionary<string, string> row, string? subSegment = null)
    {
        if (!ModuleAvailable(moduleDir)) return;
        var module = Snapshot.GetModule(moduleDir)!;

        if (!module.Csvs.TryGetValue(csvName, out var csv))
        {
            Warn($"{moduleDir}/{csvName} が見つかりません。行の追加をスキップします。");
            return;
        }

        var op = Plan.CsvOps.FirstOrDefault(o =>
            o.ModuleDir.Equals(moduleDir, StringComparison.OrdinalIgnoreCase) &&
            o.CsvName.Equals(csvName, StringComparison.OrdinalIgnoreCase));

        if (op is null)
        {
            PlanIsolation isolation;
            if (csv.HasSegment)                    isolation = PlanIsolation.Segment;
            else if (csv.HasColumn("Description")) isolation = PlanIsolation.DescriptionTag;
            else                                   isolation = PlanIsolation.None;

            op = new PlanCsvRows
            {
                ModuleDir = moduleDir,
                CsvName   = csvName,
                AbsPath   = csv.AbsPath,
                RelPath   = Resolver.ToRelative(csv.AbsPath),
                Isolation = isolation,
                Tag       = Tag,
                ExistingIsolatedRows = isolation switch
                {
                    PlanIsolation.Segment        => CountOwnedRows(csv, MasterName),
                    PlanIsolation.DescriptionTag => csv.TagCounts.GetValueOrDefault(Tag),
                    _                            => 0,
                },
            };

            if (isolation == PlanIsolation.None)
                Warn($"{op.RelPath} には Segment 列も Description 列も無いため、再生成時に以前の行を取り除けません。");

            Plan.CsvOps.Add(op);
        }

        // 隔離マーカーを確実に付ける
        var normalized = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
        switch (op.Isolation)
        {
            case PlanIsolation.Segment:
                normalized["Segment"] = SegmentFor(subSegment);
                break;
            case PlanIsolation.DescriptionTag:
                var desc = normalized.GetValueOrDefault("Description", "");
                if (!desc.Contains(Tag, StringComparison.Ordinal))
                    normalized["Description"] = string.IsNullOrEmpty(desc) ? Tag : $"{desc} {Tag}";
                break;
        }

        // 存在しない列は警告（ファイル・列ごとに 1 回）してから落とす
        foreach (var key in normalized.Keys.ToList())
        {
            if (csv.HasColumn(key)) continue;
            if (_columnWarned.Add($"{op.RelPath}:{key}"))
                Warn($"{op.RelPath} に列 {key} がありません（fabriq の版差）。この列の値は書きません。");
            normalized.Remove(key);
        }

        // 重複除去
        if (op.Rows.Any(r => RowEquals(r, normalized))) return;
        op.Rows.Add(normalized);
    }

    /// <summary>
    /// モジュール配下にテキストファイルを生成する（例: odt_config/assets/M365/configuration.xml）。
    /// 同じパスへの 2 回目以降の要求は後勝ち。
    /// </summary>
    public void AddTextFile(string moduleDir, string relativePath, string content, string label)
    {
        if (!ModuleAvailable(moduleDir)) return;
        var module = Snapshot.GetModule(moduleDir)!;
        var abs    = System.IO.Path.Combine(module.AbsPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        Plan.TextFiles.RemoveAll(t => t.AbsPath.Equals(abs, StringComparison.OrdinalIgnoreCase));
        Plan.TextFiles.Add(new PlanTextFile
        {
            AbsPath = abs,
            RelPath = Resolver.ToRelative(abs),
            Exists  = System.IO.File.Exists(abs),
            Content = content,
            Label   = label,
        });
    }

    /// <summary>
    /// kernel/csv/hostlist.csv に 1 行追加する（AdminID = マスタ名 で隔離。マスタ作成時の仮ホスト名用）。
    /// hostlist が無ければ Error。
    /// </summary>
    public void AddHostlistRow(Dictionary<string, string> row, string adminId)
    {
        var host = Snapshot.Hostlist;
        if (host is null || host.Headers.Count == 0)
        {
            Error("kernel/csv/hostlist.csv が見つからない（または読めない）ため、仮ホスト名を書けません。");
            return;
        }

        var op = Plan.CsvOps.FirstOrDefault(o => o.AbsPath.Equals(host.AbsPath, StringComparison.OrdinalIgnoreCase));
        if (op is null)
        {
            op = new PlanCsvRows
            {
                ModuleDir = "kernel/csv",
                CsvName   = host.Name,
                AbsPath   = host.AbsPath,
                RelPath   = Resolver.ToRelative(host.AbsPath),
                Isolation = PlanIsolation.AdminId,
                Tag       = Tag,
                AdminIdKey = adminId,
                // 旧版が書いた AdminID = マスタ名 の行と、この管理番号の行を置き換える
                ExistingIsolatedRows = host.AdminIdCounts.GetValueOrDefault(MasterName) + host.AdminIdCounts.GetValueOrDefault(adminId),
            };
            Plan.CsvOps.Add(op);
        }

        var normalized = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase)
        {
            ["AdminID"] = adminId,
        };
        foreach (var key in normalized.Keys.ToList())
        {
            if (host.HasColumn(key)) continue;
            if (_columnWarned.Add($"{op.RelPath}:{key}"))
                Warn($"{op.RelPath} に列 {key} がありません。この列の値は書きません。");
            normalized.Remove(key);
        }

        if (op.Rows.Any(r => RowEquals(r, normalized))) return;
        op.Rows.Add(normalized);
    }

    private readonly HashSet<string> _columnWarned = new(StringComparer.OrdinalIgnoreCase);

    private static bool RowEquals(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv)) return false;
            if (!string.Equals(v ?? "", bv ?? "", StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>
    /// レジストリ辞書のエントリを 1 行追加する。<paramref name="valueOverride"/> が非 null ならその値で上書き。
    /// 同じ KeyPath + KeyName は後勝ちで 1 行にまとめる（選択肢の値上書きが確実に効くように）。
    /// </summary>
    public void AddRegistry(string dictId, string? valueOverride, string? sourceLabel, string? subSegment = null, string? itemId = null)
    {
        if (!_dictionary.TryGetValue(dictId, out var entry))
        {
            Warn($"レジストリ辞書に ID {dictId} がありません（{sourceLabel ?? "テンプレート"}）。この設定は書きません。");
            return;
        }

        var value = valueOverride ?? entry.Value;
        var title = string.IsNullOrEmpty(sourceLabel) ? entry.Title : $"{sourceLabel}: {entry.Title}";

        RegistryRequests.RemoveAll(r =>
            string.Equals(r.SubSegment, subSegment, StringComparison.Ordinal) &&
            r.Entry.Hive.Equals(entry.Hive, StringComparison.OrdinalIgnoreCase) &&
            r.Entry.KeyPath.Equals(entry.KeyPath, StringComparison.OrdinalIgnoreCase) &&
            r.Entry.KeyName.Equals(entry.KeyName, StringComparison.OrdinalIgnoreCase));

        RegistryRequests.Add(new RegistryRequest { Entry = entry, Value = value, SettingTitle = title, SubSegment = subSegment, ItemId = itemId });
    }

    /// <summary>
    /// プロファイル行を要求する。同一モジュール・スクリプト・副セグメント・プロファイル種別は 1 回だけ。
    /// <paramref name="subSegment"/> を指定すると同じスクリプトを副セグメント違いで複数行並べられる
    /// （FlexProfile で 1 行 = 1 アプリとして個別実行するための形）。
    /// </summary>
    public void AddProfile(
        string moduleDir, string script, ProfileSlot slot, int order,
        bool isolated, string errorMode = "",
        string? subSegment = null, string? description = null, ProfileKind? kind = null)
    {
        if (!ModuleAvailable(moduleDir)) return;

        var profileKind = kind ?? ProfileKind.Master;

        if (ProfileRequests.Any(p =>
                p.Module.Equals(moduleDir, StringComparison.OrdinalIgnoreCase) &&
                p.Script.Equals(script, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.SubSegment, subSegment, StringComparison.Ordinal) &&
                p.Kind == profileKind))
            return;

        ProfileRequests.Add(new ProfileRequest
        {
            Module      = moduleDir,
            Script      = script,
            Slot        = slot,
            Order       = order,
            Isolated    = isolated || subSegment is not null,
            ErrorMode   = errorMode,
            Kind        = profileKind,
            SubSegment  = subSegment,
            Description = description,
            Sequence    = _sequence++,
        });
    }

    /// <summary>モジュール配下のファイルの絶対パス（モジュールが無ければ null）。資材の存在確認・読み込みに使う。</summary>
    public string? ModuleFile(string moduleDir, string relativePath)
    {
        var module = Snapshot.GetModule(moduleDir);
        return module is null ? null : System.IO.Path.Combine(module.AbsPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    /// <summary>秘密情報を ENC: 化する。パスフレーズ未設定なら平文のまま（1 回だけ警告）。既に ENC: なら変えない。</summary>
    public string Secret(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("ENC:", StringComparison.Ordinal)) return value;
        if (_encrypt is not null) return _encrypt(value);

        if (!_plainSecretWarned)
        {
            _plainSecretWarned = true;
            Warn("パスフレーズが未設定のため、パスワード等を平文で書き込みます。左ペイン下部の「🔑 パスフレーズ」で設定すると ENC: 暗号化されます。");
        }
        return value;
    }

    public void Info(string message, string? itemId = null)
        => Plan.Messages.Add(new PlanMessage { Severity = PlanSeverity.Info, Message = message, ItemId = itemId });

    public void Warn(string message, string? itemId = null)
        => Plan.Messages.Add(new PlanMessage { Severity = PlanSeverity.Warning, Message = message, ItemId = itemId });

    public void Error(string message, string? itemId = null)
        => Plan.Messages.Add(new PlanMessage { Severity = PlanSeverity.Error, Message = message, ItemId = itemId });

    public void Manual(string message)
    {
        if (!Plan.ManualTasks.Contains(message)) Plan.ManualTasks.Add(message);
    }
}
