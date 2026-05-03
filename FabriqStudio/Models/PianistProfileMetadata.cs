using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// pianist.json のスキーマ。フィールドは全て snake_case で
/// 直接シリアライズ／デシリアライズする（pianist.ps1 が読む形式に厳密準拠）。
///
/// プロパティ順序は出力時のキー順と一致させている（schema → label → description →
/// target_app → default_phase → version）。Studio 側で書き出す JSON は §10 規約に従い
/// UTF-8 BOM なし + LF + 末尾改行で正規化する（Phase 5 で実装）。
/// </summary>
public partial class PianistProfileMetadata : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("schema")]
    [property: JsonPropertyOrder(0)]
    private int _schema = 1;

    [ObservableProperty]
    [property: JsonPropertyName("label")]
    [property: JsonPropertyOrder(1)]
    private string _label = "";

    [ObservableProperty]
    [property: JsonPropertyName("description")]
    [property: JsonPropertyOrder(2)]
    private string _description = "";

    [ObservableProperty]
    [property: JsonPropertyName("target_app")]
    [property: JsonPropertyOrder(3)]
    private string _targetApp = "";

    [ObservableProperty]
    [property: JsonPropertyName("default_phase")]
    [property: JsonPropertyOrder(4)]
    private string _defaultPhase = "";

    [ObservableProperty]
    [property: JsonPropertyName("version")]
    [property: JsonPropertyOrder(5)]
    private string _version = "0.1.0";
}
