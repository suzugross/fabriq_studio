using FabriqStudio.Models;

namespace FabriqStudio.Services;

public interface IModuleService
{
    /// <summary>
    /// modules/standard/ および modules/extended/ 以下の全モジュールを
    /// それぞれの module.csv から読み込み、マスターリストとして返す。
    /// </summary>
    Task<IReadOnlyList<ModuleMasterEntry>> GetAllModulesAsync();

    /// <summary>
    /// 全モジュールの設定 CSV（module.csv / preset.csv を除く）を走査し、
    /// モジュールディレクトリ名（大文字小文字無視）→ そのモジュールの設定 CSV に
    /// 実在する非空 Segment 値（distinct・昇順）の辞書を返す。
    /// Segment 列を持たない／値が無いモジュールはキーを持たない。
    /// プロファイル編集画面のセグメント列の候補値ソースとして使う。
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetModuleSegmentsAsync();
}
