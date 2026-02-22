using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// レジストリ設定テンプレートのコレクションを管理する。
/// <para>
/// ワークスペース非依存。永続化先: &lt;exe と同じフォルダ&gt;\registry_collection\catalog.json
/// </para>
/// </summary>
public interface IRegistryCollectionService
{
    /// <summary>現在読み込まれている全エントリ（順序保持）。</summary>
    IReadOnlyList<RegistryTemplateEntry> Entries { get; }

    /// <summary>
    /// 起動時の初期化。catalog.json が存在すればロードし、
    /// 存在しなければ空の状態で待機する。
    /// このメソッドは App.OnStartup で VM 構築前に呼び出すこと。
    /// </summary>
    Task EnsureInitializedAsync();

    /// <summary>catalog.json を再ロードして Entries を更新する。</summary>
    Task ReloadAsync();

    /// <summary>エントリを末尾に追加して catalog.json に保存する。</summary>
    Task AddAsync(RegistryTemplateEntry entry);

    /// <summary>
    /// 既存エントリを差し替えて catalog.json に保存する。
    /// <paramref name="entry"/>.Id で対象行を特定する。
    /// Id が見つからない場合は何もしない。
    /// </summary>
    Task UpdateAsync(RegistryTemplateEntry entry);

    /// <summary>指定 Id のエントリを削除して catalog.json に保存する。</summary>
    Task RemoveAsync(string id);

    /// <summary>
    /// 1 件のエントリを現在のワークスペースの reg_config CSV へエクスポートする。
    /// Hive に応じて reg_hklm_list.csv / reg_hkcu_list.csv に追記する。
    /// KeyPath + KeyName が既存行と重複する場合はスキップする。
    /// </summary>
    /// <param name="entry">エクスポートするエントリ。</param>
    /// <param name="workspaceRootPath">エクスポート先ワークスペースの絶対パス（IWorkspaceService.RootPath）。</param>
    Task<ExportResult> ExportToWorkspaceAsync(RegistryTemplateEntry entry, string workspaceRootPath);
}

/// <summary>ワークスペースへのエクスポート結果。</summary>
public sealed class ExportResult
{
    /// <summary>新規追加されたエントリ数（0 または 1）。</summary>
    public int Added { get; init; }

    /// <summary>重複によりスキップされたエントリ数（0 または 1）。</summary>
    public int Skipped { get; init; }

    /// <summary>エラーメッセージ。null = 正常終了。</summary>
    public string? Error { get; init; }
}
