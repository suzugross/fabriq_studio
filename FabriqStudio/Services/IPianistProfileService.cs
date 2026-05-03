using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// Pianist Profile (modules/extended/pianist/profiles/&lt;name&gt;/) の I/O を司るサービス。
/// 通常の <see cref="IProfileService"/>（profiles/*.csv のモジュール実行リスト）とは別物。
///
/// Phase 1 では読み込み API のみ提供する。新規作成 / 保存 / Phase 操作 / 列リネームなどの
/// 書き込み API は Phase 2 以降で順次追加する。
/// </summary>
public interface IPianistProfileService
{
    /// <summary>
    /// modules/extended/pianist/profiles/ 直下のサブディレクトリを Pianist Profile として列挙する。
    /// 名前順でソートして返す。pianist プロファイルディレクトリが存在しない場合は空リスト。
    /// </summary>
    Task<IReadOnlyList<PianistProfileEntry>> GetProfilesAsync();

    /// <summary>
    /// 指定された Pianist Profile フォルダから全データ（json / 3 CSV / instructions/*.txt）を
    /// 読み込んで返す。pianist.ps1 が無くても起動できる profile （ファイル欠損あり）も
    /// 寛容に扱う — 欠損ファイルは空のオブジェクトで埋める（§7.1 と同じ哲学）。
    /// </summary>
    /// <exception cref="FileNotFoundException">profile フォルダ自体が存在しない場合</exception>
    Task<PianistProfileData> LoadProfileAsync(PianistProfileEntry entry);

    /// <summary>
    /// 指定 profile を §10 規約で書き出す（pianist.json: BOM なし + LF / CSV: BOM 付き + CRLF /
    /// instructions/*.txt: BOM なし + LF）。values.csv のセル値はメモリ上の表現（暗号文 or 平文）を
    /// そのまま書き出す（暗号化／復号は事前に右クリック ContextMenu で明示操作する方針）。
    /// </summary>
    /// <returns>null=成功 / 非 null = エラーメッセージ（呼び出し側で表示）</returns>
    Task<string?> SaveProfileAsync(PianistProfileData data, ICryptoService crypto);

    /// <summary>
    /// 旧 long format（Key,Value,Encrypted,Note）の values.csv を読み出す。
    /// wide format への移行（§5.5）専用 — 通常の <see cref="LoadProfileAsync"/> は wide 前提。
    /// </summary>
    Task<IReadOnlyList<PianistLegacyValueEntry>> LoadLegacyValuesAsync(PianistProfileEntry entry);

    /// <summary>
    /// プロファイル名のバリデーション（半角英数 + アンダースコアのみ、未使用名）。
    /// </summary>
    /// <returns>null=OK / 非 null=エラーメッセージ</returns>
    string? ValidateNewProfileName(string name);

    /// <summary>
    /// modules/extended/pianist/profiles/&lt;name&gt;/ にテンプレートから空の Pianist Profile を作成する。
    /// 5 ファイル（pianist.json, procedure.csv, values.csv, shortcuts.csv, instructions/P01.txt）を §10 規約で生成。
    /// 初期内容は最小限の placeholder（P01 に Wait 1000ms の Step 1 件 + `*` 行のみの空 values.csv）。
    /// </summary>
    Task<PianistProfileEntry> CreateNewProfileAsync(string name);
}
