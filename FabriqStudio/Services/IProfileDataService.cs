namespace FabriqStudio.Services;

/// <summary>
/// プロファイル別データフォルダ（PDF: profiles/&lt;名&gt;/modules/…）の作成・取り込み・整理。
/// 方針（TM t-0022）:
/// <list type="bullet">
///   <item>PDF は「プロファイルで使うモジュールの分だけ」作る。全モジュールの一括コピーはしない。</item>
///   <item>取り込みは本体からの as-is コピー（バイト単位）。<b>取り込みで実行結果は変わらない</b>。</item>
///   <item>設定 CSV だけを対象にし、資材フォルダは触らない（資材は実際に置くときだけ作る。空フォルダは「資材ゼロ」の明示になるため）。</item>
///   <item><c>reg_*_list*.csv</c> はモジュール単位 all-or-nothing なので、PDF に 1 件でもあれば本体の reg_* は取り込まない（取り込むと本体のサンプル行が動き出す）。</item>
///   <item>フォルダは実際にコピーする瞬間だけ作る。</item>
/// </list>
/// </summary>
public interface IProfileDataService
{
    /// <summary>profiles/&lt;名&gt;/ が存在するか。</summary>
    bool HasDataFolder(string profileName);

    /// <summary>profiles/&lt;名&gt;/ の絶対パス（存在しなくても返す）。</summary>
    string DataFolderPath(string profileName);

    /// <summary>
    /// モジュールの設定 CSV を本体から PDF へ取り込む（PDF に無いものだけ）。
    /// 戻り値はコピーしたファイル名。本体に設定 CSV が無いモジュールでは空。
    /// </summary>
    Task<IReadOnlyList<string>> MaterializeModuleAsync(string profileName, string moduleDir);

    /// <summary>
    /// 1 つの CSV を取り込む。<c>reg_*_list*.csv</c> はモジュール単位に広がる（同モジュールの reg_* を全部）。
    /// </summary>
    Task<IReadOnlyList<string>> MaterializeCsvAsync(string profileName, string moduleDir, string csvName);

    /// <summary>
    /// 使用モジュールのうち、本体に設定 CSV があり PDF に未取り込みのものがあるモジュール（表示順）。
    /// 実行時はこれらが本体へフォールバックし、fabriq が警告を出す。
    /// </summary>
    IReadOnlyList<string> FindMissingModules(string profileName, IEnumerable<string> moduleDirs);

    /// <summary>PDF にモジュールフォルダがあるのに、プロファイルに行が無いモジュール。自動では消さない。</summary>
    IReadOnlyList<string> FindUnusedModules(string profileName, IEnumerable<string> usedModuleDirs);

    /// <summary>PDF のモジュールフォルダ（設定 CSV と資材）を削除する。明示操作専用。</summary>
    void DeleteModuleData(string profileName, string moduleDir);
}
