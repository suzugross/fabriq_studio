namespace FabriqStudio.Services.Collect;

/// <summary>この PC にインストールされているストアアプリのうち、削除できるものの一覧。</summary>
public interface IStoreAppInventoryService
{
    Task<IReadOnlyList<StoreApp>> ListAsync(CancellationToken ct = default);
}
