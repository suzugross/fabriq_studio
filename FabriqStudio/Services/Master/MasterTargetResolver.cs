using System.IO;

namespace FabriqStudio.Services.Master;

public sealed class MasterTargetResolver : IMasterTargetResolver
{
    private static readonly string[] Tiers = ["standard", "extended"];

    private readonly IWorkspaceService _workspace;

    public MasterTargetResolver(IWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public string RootPath => _workspace.RootPath
        ?? throw new InvalidOperationException(
            "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    public string ProfilesDir => Path.Combine(RootPath, "profiles");

    public string? FindModuleDir(string moduleDir)
    {
        foreach (var tier in Tiers)
        {
            var path = Path.Combine(RootPath, "modules", tier, moduleDir);
            if (Directory.Exists(path)) return path;
        }
        return null;
    }

    public string? FindModuleKind(string moduleDir)
    {
        foreach (var tier in Tiers)
        {
            if (Directory.Exists(Path.Combine(RootPath, "modules", tier, moduleDir)))
                return tier;
        }
        return null;
    }

    public string? GetModuleCsvPath(string moduleDir, string csvName)
    {
        var dir = FindModuleDir(moduleDir);
        return dir is null ? null : Path.Combine(dir, csvName);
    }

    public string GetProfilePath(string profileName)
        => Path.Combine(ProfilesDir, profileName + ".csv");

    public string ToRelative(string absolutePath)
        => Path.GetRelativePath(RootPath, absolutePath).Replace('\\', '/');
}
