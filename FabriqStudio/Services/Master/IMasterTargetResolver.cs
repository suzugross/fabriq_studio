namespace FabriqStudio.Services.Master;

/// <summary>
/// 生成物の書き込み先パスを解決する唯一の場所。
/// 案件データ（モジュールの設定 CSV・資材・テキストファイル）は fabriq のプロファイル別データフォルダ
/// （PDF: <c>profiles/&lt;名&gt;/modules/&lt;module&gt;/…</c>）に書く（Profile-First）。
/// マスタ本体プロファイル（<c>&lt;名&gt;.csv</c>）の分は <c>profiles/&lt;名&gt;/</c>、
/// Sysprep プロファイル（<c>&lt;名&gt;_sysprep.csv</c>）の分は <c>profiles/&lt;名&gt;_sysprep/</c>。
/// フレームワーク資産（スクリプト・module.csv・tools/）は本体 <c>modules/&lt;tier&gt;/&lt;module&gt;/</c> を参照する。
/// 写像・粒度の規則は <see cref="IModuleDataResolver"/> に従う。
/// </summary>
public interface IMasterTargetResolver
{
    /// <summary>ワークスペースルート。未オープン時は例外。</summary>
    string RootPath { get; }

    /// <summary>profiles/ の絶対パス。</summary>
    string ProfilesDir { get; }

    /// <summary>本体側のモジュールディレクトリの絶対パス（standard → extended の順に探索）。無ければ null。</summary>
    string? FindModuleDir(string moduleDir);

    /// <summary>モジュール種別（"standard" / "extended"）。無ければ null。</summary>
    string? FindModuleKind(string moduleDir);

    /// <summary>本体側の設定 CSV の絶対パス（列の基準・取り込み元。存在しなくてもパスは返す。モジュール自体が無ければ null）。</summary>
    string? GetModuleCsvPath(string moduleDir, string csvName);

    /// <summary>プロファイル CSV の絶対パス。</summary>
    string GetProfilePath(string profileName);

    /// <summary>ルートからの相対パス（表示・ICsvService 用、区切りは '/'）。</summary>
    string ToRelative(string absolutePath);

    // ── データフォルダ（PDF）──────────────────────────────────────

    /// <summary>Sysprep プロファイル名（= そのデータフォルダ名）。</summary>
    string SysprepDataSet(string masterName);

    /// <summary>
    /// このモジュールの案件データ（資材・テキストファイル）を置くデータフォルダ。
    /// Sysprep プロファイルだけが使うモジュールは <c>&lt;名&gt;_sysprep</c>、それ以外は <c>&lt;名&gt;</c>。
    /// （設定 CSV の書き先は、そのモジュールの行を持つプロファイルから組み立て時に決める）
    /// </summary>
    string DataSetFor(string masterName, string moduleDir);

    /// <summary>読み解決（データフォルダにあればそちら、無ければ本体）。dataSet が null なら本体。モジュールが無ければ null。</summary>
    ResolvedModulePath? ResolveRead(string moduleDir, string rel, string? dataSet);

    /// <summary>書き解決（データフォルダ側。ディレクトリは作らない）。モジュールが無ければ null。</summary>
    ResolvedModulePath? ResolveWrite(string moduleDir, string rel, string dataSet);

    /// <summary>profiles/ 直下にあるデータフォルダ名（フォルダがあるもの）。</summary>
    IReadOnlyList<string> ListDataSets();

    /// <summary>データフォルダ内にモジュールフォルダがあるモジュール名。</summary>
    IReadOnlyList<string> ListDataSetModules(string dataSet);

    /// <summary>データフォルダ側のモジュールフォルダの絶対パス（存在しなくても返す）。</summary>
    string DataSetModuleDir(string dataSet, string moduleDir);

    /// <summary>フレームワーク資産（PDF に置かれても無視されるもの）か。</summary>
    bool IsFrameworkAsset(string fileName);
}
