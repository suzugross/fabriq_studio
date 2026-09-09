using System.IO;
using FabriqStudio.Services;

namespace FabriqStudio.Tests;

/// <summary>固定ルートを返すだけの IWorkspaceService。</summary>
internal sealed class StubWorkspace : IWorkspaceService
{
    public StubWorkspace(string root) => RootPath = root;

    public string? RootPath { get; }
    public bool    IsOpen   => RootPath is not null;

    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged { add { } remove { } }

    public string? Validate(string path) => null;
    public void Open(string path) { }
    public void Close() { }
    public void Reload() { }
    public void TryRestorePersisted() { }
    public Task<string?> CreateFromTemplateAsync(string targetPath) => Task.FromResult<string?>(null);
}

/// <summary>テスト用の使い捨てワークスペース（%TEMP% 配下）。</summary>
internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fabriq_studio_tests", Guid.NewGuid().ToString("N"));

    public TempWorkspace() => Directory.CreateDirectory(Root);

    /// <summary>ルート相対パスにファイルを作る（親フォルダも作る）。</summary>
    public string File(string rel, string content = "Enabled,Description\r\n1,sample\r\n")
    {
        var p = Abs(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    /// <summary>ルート相対パスにフォルダを作る。</summary>
    public string Dir(string rel)
    {
        var p = Abs(rel);
        Directory.CreateDirectory(p);
        return p;
    }

    public string Abs(string rel) => Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));

    public ModuleDataResolver Resolver() => new(new StubWorkspace(Root));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* 後始末の失敗は無視 */ }
    }
}
