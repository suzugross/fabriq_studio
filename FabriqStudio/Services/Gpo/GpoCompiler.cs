using System.Globalization;
using FabriqStudio.Models.Gpo;

namespace FabriqStudio.Services.Gpo;

/// <summary>
/// ポリシー + 状態 + 要素値 を gpo_list.csv の行（Registry.pol のエントリ）に展開する。
/// 規則は Policy Plus / gpedit と同じ:
///   有効   : enabledValue（無ければ DWORD 1）+ enabledList + 各要素の値
///   無効   : disabledValue（無ければ削除）+ disabledList + 各要素の削除（list は DeleteAllValues）
///   未構成 : 触る値すべてを Unmanage（Registry.pol から除去）
/// </summary>
public static class GpoCompiler
{
    public static GpoCompileResult Compile(GpoPolicy policy, GpoSelection selection)
    {
        var result = new GpoCompileResult();
        var state  = GpoStates.Normalize(selection.State);
        var scope  = policy.Class == GpoPolicyClass.Both
            ? (selection.Scope == GpoPolicyClass.User ? GpoPolicyClass.User : GpoPolicyClass.Machine)
            : policy.Class;

        var title     = $"{policy.DisplayName} = {GpoStates.Label(state)}";
        var policyRef = $"{policy.AdmxFile}:{policy.Name}={state}";
        var rows      = new List<GpoRow>();

        void Add(string key, string valueName, string action, string type, string value, string? suffix = null)
            => rows.Add(new GpoRow
            {
                Scope     = scope,
                KeyPath   = key,
                ValueName = valueName,
                Action    = action,
                Type      = type,
                Value     = value,
                Title     = suffix is null ? title : $"{title} {suffix}",
                PolicyRef = policyRef,
            });

        void AddValue(string key, string valueName, GpoValue v, string? suffix = null)
        {
            if (v.Kind == GpoValueKind.Delete) Add(key, valueName, GpoActions.Delete, "", "", suffix);
            else                               Add(key, valueName, GpoActions.Set, v.RegistryType, v.Data, suffix);
        }

        switch (state)
        {
            case GpoStates.Enabled:
                if (policy.EnabledValue is not null)
                {
                    if (policy.ValueName is null) result.Warnings.Add($"{policy.DisplayName}: enabledValue がありますが valueName が無いため書けません。");
                    else AddValue(policy.Key, policy.ValueName, policy.EnabledValue);
                }
                else if (policy.ValueName is not null)
                {
                    Add(policy.Key, policy.ValueName, GpoActions.Set, "REG_DWORD", "1");
                }
                foreach (var item in policy.EnabledList)
                    AddValue(item.Key ?? policy.Key, item.ValueName, item.Value);
                foreach (var e in policy.Elements)
                    EmitElementEnabled(policy, e, selection, result, Add, AddValue);
                break;

            case GpoStates.Disabled:
                if (policy.DisabledValue is not null)
                {
                    if (policy.ValueName is null) result.Warnings.Add($"{policy.DisplayName}: disabledValue がありますが valueName が無いため書けません。");
                    else AddValue(policy.Key, policy.ValueName, policy.DisabledValue);
                }
                else if (policy.ValueName is not null)
                {
                    Add(policy.Key, policy.ValueName, GpoActions.Delete, "", "");
                }
                foreach (var item in policy.DisabledList)
                    AddValue(item.Key ?? policy.Key, item.ValueName, item.Value);
                foreach (var e in policy.Elements)
                    EmitElementDisabled(policy, e, Add);
                break;

            default:
                EmitUnmanage(policy, result, Add);
                break;
        }

        // 同じエントリ（Scope + KeyPath + ValueName）は後勝ちで 1 行にまとめる（位置は最初の出現）
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (seen.TryGetValue(r.DedupeKey, out var idx)) result.Rows[idx] = r;
            else
            {
                seen[r.DedupeKey] = result.Rows.Count;
                result.Rows.Add(r);
            }
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  要素（有効）
    // ═══════════════════════════════════════════════════════════════

    private static void EmitElementEnabled(
        GpoPolicy policy, GpoElement e, GpoSelection sel, GpoCompileResult result,
        Action<string, string, string, string, string, string?> add,
        Action<string, string, GpoValue, string?> addValue)
    {
        var key    = e.Key ?? policy.Key;
        var label  = e.DisplayLabel;
        var suffix = $"({label})";
        var raw = sel.GetElementValue(e) ?? "";

        switch (e.Type)
        {
            case GpoElementType.Boolean:
            {
                var isChecked = raw.Trim() == "1";
                var wrote = false;
                if (isChecked)
                {
                    if (e.TrueValue is not null && e.ValueName is not null) { addValue(key, e.ValueName, e.TrueValue, suffix); wrote = true; }
                    foreach (var item in e.TrueList) { addValue(item.Key ?? key, item.ValueName, item.Value, suffix); wrote = true; }
                    if (!wrote && e.ValueName is not null) add(key, e.ValueName, GpoActions.Set, "REG_DWORD", "1", suffix);
                }
                else
                {
                    if (e.FalseValue is not null && e.ValueName is not null) { addValue(key, e.ValueName, e.FalseValue, suffix); wrote = true; }
                    foreach (var item in e.FalseList) { addValue(item.Key ?? key, item.ValueName, item.Value, suffix); wrote = true; }
                    if (!wrote && e.ValueName is not null) add(key, e.ValueName, GpoActions.Delete, "", "", suffix);
                }
                break;
            }

            case GpoElementType.Decimal:
            case GpoElementType.LongDecimal:
            {
                var text = raw.Trim();
                if (text.Length == 0)
                {
                    if (e.Required) result.Errors.Add($"「{label}」は必須です。");
                    break;
                }
                if (!TryParseNumber(text, out var n))
                {
                    result.Errors.Add($"「{label}」は整数で入力してください（{text}）。");
                    break;
                }
                if (e.Type == GpoElementType.Decimal && n > uint.MaxValue)
                {
                    result.Errors.Add($"「{label}」は {uint.MaxValue} 以下で入力してください。");
                    break;
                }
                if (e.MinValue is { } min && n < min) { result.Errors.Add($"「{label}」は {min} 以上で入力してください。"); break; }
                if (e.MaxValue is { } max && n > max) { result.Errors.Add($"「{label}」は {max} 以下で入力してください。"); break; }
                if (e.ValueName is null) { result.Warnings.Add($"「{label}」に valueName が無いため書けません。"); break; }

                var type = e.StoreAsText ? "REG_SZ" : (e.Type == GpoElementType.Decimal ? "REG_DWORD" : "REG_QWORD");
                add(key, e.ValueName, GpoActions.Set, type, n.ToString(CultureInfo.InvariantCulture), suffix);
                break;
            }

            case GpoElementType.Text:
            {
                if (raw.Length == 0)
                {
                    if (e.Required) result.Errors.Add($"「{label}」は必須です。");
                    break;
                }
                if (e.MaxLength is { } maxLen && raw.Length > maxLen)
                    result.Warnings.Add($"「{label}」が最大長 {maxLen} を超えています（{raw.Length} 文字）。");
                if (e.ValueName is null) { result.Warnings.Add($"「{label}」に valueName が無いため書けません。"); break; }
                add(key, e.ValueName, GpoActions.Set, e.Expandable ? "REG_EXPAND_SZ" : "REG_SZ", raw, suffix);
                break;
            }

            case GpoElementType.Enum:
            {
                if (e.Items.Count == 0) break;
                var item = e.Items.FirstOrDefault(i => i.Value.ToString() == raw.Trim());
                if (item is null)
                {
                    if (raw.Trim().Length > 0)
                        result.Warnings.Add($"「{label}」の値 {raw} は選択肢に無いため既定の項目を使います。");
                    var idx = e.DefaultItem is { } d && d >= 0 && d < e.Items.Count ? d : 0;
                    item = e.Items[idx];
                }
                if (e.ValueName is not null) addValue(key, e.ValueName, item.Value, suffix);
                foreach (var vl in item.ValueList)
                    addValue(vl.Key ?? key, vl.ValueName, vl.Value, suffix);
                break;
            }

            case GpoElementType.MultiText:
            {
                var lines = SplitLines(raw);
                if (lines.Count == 0)
                {
                    if (e.Required) result.Errors.Add($"「{label}」は必須です。");
                    break;
                }
                if (lines.Any(l => l.Contains('|')))
                    result.Warnings.Add($"「{label}」に「|」を含む行があります（REG_MULTI_SZ の区切り文字と衝突します）。");
                if (e.MaxStrings is { } maxStr && lines.Count > maxStr)
                    result.Warnings.Add($"「{label}」の行数が上限 {maxStr} を超えています。");
                if (e.ValueName is null) { result.Warnings.Add($"「{label}」に valueName が無いため書けません。"); break; }
                add(key, e.ValueName, GpoActions.Set, "REG_MULTI_SZ", string.Join("|", lines), suffix);
                break;
            }

            case GpoElementType.List:
            {
                var lines = SplitLines(raw);
                if (!e.Additive)
                    add(key, "", GpoActions.DeleteAllValues, "", "", "(一覧クリア)");
                if (lines.Count == 0)
                {
                    if (e.Required) result.Errors.Add($"「{label}」は必須です。");
                    break;
                }
                var type = e.Expandable ? "REG_EXPAND_SZ" : "REG_SZ";
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    string name, value;
                    if (e.ExplicitValue)
                    {
                        var eq = line.IndexOf('=');
                        if (eq <= 0)
                        {
                            result.Errors.Add($"「{label}」は「名前=値」の形式で 1 行ずつ入力してください（{line}）。");
                            continue;
                        }
                        name  = line[..eq].Trim();
                        value = line[(eq + 1)..].Trim();
                    }
                    else if (e.ValuePrefix is not null)
                    {
                        name  = $"{e.ValuePrefix}{i + 1}";
                        value = line;
                    }
                    else
                    {
                        name  = line;
                        value = line;
                    }
                    add(key, name, GpoActions.Set, type, value, suffix);
                }
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  要素（無効）: Policy Plus と同じく要素の値を削除する
    // ═══════════════════════════════════════════════════════════════

    private static void EmitElementDisabled(
        GpoPolicy policy, GpoElement e,
        Action<string, string, string, string, string, string?> add)
    {
        var key = e.Key ?? policy.Key;
        if (e.Type == GpoElementType.List)
        {
            add(key, "", GpoActions.DeleteAllValues, "", "", "(一覧クリア)");
            return;
        }
        if (e.ValueName is not null)
            add(key, e.ValueName, GpoActions.Delete, "", "", "(要素クリア)");

        // valueName を持たず一覧だけで書く boolean は、その一覧の値を消す
        if (e.Type == GpoElementType.Boolean && e.ValueName is null)
            foreach (var item in e.TrueList.Concat(e.FalseList))
                add(item.Key ?? key, item.ValueName, GpoActions.Delete, "", "", "(要素クリア)");
    }

    // ═══════════════════════════════════════════════════════════════
    //  未構成: 触る値をすべて Registry.pol から外す
    // ═══════════════════════════════════════════════════════════════

    private static void EmitUnmanage(
        GpoPolicy policy, GpoCompileResult result,
        Action<string, string, string, string, string, string?> add)
    {
        void Un(string key, string valueName) => add(key, valueName, GpoActions.Unmanage, "", "", null);

        if (policy.ValueName is not null) Un(policy.Key, policy.ValueName);
        foreach (var item in policy.EnabledList.Concat(policy.DisabledList))
            Un(item.Key ?? policy.Key, item.ValueName);

        foreach (var e in policy.Elements)
        {
            var key = e.Key ?? policy.Key;
            switch (e.Type)
            {
                case GpoElementType.List:
                    result.Warnings.Add($"「{e.DisplayLabel}」は一覧要素のため、未構成に戻す際に値名を列挙できません。実機の Registry.pol を確認してください。");
                    break;
                case GpoElementType.Boolean:
                    if (e.ValueName is not null) Un(key, e.ValueName);
                    foreach (var item in e.TrueList.Concat(e.FalseList)) Un(item.Key ?? key, item.ValueName);
                    break;
                case GpoElementType.Enum:
                    if (e.ValueName is not null) Un(key, e.ValueName);
                    foreach (var item in e.Items)
                        foreach (var vl in item.ValueList) Un(vl.Key ?? key, vl.ValueName);
                    break;
                default:
                    if (e.ValueName is not null) Un(key, e.ValueName);
                    break;
            }
        }

        if (policy.ValueName is null && policy.Elements.Count == 0 && policy.EnabledList.Count == 0)
            result.Warnings.Add($"{policy.DisplayName}: 未構成にするために外す値がありません。");
    }

    // ═══════════════════════════════════════════════════════════════
    //  ヘルパ
    // ═══════════════════════════════════════════════════════════════

    private static List<string> SplitLines(string raw)
        => raw.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

    private static bool TryParseNumber(string text, out ulong value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
