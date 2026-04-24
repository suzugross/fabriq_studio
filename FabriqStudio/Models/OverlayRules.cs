using System.Text.Json.Serialization;

namespace FabriqStudio.Models;

/// <summary>
/// <c>dev/framework_overlay_rules.json</c>（fabriq 公開契約 § 9）の C# 表現。
/// schemaVersion は現行 1。将来 2 以上になった場合、外部ツールは処理拒否して明示エラーとする（§ 9.8）。
/// </summary>
public class OverlayRules
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("excludeDirsTopLevel")]
    public List<string> ExcludeDirsTopLevel { get; set; } = new();

    [JsonPropertyName("excludeDirsRecursive")]
    public List<string> ExcludeDirsRecursive { get; set; } = new();

    [JsonPropertyName("excludeFilesKernelLevel")]
    public List<string> ExcludeFilesKernelLevel { get; set; } = new();

    [JsonPropertyName("moduleCsvWhitelist")]
    public List<string> ModuleCsvWhitelist { get; set; } = new();

    [JsonPropertyName("bundles")]
    public BundleDefs Bundles { get; set; } = new();

    public class BundleDefs
    {
        [JsonPropertyName("kernel")]
        public KernelBundleDef Kernel { get; set; } = new();

        [JsonPropertyName("module")]
        public ModuleBundleDef Module { get; set; } = new();
    }

    public class KernelBundleDef
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("versionFile")]
        public string VersionFile { get; set; } = "";

        [JsonPropertyName("includePaths")]
        public List<string> IncludePaths { get; set; } = new();
    }

    public class ModuleBundleDef
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("pathPattern")]
        public string PathPattern { get; set; } = "";

        [JsonPropertyName("versionFilePattern")]
        public string VersionFilePattern { get; set; } = "";

        [JsonPropertyName("requiresKernelFilePattern")]
        public string RequiresKernelFilePattern { get; set; } = "";

        [JsonPropertyName("typeValues")]
        public List<string> TypeValues { get; set; } = new();
    }
}
