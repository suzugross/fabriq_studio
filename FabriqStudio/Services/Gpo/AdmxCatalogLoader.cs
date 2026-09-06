using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FabriqStudio.Models.Gpo;

namespace FabriqStudio.Services.Gpo;

/// <summary>
/// PolicyDefinitions フォルダー（*.admx + &lt;lang&gt;\*.adml）を読み、<see cref="GpoCatalog"/> を組み立てる。
/// 名前空間に依存せず要素名（LocalName）で辿る。表示文字列は優先言語 → フォールバック言語 → ID の順で解決する。
/// </summary>
public static class AdmxCatalogLoader
{
    private static readonly Regex StringRef       = new(@"^\$\(string\.(.+)\)$",       RegexOptions.Compiled);
    private static readonly Regex PresentationRef = new(@"^\$\(presentation\.(.+)\)$", RegexOptions.Compiled);
    private static readonly Regex XmlDeclaration  = new(@"^\s*<\?xml[^>]*\?>",          RegexOptions.Compiled);

    private sealed class AdmxFile
    {
        public string   BaseName        = "";
        public XElement Root            = null!;
        public string   TargetNamespace = "";
        public bool     PreferredAdmlFound;
        public readonly Dictionary<string, string>   Prefixes        = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string>   Strings         = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string>   FallbackStrings = new(StringComparer.Ordinal);
        public readonly Dictionary<string, XElement> Presentations   = new(StringComparer.Ordinal);
    }

    /// <param name="sourcePath">PolicyDefinitions フォルダー。</param>
    /// <param name="languages">優先順の言語フォルダー名（例: ja-JP, en-US）。最後の要素をフォールバック（英語名）に使う。</param>
    public static GpoCatalog Load(string sourcePath, IReadOnlyList<string> languages)
    {
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"ADMX フォルダーが見つかりません: {sourcePath}");

        var errors = new List<string>();
        var files  = new List<AdmxFile>();
        var newest = DateTime.MinValue;

        foreach (var path in Directory.GetFiles(sourcePath, "*.admx", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var root = ParseXml(path);
                if (root.Name.LocalName != "policyDefinitions")
                {
                    errors.Add($"{Path.GetFileName(path)}: policyDefinitions ではありません");
                    continue;
                }

                var f  = new AdmxFile { BaseName = Path.GetFileNameWithoutExtension(path), Root = root };
                var ns = El(root, "policyNamespaces");
                var target = El(ns, "target");
                f.TargetNamespace = Attr(target, "namespace") ?? f.BaseName;
                var tp = Attr(target, "prefix");
                if (!string.IsNullOrEmpty(tp)) f.Prefixes[tp] = f.TargetNamespace;
                foreach (var u in Els(ns, "using"))
                {
                    var p = Attr(u, "prefix");
                    var n = Attr(u, "namespace");
                    if (p is not null && n is not null) f.Prefixes[p] = n;
                }

                LoadAdml(sourcePath, f, languages, errors);
                files.Add(f);

                var t = File.GetLastWriteTime(path);
                if (t > newest) newest = t;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        // ── カテゴリ ────────────────────────────────────────────────
        var categories = new Dictionary<string, GpoCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            foreach (var c in Els(El(f.Root, "categories"), "category"))
            {
                var name = Attr(c, "name");
                if (string.IsNullOrEmpty(name)) continue;
                var key       = f.TargetNamespace + ":" + name;
                var parentRef = Attr(El(c, "parentCategory"), "ref");
                categories[key] = new GpoCategory
                {
                    Key         = key,
                    Name        = name,
                    DisplayName = ResolveString(f, Attr(c, "displayName")),
                    ParentKey   = parentRef is null ? null : ResolveRef(f, parentRef),
                };
            }
        }
        foreach (var c in categories.Values) c.Path = BuildPath(c, categories);

        // ── supportedOn ─────────────────────────────────────────────
        var supported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            foreach (var d in Els(El(El(f.Root, "supportedOn"), "definitions"), "definition"))
            {
                var name = Attr(d, "name");
                if (string.IsNullOrEmpty(name)) continue;
                supported[f.TargetNamespace + ":" + name] = ResolveString(f, Attr(d, "displayName"));
            }
        }

        // ── ポリシー ────────────────────────────────────────────────
        var preferred = languages.Count > 0 ? languages[0] : "";
        var fallback  = languages.Count > 1 ? languages[^1] : preferred;
        var language  = files.Any(f => f.PreferredAdmlFound) ? preferred : fallback;

        var catalog = new GpoCatalog
        {
            SourcePath = sourcePath,
            Language   = language,
            AdmxCount  = files.Count,
            VersionTag = $"Windows build {Environment.OSVersion.Version.Build} / ADMX {files.Count} 本 / 最終更新 {(newest == DateTime.MinValue ? "-" : newest.ToString("yyyy-MM-dd"))} / {language}",
        };

        foreach (var f in files)
        {
            foreach (var p in Els(El(f.Root, "policies"), "policy"))
            {
                try
                {
                    var policy = ParsePolicy(f, p, categories, supported);
                    if (policy is not null) catalog.Policies.Add(policy);
                }
                catch (Exception ex)
                {
                    errors.Add($"{f.BaseName}:{Attr(p, "name")}: {ex.Message}");
                }
            }
        }

        catalog.Policies.Sort((a, b) =>
        {
            var c = string.Compare(a.CategoryPath, b.CategoryPath, StringComparison.CurrentCulture);
            return c != 0 ? c : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
        });
        catalog.Categories.AddRange(categories.Values.OrderBy(c => c.Path, StringComparer.CurrentCulture));
        catalog.Errors.AddRange(errors);
        return catalog;
    }

    // ═══════════════════════════════════════════════════════════════
    //  ADML
    // ═══════════════════════════════════════════════════════════════

    private static void LoadAdml(string sourcePath, AdmxFile f, IReadOnlyList<string> languages, List<string> errors)
    {
        for (var i = 0; i < languages.Count; i++)
        {
            var lang = languages[i];
            var path = Path.Combine(sourcePath, lang, f.BaseName + ".adml");
            if (!File.Exists(path)) continue;

            try
            {
                var root      = ParseXml(path);
                var resources = El(root, "resources");
                if (resources is null) continue;

                var isPreferred = i == 0;
                var isFallback  = languages.Count > 1 && i == languages.Count - 1;
                if (isPreferred) f.PreferredAdmlFound = true;

                foreach (var s in Els(El(resources, "stringTable"), "string"))
                {
                    var id = Attr(s, "id");
                    if (id is null) continue;
                    var text = s.Value.Trim();
                    f.Strings.TryAdd(id, text);
                    if (isFallback) f.FallbackStrings[id] = text;
                }

                foreach (var p in Els(El(resources, "presentationTable"), "presentation"))
                {
                    var id = Attr(p, "id");
                    if (id is null) continue;
                    f.Presentations.TryAdd(id, p);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{lang}/{f.BaseName}.adml: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ポリシー
    // ═══════════════════════════════════════════════════════════════

    private static GpoPolicy? ParsePolicy(
        AdmxFile f, XElement p,
        IReadOnlyDictionary<string, GpoCategory> categories,
        IReadOnlyDictionary<string, string> supported)
    {
        var name = Attr(p, "name");
        var key  = NormalizeKey(Attr(p, "key"));
        if (string.IsNullOrWhiteSpace(name) || key is null) return null;

        var cls = (Attr(p, "class") ?? GpoPolicyClass.Machine).Trim();
        cls = cls.Equals(GpoPolicyClass.User, StringComparison.OrdinalIgnoreCase) ? GpoPolicyClass.User
            : cls.Equals(GpoPolicyClass.Both, StringComparison.OrdinalIgnoreCase) ? GpoPolicyClass.Both
            : GpoPolicyClass.Machine;

        var policy = new GpoPolicy
        {
            Id            = f.BaseName + ":" + name,
            AdmxFile      = f.BaseName,
            Name          = name,
            Class         = cls,
            Key           = key,
            ValueName     = NullIfEmpty(Attr(p, "valueName")),
            DisplayName   = ResolveString(f, Attr(p, "displayName")),
            DisplayNameEn = ResolveFallbackString(f, Attr(p, "displayName")),
            ExplainText   = ResolveString(f, Attr(p, "explainText")),
            ExplainTextEn = ResolveFallbackString(f, Attr(p, "explainText")),
        };
        if (string.IsNullOrWhiteSpace(policy.DisplayName)) policy.DisplayName = name;

        var pc = Attr(El(p, "parentCategory"), "ref");
        if (pc is not null)
        {
            policy.CategoryKey = ResolveRef(f, pc);
            if (categories.TryGetValue(policy.CategoryKey, out var cat)) policy.CategoryPath = cat.Path;
        }

        var so = Attr(El(p, "supportedOn"), "ref");
        if (so is not null)
            policy.SupportedOn = supported.TryGetValue(ResolveRef(f, so), out var s) ? s : so;

        policy.EnabledValue  = ParseValue(El(p, "enabledValue"));
        policy.DisabledValue = ParseValue(El(p, "disabledValue"));
        policy.EnabledList.AddRange(ParseItems(El(p, "enabledList")));
        policy.DisabledList.AddRange(ParseItems(El(p, "disabledList")));

        var elements = El(p, "elements");
        if (elements is not null)
        {
            var idx = 0;
            foreach (var e in elements.Elements())
            {
                var el = ParseElement(f, e);
                if (el is null) continue;
                el.Order = 1000 + idx++;
                policy.Elements.Add(el);
            }
        }

        var pr = Attr(p, "presentation");
        if (pr is not null && PresentationRef.Match(pr) is { Success: true } m && f.Presentations.TryGetValue(m.Groups[1].Value, out var pres))
            ApplyPresentation(policy, pres);

        foreach (var el in policy.Elements)
            if (string.IsNullOrWhiteSpace(el.Label)) el.Label = el.Id;

        policy.BuildSearchIndex();
        return policy;
    }

    private static GpoElement? ParseElement(AdmxFile f, XElement e)
    {
        GpoElementType? type = e.Name.LocalName switch
        {
            "boolean"     => GpoElementType.Boolean,
            "decimal"     => GpoElementType.Decimal,
            "longDecimal" => GpoElementType.LongDecimal,
            "text"        => GpoElementType.Text,
            "enum"        => GpoElementType.Enum,
            "list"        => GpoElementType.List,
            "multiText"   => GpoElementType.MultiText,
            _             => null,
        };
        if (type is null) return null;

        var id = Attr(e, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var el = new GpoElement
        {
            Id        = id,
            Type      = type.Value,
            Key       = NormalizeKey(Attr(e, "key")),
            ValueName = NullIfEmpty(Attr(e, "valueName")),
            Required  = IsTrue(Attr(e, "required")),
            Control   = DefaultControl(type.Value),
        };

        switch (type.Value)
        {
            case GpoElementType.Boolean:
                el.TrueValue  = ParseValue(El(e, "trueValue"));
                el.FalseValue = ParseValue(El(e, "falseValue"));
                el.TrueList.AddRange(ParseItems(El(e, "trueList")));
                el.FalseList.AddRange(ParseItems(El(e, "falseList")));
                break;

            case GpoElementType.Decimal:
            case GpoElementType.LongDecimal:
                el.MinValue    = ParseULong(Attr(e, "minValue"));
                el.MaxValue    = ParseULong(Attr(e, "maxValue"));
                el.StoreAsText = IsTrue(Attr(e, "storeAsText"));
                break;

            case GpoElementType.Text:
                el.MaxLength  = ParseInt(Attr(e, "maxLength"));
                el.Expandable = IsTrue(Attr(e, "expandable"));
                break;

            case GpoElementType.Enum:
                foreach (var item in Els(e, "item"))
                {
                    var value = ParseValue(El(item, "value"));
                    if (value is null) continue;
                    var enumItem = new GpoEnumItem
                    {
                        DisplayName = ResolveString(f, Attr(item, "displayName")),
                        Value       = value,
                    };
                    enumItem.ValueList.AddRange(ParseItems(El(item, "valueList")));
                    el.Items.Add(enumItem);
                }
                break;

            case GpoElementType.List:
                el.ValuePrefix   = NullIfEmpty(Attr(e, "valuePrefix"));
                el.Additive      = IsTrue(Attr(e, "additive"));
                el.Expandable    = IsTrue(Attr(e, "expandable"));
                el.ExplicitValue = IsTrue(Attr(e, "explicitValue"));
                break;

            case GpoElementType.MultiText:
                el.MaxLength  = ParseInt(Attr(e, "maxLength"));
                el.MaxStrings = ParseInt(Attr(e, "maxStrings"));
                break;
        }
        return el;
    }

    private static void ApplyPresentation(GpoPolicy policy, XElement pres)
    {
        var byId = new Dictionary<string, GpoElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in policy.Elements) byId.TryAdd(el.Id, el);

        string? pendingNote = null;
        var order = 0;

        foreach (var c in pres.Elements())
        {
            var ln = c.Name.LocalName;
            if (ln == "text")
            {
                var t = c.Value.Trim();
                if (t.Length > 0) pendingNote = pendingNote is null ? t : pendingNote + "\n" + t;
                continue;
            }

            var refId = Attr(c, "refId");
            if (refId is null || !byId.TryGetValue(refId, out var el)) continue;

            el.Order = order++;
            el.Note  = pendingNote;
            pendingNote = null;

            switch (ln)
            {
                case "dropdownList":
                    el.Control     = GpoControlType.DropdownList;
                    el.Label       = CleanLabel(c.Value);
                    el.DefaultItem = ParseInt(Attr(c, "defaultItem"));
                    break;
                case "checkBox":
                    el.Control        = GpoControlType.CheckBox;
                    el.Label          = CleanLabel(c.Value);
                    el.DefaultChecked = IsTrue(Attr(c, "defaultChecked"));
                    break;
                case "decimalTextBox":
                    el.Control     = GpoControlType.DecimalTextBox;
                    el.Label       = CleanLabel(c.Value);
                    el.DefaultText = Attr(c, "defaultValue");
                    break;
                case "longDecimalTextBox":
                    el.Control     = GpoControlType.LongDecimalTextBox;
                    el.Label       = CleanLabel(c.Value);
                    el.DefaultText = Attr(c, "defaultValue");
                    break;
                case "textBox":
                    el.Control     = GpoControlType.TextBox;
                    el.Label       = CleanLabel(El(c, "label")?.Value);
                    el.DefaultText = El(c, "defaultValue")?.Value;
                    break;
                case "comboBox":
                    el.Control     = GpoControlType.ComboBox;
                    el.Label       = CleanLabel(El(c, "label")?.Value);
                    el.DefaultText = El(c, "default")?.Value;
                    foreach (var s in Els(c, "suggestion"))
                    {
                        var v = s.Value.Trim();
                        if (v.Length > 0) el.Suggestions.Add(v);
                    }
                    break;
                case "listBox":
                    el.Control = GpoControlType.ListBox;
                    el.Label   = CleanLabel(c.Value);
                    break;
                case "multiTextBox":
                    el.Control = GpoControlType.MultiTextBox;
                    el.Label   = CleanLabel(c.Value);
                    break;
            }
        }

        if (pendingNote is not null) policy.PresentationNotes.Add(pendingNote);
    }

    // ═══════════════════════════════════════════════════════════════
    //  値・項目
    // ═══════════════════════════════════════════════════════════════

    private static GpoValue? ParseValue(XElement? container)
    {
        var child = container?.Elements().FirstOrDefault();
        if (child is null) return null;
        return child.Name.LocalName switch
        {
            "decimal"     => new GpoValue(GpoValueKind.Decimal,     (Attr(child, "value") ?? "0").Trim()),
            "longDecimal" => new GpoValue(GpoValueKind.LongDecimal, (Attr(child, "value") ?? "0").Trim()),
            "string"      => new GpoValue(GpoValueKind.String,      child.Value),
            "delete"      => new GpoValue(GpoValueKind.Delete,      ""),
            _             => null,
        };
    }

    private static IEnumerable<GpoRegistryItem> ParseItems(XElement? list)
    {
        if (list is null) yield break;
        var defaultKey = NormalizeKey(Attr(list, "defaultKey"));
        foreach (var item in Els(list, "item"))
        {
            var valueName = NullIfEmpty(Attr(item, "valueName"));
            if (valueName is null) continue;
            var value = ParseValue(El(item, "value"));
            if (value is null) continue;
            yield return new GpoRegistryItem
            {
                Key       = NormalizeKey(Attr(item, "key")) ?? defaultKey,
                ValueName = valueName,
                Value     = value,
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  解決ヘルパ
    // ═══════════════════════════════════════════════════════════════

    private static string ResolveString(AdmxFile f, string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var m = StringRef.Match(raw);
        if (!m.Success) return raw;
        return f.Strings.TryGetValue(m.Groups[1].Value, out var s) ? s : m.Groups[1].Value;
    }

    private static string ResolveFallbackString(AdmxFile f, string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var m = StringRef.Match(raw);
        if (!m.Success) return "";
        return f.FallbackStrings.TryGetValue(m.Groups[1].Value, out var s) ? s : "";
    }

    /// <summary>prefix:name → namespace:name（prefix 無しは自ファイルの名前空間）。</summary>
    private static string ResolveRef(AdmxFile f, string r)
    {
        r = r.Trim();
        var i = r.IndexOf(':');
        if (i < 0) return f.TargetNamespace + ":" + r;
        var prefix = r[..i];
        var name   = r[(i + 1)..];
        var ns     = f.Prefixes.TryGetValue(prefix, out var n) ? n : prefix;
        return ns + ":" + name;
    }

    private static string BuildPath(GpoCategory c, IReadOnlyDictionary<string, GpoCategory> all)
    {
        var parts = new List<string> { c.DisplayName };
        var cur   = c;
        var guard = 0;
        while (cur.ParentKey is not null && guard++ < 16 && all.TryGetValue(cur.ParentKey, out var parent))
        {
            parts.Add(parent.DisplayName);
            cur = parent;
        }
        parts.Reverse();
        return string.Join(" > ", parts.Where(p => p.Length > 0));
    }

    private static XElement ParseXml(string path)
    {
        // encoding="unicode" 等の宣言は文字列にしてから捨てる（BOM で実エンコーディングを判定する）
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = XmlDeclaration.Replace(reader.ReadToEnd(), "", 1);
        return XDocument.Parse(text).Root ?? throw new InvalidDataException("空の XML です");
    }

    private static GpoControlType DefaultControl(GpoElementType t) => t switch
    {
        GpoElementType.Boolean     => GpoControlType.CheckBox,
        GpoElementType.Enum        => GpoControlType.DropdownList,
        GpoElementType.Decimal     => GpoControlType.DecimalTextBox,
        GpoElementType.LongDecimal => GpoControlType.LongDecimalTextBox,
        GpoElementType.List        => GpoControlType.ListBox,
        GpoElementType.MultiText   => GpoControlType.MultiTextBox,
        _                          => GpoControlType.TextBox,
    };

    private static string? CleanLabel(string? s)
    {
        if (s is null) return null;
        var t = s.Trim().TrimEnd(':', '：').Trim();
        return t.Length == 0 ? null : t;
    }

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var k = key.Trim().Replace('/', '\\').Trim('\\');
        return k.Length == 0 ? null : k;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool IsTrue(string? s)
        => s is not null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Trim() == "1");

    private static int?   ParseInt(string? s)   => int.TryParse(s?.Trim(), out var v) ? v : null;
    private static ulong? ParseULong(string? s) => ulong.TryParse(s?.Trim(), out var v) ? v : null;

    private static XElement? El(XElement? e, string name)
        => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);

    private static IEnumerable<XElement> Els(XElement? e, string name)
        => e is null ? [] : e.Elements().Where(x => x.Name.LocalName == name);

    private static string? Attr(XElement? e, string name) => e?.Attribute(name)?.Value;
}
