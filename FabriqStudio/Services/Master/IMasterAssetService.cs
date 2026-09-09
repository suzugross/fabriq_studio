using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// ドロップされた資材をマスタのデータフォルダ（profiles/&lt;名&gt;/modules/&lt;module&gt;/&lt;subDir&gt;/、
/// Sysprep 側のモジュールは profiles/&lt;名&gt;_sysprep/…）へ配置し、表に入れる行の補完案を返す。
/// コピーはドロップ時に即時に行う（既存の AppConfig 画面「インストーラーを追加」と同じ挙動）。
/// 資材フォルダはフォルダ単位 all-or-nothing なので、データフォルダに初めて作るときは確認する。
/// </summary>
public interface IMasterAssetService
{
    /// <param name="spec">ドロップ枠の定義（コピー先・拡張子・補完方法）。</param>
    /// <param name="paths">ドロップされたファイル／フォルダの絶対パス。</param>
    /// <param name="masterName">マスタ名（データフォルダの決定に使う）。</param>
    /// <param name="confirmOverwrite">同名が既にあるときの上書き確認（true = 上書き）。</param>
    /// <param name="confirmCreateFolder">
    /// データフォルダに資材フォルダを初めて作るときの確認（引数は説明文。true = 作成）。
    /// null なら確認せずに作る。本体側の同名フォルダに何も無ければ確認しない。
    /// </param>
    Task<AssetDropResult> ImportAsync(
        MasterDropSpec spec, IReadOnlyList<string> paths, string masterName,
        Func<string, bool> confirmOverwrite, Func<string, bool>? confirmCreateFolder = null);
}
