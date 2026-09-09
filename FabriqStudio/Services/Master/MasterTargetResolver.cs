using System.IO;

namespace FabriqStudio.Services.Master;

public sealed class MasterTargetResolver : IMasterTargetResolver
{
    /// <summary>
    /// Sysprep プロファイルだけが使うモジュール。資材・テキストファイルは <c>&lt;名&gt;_sysprep</c> のデータフォルダへ。
    /// （taskbar_config は Sysprep 章の有無でどちらにも付くので含めない。設定 CSV の書き先は組み立て時に決まる）
    /// </summary>
    private static readonly HashSet<string> SysprepOnlyModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "storeapp_config", "default_app_config", "sysprep_config", "generic_process_runner", "history_destroyer",
    };

    private readonly IWorkspaceService   _workspace;
    private readonly IModuleDataResolver _data;

    public MasterTargetResolver(IWorkspaceService workspace, IModuleDataResolver data)
    {
        _workspace = workspace;
        _data      = data;
    }

    public string RootPath => _workspace.RootPath
        ?? throw new InvalidOperationException(
            "ワークスペースが開かれていません。fabriq フォルダを選択してください。");

    public string ProfilesDir => Path.GetFullPath(Path.Combine(RootPath, "profiles"));

    public string? FindModuleDir(string moduleDir)
        => _data.ModuleRelPath(moduleDir) is { } rel ? _data.ResolveRead(rel, null).AbsPath : null;

    public string? FindModuleKind(string moduleDir)
        => _data.ModuleRelPath(moduleDir)?.Split('/')[1];

    public string? GetModuleCsvPath(string moduleDir, string csvName)
    {
        var dir = FindModuleDir(moduleDir);
        return dir is null ? null : Path.Combine(dir, csvName);
    }

    public string GetProfilePath(string profileName)
        => Path.Combine(ProfilesDir, profileName + ".csv");

    public string ToRelative(string absolutePath)
        => Path.GetRelativePath(RootPath, absolutePath).Replace('\\', '/');

    // ── データフォルダ（PDF）──────────────────────────────────────

    public string SysprepDataSet(string masterName) => masterName + "_sysprep";

    public string DataSetFor(string masterName, string moduleDir)
    {
        var name = Path.GetFileName((moduleDir ?? "").Trim().TrimEnd('/', '\\'));
        return SysprepOnlyModules.Contains(name) ? SysprepDataSet(masterName) : masterName;
    }

    public ResolvedModulePath? ResolveRead(string moduleDir, string rel, string? dataSet)
    {
        var kind = FindModuleKind(moduleDir);
        return kind is null ? null : _data.ResolveRead(_data.ModuleRelPath(kind, moduleDir, rel), dataSet);
    }

    public ResolvedModulePath? ResolveWrite(string moduleDir, string rel, string dataSet)
    {
        var kind = FindModuleKind(moduleDir);
        return kind is null ? null : _data.ResolveWrite(_data.ModuleRelPath(kind, moduleDir, rel), dataSet);
    }

    public IReadOnlyList<string> ListDataSets()
    {
        var dir = ProfilesDir;
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName).OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> ListDataSetModules(string dataSet) => _data.ListDataSetModules(dataSet);

    public string DataSetModuleDir(string dataSet, string moduleDir) => _data.DataSetModuleDir(dataSet, moduleDir);

    public bool IsFrameworkAsset(string fileName) => _data.IsFrameworkAsset(fileName);
}
