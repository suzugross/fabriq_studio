namespace FabriqStudio.Models;

/// <summary>
/// kernel/csv/log_destinations.csv の1行を表すモデル。
/// Enabled は fabriq の慣例に合わせて "0"/"1" 文字列のまま保持する。
/// </summary>
public class LogDestination
{
    public string Path        { get; set; } = "";
    public string Type        { get; set; } = "";
    public string Enabled     { get; set; } = "0";
    public string Description { get; set; } = "";
}
