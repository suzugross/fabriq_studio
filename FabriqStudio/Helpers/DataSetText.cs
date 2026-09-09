namespace FabriqStudio.Helpers;

/// <summary>編集先データセット（PDF）の表示文言。</summary>
public static class DataSetText
{
    /// <summary>「書き先: 本体（既定値）」／「書き先: プロファイル X のデータフォルダ（profiles/X/modules/）」。</summary>
    public static string WriteTarget(string? dataSet) => dataSet is null
        ? "書き先: 本体（既定値）"
        : $"書き先: プロファイル {dataSet} のデータフォルダ（profiles/{dataSet}/modules/）";
}
