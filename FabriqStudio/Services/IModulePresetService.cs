namespace FabriqStudio.Services;

public interface IModulePresetService
{
    /// <summary>
    /// 指定モジュールディレクトリの preset.csv を読み込み、
    /// 列名 → 選択肢（Value のみ）の辞書に整形して返す。
    /// </summary>
    /// <param name="moduleDirAbsolutePath">モジュールフォルダの絶対パス（preset.csv が直下にある想定）</param>
    /// <returns>
    /// preset.csv が存在しない場合や読み込みに失敗した場合は空の辞書を返す
    /// （graceful degradation: 既存モジュールの動作に影響させない）。
    /// キーは列名（大文字小文字を区別しない比較用に <see cref="StringComparer.OrdinalIgnoreCase"/>）。
    /// </returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadAsync(string moduleDirAbsolutePath);
}
