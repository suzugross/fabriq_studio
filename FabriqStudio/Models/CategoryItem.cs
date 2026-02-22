namespace FabriqStudio.Models;

/// <summary>
/// kernel/csv/categories.csv の1行を表すモデル
/// </summary>
public class CategoryItem
{
    public string Category { get; set; } = "";
    public int    Order    { get; set; }
}
