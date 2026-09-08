using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace FabriqStudio.Models.Master;

/// <summary>既定のアプリ関連付け XML（Dism /Export-DefaultAppAssociations の形式）の 1 行。</summary>
public sealed class AppAssocEntry
{
    /// <summary>拡張子（.pdf）またはプロトコル（http / mailto）。</summary>
    public string Identifier      { get; set; } = "";
    public string ProgId          { get; set; } = "";
    public string ApplicationName { get; set; } = "";
}

/// <summary>
/// DefaultAssociations XML の読み書き。書式は Dism のエクスポートと同じ
/// （UTF-8、2 スペース字下げ、&lt;Association Identifier ProgId ApplicationName /&gt;）。
/// </summary>
public sealed class AppAssocDocument
{
    public List<AppAssocEntry> Entries { get; } = [];

    public static AppAssocDocument Parse(string xml)
    {
        var doc  = new AppAssocDocument();
        var root = XDocument.Parse(xml).Root
                   ?? throw new InvalidDataException("XML が空です。");
        if (root.Name.LocalName != "DefaultAssociations")
            throw new InvalidDataException($"DefaultAssociations の XML ではありません（ルート: {root.Name.LocalName}）。");

        foreach (var e in root.Elements().Where(e => e.Name.LocalName == "Association"))
        {
            var id = e.Attribute("Identifier")?.Value?.Trim() ?? "";
            if (id.Length == 0) continue;
            doc.Entries.Add(new AppAssocEntry
            {
                Identifier      = id,
                ProgId          = e.Attribute("ProgId")?.Value?.Trim() ?? "",
                ApplicationName = e.Attribute("ApplicationName")?.Value?.Trim() ?? "",
            });
        }
        return doc;
    }

    public static AppAssocDocument Load(string path) => Parse(File.ReadAllText(path));

    public string ToXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<DefaultAssociations>\r\n");
        foreach (var e in Entries)
        {
            sb.Append("  <Association Identifier=\"").Append(Esc(e.Identifier))
              .Append("\" ProgId=\"").Append(Esc(e.ProgId))
              .Append("\" ApplicationName=\"").Append(Esc(e.ApplicationName))
              .Append("\" />\r\n");
        }
        sb.Append("</DefaultAssociations>\r\n");
        return sb.ToString();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToXml(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>既知のデスクトップ アプリの ProgId 辞書（master_template/appassoc_apps.json）。</summary>
public sealed class AppAssocDictionary
{
    [JsonPropertyName("version")]    public int Version { get; set; } = 1;
    [JsonPropertyName("apps")]       public List<AppAssocApp>      Apps       { get; set; } = [];
    [JsonPropertyName("categories")] public List<AppAssocCategory> Categories { get; set; } = [];
}

public sealed class AppAssocApp
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>識別子（拡張子・プロトコル）→ ProgId。</summary>
    [JsonPropertyName("progIds")] public Dictionary<string, string> ProgIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AppAssocCategory
{
    [JsonPropertyName("id")]          public string       Id          { get; set; } = "";
    [JsonPropertyName("label")]       public string       Label       { get; set; } = "";
    [JsonPropertyName("identifiers")] public List<string> Identifiers { get; set; } = [];
}

/// <summary>ある識別子に対して選べるアプリの候補（辞書 / XML 内 / この PC の登録情報）。</summary>
public sealed class AppAssocCandidate
{
    public string AppName { get; init; } = "";
    public string ProgId  { get; init; } = "";
    /// <summary>辞書 / XML / この PC</summary>
    public string Source  { get; init; } = "";

    public string Detail => $"{ProgId}  [{Source}]";
    public override string ToString() => AppName;
}
