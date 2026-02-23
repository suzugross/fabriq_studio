namespace FabriqStudio.Models;

/// <summary>
/// コマンドライブラリ（uri_list.csv）の 1 行を表す読み取り専用モデル。
/// CsvHelper の列名マッピング（PascalCase）で直接デシリアライズされる。
/// </summary>
public class GyotaqCommand
{
    /// <summary>分類: "Windows Settings", "Control Panel", "System Tools" 等。</summary>
    public string Category { get; set; } = "";

    /// <summary>表示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>自動起動コマンド（URI / exe / msc）。</summary>
    public string OpenCommand { get; set; } = "";

    /// <summary>コマンド引数。</summary>
    public string OpenArgs { get; set; } = "";

    /// <summary>タスク追加時の既定タイトル。</summary>
    public string DefaultTitle { get; set; } = "";

    /// <summary>タスク追加時の既定指示文。</summary>
    public string DefaultInstruction { get; set; } = "";
}
