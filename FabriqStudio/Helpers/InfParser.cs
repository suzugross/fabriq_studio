using System.IO;
using System.Text.RegularExpressions;

namespace FabriqStudio.Helpers;

/// <summary>
/// プリンタドライバ INF ファイルからモデル名を抽出するパーサー。
/// fabriq の <c>printer_driver_config/printer_driver_install.ps1</c> 内
/// <c>Get-ValidInfFiles</c> のロジックを C# に忠実移植したもの。
/// <para>
/// 判定条件:
/// <list type="number">
///   <item><c>[Manufacturer]</c> セクション内に <c>NTamd64</c> の参照がある</item>
///   <item><c>[*.NTamd64]</c> セクション内に <c>"ModelName" = ...</c> 形式の行が 1 つ以上ある</item>
/// </list>
/// 両方を満たす INF のみを有効とみなし、抽出された全モデル名を返す。
/// </para>
/// <para>
/// エンコーディング: INF は UTF-16 LE(BOM 付き) が一般的だが、UTF-8(BOM)・UTF-16 BE・ASCII も
/// 混在する。<see cref="File.ReadAllText(string)"/> が BOM を自動検出するためそれに委ねる。
/// </para>
/// </summary>
public static class InfParser
{
    /// <summary>現状は 64bit Windows 前提のため固定（将来の拡張時に切替可能）。</summary>
    public const string Architecture = "NTamd64";

    // セクション検出: 行頭 '[' で始まる任意のセクションヘッダ
    private static readonly Regex SectionHeaderRegex =
        new(@"^\[.*\]", RegexOptions.Compiled);

    // [Manufacturer] セクション
    private static readonly Regex ManufacturerRegex =
        new(@"^\[Manufacturer\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // モデル定義セクション: [*.NTamd64]
    private static readonly Regex ModelSectionRegex =
        new($@"^\[.+\.{Architecture}\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // モデル行: "Model Name" = ...
    private static readonly Regex ModelEntryRegex =
        new(@"^""(.+?)""\s*=", RegexOptions.Compiled);

    /// <summary>
    /// 指定 INF を解析し、有効（アーキテクチャ一致かつモデル行あり）の場合のみ
    /// モデル名一覧を返す。無効・読み込み失敗時は空リスト。
    /// </summary>
    public static IReadOnlyList<string> ExtractModelNames(string infPath)
    {
        string text;
        try
        {
            // BOM 自動検出: UTF-8(BOM)/UTF-16 LE(BOM)/UTF-16 BE(BOM)/UTF-32 に対応
            text = File.ReadAllText(infPath);
        }
        catch
        {
            return Array.Empty<string>();
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        bool inManufacturer = false;
        bool hasArchInManufacturer = false;
        bool inModelSection = false;
        var models = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            // [Manufacturer] セクションに入る
            if (ManufacturerRegex.IsMatch(line))
            {
                inManufacturer = true;
                continue;
            }

            // [Manufacturer] 中に別のセクションヘッダが来たら抜ける
            if (inManufacturer && SectionHeaderRegex.IsMatch(line)
                && !ManufacturerRegex.IsMatch(line))
            {
                inManufacturer = false;
            }

            // [Manufacturer] 内で NTamd64 への参照を検出
            if (inManufacturer && line.Contains(Architecture, StringComparison.OrdinalIgnoreCase))
            {
                hasArchInManufacturer = true;
            }

            // [XXX.NTamd64] セクションに入る
            if (ModelSectionRegex.IsMatch(line))
            {
                inModelSection = true;
                continue;
            }

            // モデルセクション中に別のセクションヘッダが来たら抜ける
            if (inModelSection && SectionHeaderRegex.IsMatch(line)
                && !ModelSectionRegex.IsMatch(line))
            {
                inModelSection = false;
                continue;
            }

            // モデルセクション内の "ModelName" = ... 行からモデル名を収集（重複排除）
            if (inModelSection)
            {
                var m = ModelEntryRegex.Match(line);
                if (m.Success)
                {
                    var name = m.Groups[1].Value;
                    if (!models.Contains(name, StringComparer.Ordinal))
                        models.Add(name);
                }
            }
        }

        // [Manufacturer] に該当アーキテクチャ参照がないか、モデル行が 0 件なら無効
        if (!hasArchInManufacturer || models.Count == 0)
            return Array.Empty<string>();

        return models;
    }
}
