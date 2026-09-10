namespace FabriqStudio.Services.Collect;

/// <summary>winget でインストールできるパッケージの検索（この PC の winget を使う。ネット接続が要る）。</summary>
public interface IWingetSearchService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<WingetPackage>> SearchAsync(string query, CancellationToken ct = default);
}
