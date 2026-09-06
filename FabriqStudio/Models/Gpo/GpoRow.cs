namespace FabriqStudio.Models.Gpo;

/// <summary>gpo_list.csv の Action 列。</summary>
public static class GpoActions
{
    public const string Set             = "Set";
    public const string Delete          = "Delete";
    public const string DeleteAllValues = "DeleteAllValues";
    public const string CreateKey       = "CreateKey";
    public const string Unmanage        = "Unmanage";
}

/// <summary>コンパイル結果の 1 行（gpo_list.csv の 1 行に対応。Enabled / AdminID / Segment は書き込み側が付ける）。</summary>
public sealed class GpoRow
{
    public string Scope     { get; init; } = GpoPolicyClass.Machine;
    public string KeyPath   { get; init; } = "";
    public string ValueName { get; init; } = "";
    public string Action    { get; init; } = GpoActions.Set;
    public string Type      { get; init; } = "";
    public string Value     { get; init; } = "";
    public string Title     { get; init; } = "";
    public string PolicyRef { get; init; } = "";

    /// <summary>同一エントリ判定キー（Scope + KeyPath + ValueName。大文字小文字を無視）。</summary>
    public string DedupeKey => $"{Scope}|{KeyPath}|{ValueName}".ToLowerInvariant();

    /// <summary>プレビュー用の 1 行表示。</summary>
    public string Display
    {
        get
        {
            var target = string.IsNullOrEmpty(ValueName) ? KeyPath : $"{KeyPath}\\{ValueName}";
            return Action switch
            {
                GpoActions.Set => $"{Action}  {target} = {Value} ({Type})",
                _              => $"{Action}  {target}",
            };
        }
    }
}

public sealed class GpoCompileResult
{
    public List<GpoRow>  Rows     { get; } = [];
    public List<string>  Errors   { get; } = [];
    public List<string>  Warnings { get; } = [];
    public bool HasErrors => Errors.Count > 0;
}
