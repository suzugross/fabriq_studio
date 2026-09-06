using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// ドロップされた資材をモジュールのサブフォルダへ配置し、表に入れる行の補完案を返す。
/// コピーはドロップ時に即時に行う（既存の AppConfig 画面「インストーラーを追加」と同じ挙動）。
/// </summary>
public interface IMasterAssetService
{
    /// <param name="spec">ドロップ枠の定義（コピー先・拡張子・補完方法）。</param>
    /// <param name="paths">ドロップされたファイル／フォルダの絶対パス。</param>
    /// <param name="confirmOverwrite">同名が既にあるときの上書き確認（true = 上書き）。</param>
    Task<AssetDropResult> ImportAsync(MasterDropSpec spec, IReadOnlyList<string> paths, Func<string, bool> confirmOverwrite);
}
