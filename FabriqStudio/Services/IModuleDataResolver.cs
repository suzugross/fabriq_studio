namespace FabriqStudio.Services;

/// <summary>モジュールデータ（設定 CSV・資材フォルダ）の解決結果の採用元。</summary>
public enum ModuleDataSource
{
    /// <summary>データセット未選択。本体側をそのまま使う（fabriq の恒等写像 = 従来動作）。</summary>
    Identity,

    /// <summary>PDF（profiles/&lt;名&gt;/modules/…）側を採用。</summary>
    Profile,

    /// <summary>
    /// データセットは選ばれているが PDF に無いため本体側へ落ちた。
    /// fabriq はこの状態を実行時に必ず Show-Warning で表示する（無言経路は無い）。
    /// </summary>
    Fallback,
}

/// <summary>解決した 1 パス。</summary>
/// <param name="AbsPath">絶対パス（Fallback / Identity は本体側、Profile は PDF 側。存在しないこともある）。</param>
/// <param name="RelPath">ワークスペースルートからの相対パス（区切りは '/'。表示用）。</param>
/// <param name="Source">採用元。</param>
/// <param name="DataSet">解決に使ったデータセット名（null = 未選択）。</param>
public sealed record ResolvedModulePath(string AbsPath, string RelPath, ModuleDataSource Source, string? DataSet)
{
    public bool IsProfile => Source == ModuleDataSource.Profile;
}

/// <summary>ある PDF がモジュールを上書きしている内容（Advanced のモジュール編集画面で注意喚起に使う）。</summary>
/// <param name="DataSet">プロファイル名（profiles/&lt;名&gt;/）。</param>
/// <param name="Csvs">PDF 側にある設定 CSV 名（フレームワーク資産は除く）。</param>
/// <param name="Folders">PDF 側にある資材フォルダ名（存在するだけで本体側の同名フォルダは使われなくなる）。</param>
public sealed record ModuleOverride(string DataSet, IReadOnlyList<string> Csvs, IReadOnlyList<string> Folders);

/// <summary>合成一覧の 1 要素（CSV 名・採用元・絶対パス）。</summary>
public sealed record ModuleDataEntry(string Name, ModuleDataSource Source, string AbsPath);

/// <summary>
/// fabriq の「プロファイル別データオーバーレイ（PDF）」の解決規則を Studio 側でミラーする唯一の場所。
/// 契約は E:\fabriq\dev\PROFILE_DATA_OVERLAY_FOR_TOOLING.md（§2 写像 / §3 粒度 / §6 書き込み）。
///
/// <list type="bullet">
///   <item>写像: <c>modules\&lt;tier&gt;\&lt;module&gt;\&lt;rel&gt;</c> → <c>profiles\&lt;名&gt;\modules\&lt;module&gt;\&lt;rel&gt;</c>（tier は落ちる）。<c>modules\</c> 配下以外は恒等。</item>
///   <item>粒度: 設定 CSV = ファイル単位、資材フォルダ = フォルダ単位（空でも PDF が勝つ）、<c>reg_*_list*.csv</c> = モジュール単位。いずれも all-or-nothing で、本体側での穴埋めはしない。</item>
///   <item>書き込み: データセットが選ばれていれば存在に関係なく PDF 側。解決だけではディレクトリを作らない（空フォルダが all-or-nothing を発動させるため）。</item>
/// </list>
/// モジュール配下のパスを組み立てるコードは、ここ以外に書かない（FabriqStudio.Tests の NoHardcodedModulePathsTest が検査する）。
/// </summary>
public interface IModuleDataResolver
{
    /// <summary>ワークスペースルート。未オープン時は例外。</summary>
    string RootPath { get; }

    /// <summary>PDF のルート（profiles/&lt;名&gt;）の絶対パス。存在しなくても返す。</summary>
    string DataSetRoot(string dataSet);

    /// <summary>PDF 側のモジュールフォルダ（profiles/&lt;名&gt;/modules/&lt;module&gt;）の絶対パス。存在しなくても返す。</summary>
    string DataSetModuleDir(string dataSet, string moduleDir);

    /// <summary>PDF にモジュールフォルダがあるモジュール名（profiles/&lt;名&gt;/modules/ 直下）。無ければ空。</summary>
    IReadOnlyList<string> ListDataSetModules(string dataSet);

    /// <summary>
    /// モジュール本体側の相対パス <c>modules/&lt;tier&gt;/&lt;module&gt;[/&lt;rel&gt;]</c> を組み立てる
    /// （standard → extended の順に探索。モジュールが無ければ null）。
    /// </summary>
    string? ModuleRelPath(string moduleDir, string rel = "");

    /// <summary>tier が分かっているときの相対パス組み立て（存在確認はしない）。</summary>
    string ModuleRelPath(string kind, string moduleDir, string rel);

    /// <summary>§2 の写像だけを行う（存在判定なし）。dataSet が空なら恒等。</summary>
    string MapToDataSet(string relPath, string? dataSet);

    /// <summary>読み取り用に解決する（粒度契約を適用）。</summary>
    ResolvedModulePath ResolveRead(string relPath, string? dataSet);

    /// <summary>書き込み用に解決する（データセットが選ばれていれば PDF 側。ディレクトリは作らない）。</summary>
    ResolvedModulePath ResolveWrite(string relPath, string? dataSet);

    /// <summary>
    /// モジュールの設定 CSV 一覧を、カーネルと同じ規則で合成して返す
    /// （名前ごとに PDF 優先。<c>reg_*_list*.csv</c> は PDF 側に 1 件でもあれば PDF 側のみ）。
    /// フレームワーク資産（module.csv / preset.csv）は含めない。
    /// </summary>
    IReadOnlyList<ModuleDataEntry> EnumerateCsvs(string moduleDir, string? dataSet);

    /// <summary>このモジュールを上書きしている PDF の一覧（profiles/*/modules/&lt;module&gt;/ を走査）。</summary>
    IReadOnlyList<ModuleOverride> FindOverrides(string moduleDir);

    /// <summary>フレームワーク資産（PDF に置かれても無視されるもの）か。</summary>
    bool IsFrameworkAsset(string fileName);
}
