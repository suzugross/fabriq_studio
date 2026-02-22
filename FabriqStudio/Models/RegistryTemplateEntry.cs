using System.Text.Json.Serialization;

namespace FabriqStudio.Models;

/// <summary>
/// レジストリ設定テンプレートの 1 エントリ。
/// AppBaseDir/registry_collection/catalog.json に永続化される。
/// </summary>
public sealed class RegistryTemplateEntry
{
    /// <summary>エントリを一意に識別する 8 文字の hex ID。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>レジストリハイブ。"HKLM" または "HKCU"。</summary>
    [JsonPropertyName("hive")]
    public string Hive { get; set; } = "HKLM";

    /// <summary>フルパス形式（例: HKEY_LOCAL_MACHINE\SYSTEM\...）。</summary>
    [JsonPropertyName("keyPath")]
    public string KeyPath { get; set; } = "";

    [JsonPropertyName("keyName")]
    public string KeyName { get; set; } = "";

    /// <summary>値の型。例: REG_DWORD, REG_SZ, REG_BINARY。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "REG_DWORD";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>
    /// このレジストリ設定の説明文。
    /// 旧 docs/*.txt ファイルの内容を JSON 内にインライン化したもの。
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>セミコロン区切りのタグ（例: "RDP;リモートデスクトップ;接続"）。</summary>
    [JsonPropertyName("tags")]
    public string Tags { get; set; } = "";
}
