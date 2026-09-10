using System.Text.Json;

namespace FabriqStudio.Services.Collect;

/// <summary>
/// Get-AppxPackage（現在ユーザー、昇格不要）から、フレームワーク・削除不可・言語リソースを除いた一覧を JSON で受け取る。
/// fabriq の storeapp_config は AppName（パッケージ名）で Remove-AppxPackage / Remove-AppxProvisionedPackage する。
/// </summary>
public sealed class StoreAppInventoryService : IStoreAppInventoryService
{
    private const string Script =
        "Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and -not $_.IsResourcePackage } " +
        "| Select-Object Name, Publisher, Version | Sort-Object Name | ConvertTo-Json -Compress\r\n";

    private readonly IPowerShellRunner _ps;

    public StoreAppInventoryService(IPowerShellRunner ps)
    {
        _ps = ps;
    }

    public async Task<IReadOnlyList<StoreApp>> ListAsync(CancellationToken ct = default)
    {
        var r = await _ps.RunAsync(Script, ct);
        if (!r.Succeeded)
            throw new InvalidOperationException(FirstLine(r.StdErr) ?? "ストアアプリの一覧を取得できませんでした");
        return Parse(r.StdOut);
    }

    /// <summary>ConvertTo-Json の出力（1 件だとオブジェクト、複数だと配列）を解析する。</summary>
    public static IReadOnlyList<StoreApp> Parse(string json)
    {
        var text = (json ?? "").Trim();
        if (text.Length == 0) return [];

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];

        var result = new List<StoreApp>();
        foreach (var e in items)
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var name = Str(e, "Name");
            if (name.Length == 0) continue;
            result.Add(new StoreApp(name, PublisherName(Str(e, "Publisher")), Str(e, "Version")));
        }
        return result;
    }

    private static string Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>"CN=Microsoft Corporation, O=..." → "Microsoft Corporation"。</summary>
    internal static string PublisherName(string dn)
    {
        foreach (var part in dn.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return p[3..].Trim();
        }
        return dn.Trim();
    }

    private static string? FirstLine(string s)
    {
        var line = (s ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return line;
    }
}
