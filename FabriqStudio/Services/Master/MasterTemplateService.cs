using System.IO;
using System.Text.Json;
using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// master_template/master_template.json を読む。
/// registry_collection と同じく AppDomain.CurrentDomain.BaseDirectory 起点（ポータブル運用）。
/// </summary>
public sealed class MasterTemplateService : IMasterTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    public string TemplatePath { get; } = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "master_template", "master_template.json");

    public async Task<MasterTemplate> LoadAsync()
    {
        if (!File.Exists(TemplatePath))
            throw new FileNotFoundException(
                $"マスタ設計テンプレートが見つかりません: {TemplatePath}", TemplatePath);

        await using var stream = File.OpenRead(TemplatePath);
        var template = await JsonSerializer.DeserializeAsync<MasterTemplate>(stream, JsonOptions)
            ?? throw new InvalidDataException("マスタ設計テンプレートの内容が空です。");

        // 最低限の整合性: ID 重複はテンプレート作成ミスなので早期に落とす
        var dup = template.Sections.SelectMany(s => s.Items)
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new InvalidDataException($"マスタ設計テンプレートの項目 ID が重複しています: {dup.Key}");

        return template;
    }
}
