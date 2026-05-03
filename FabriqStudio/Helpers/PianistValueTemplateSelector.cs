using System.Windows;
using System.Windows.Controls;
using FabriqStudio.Models;

namespace FabriqStudio.Helpers;

/// <summary>
/// Step グリッドの Value 列に対し、Action 値ごとに DataTemplate を選択する。
/// XAML の DataTrigger / Visibility binding の不安定挙動を避けるため、
/// 評価ロジックを C# 側で明示的に書く（ContentControl が edit mode に入った瞬間に
/// SelectTemplate が呼ばれ、template が確定する）。
/// </summary>
public class PianistValueTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DefaultTemplate { get; set; }
    public DataTemplate? KeyTemplate     { get; set; }
    public DataTemplate? VarRefTemplate  { get; set; }
    public DataTemplate? WaitTemplate    { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not PianistStep step) return DefaultTemplate;
        return step.Action switch
        {
            "Key"                                       => KeyTemplate    ?? DefaultTemplate,
            "Type" or "Paste" or "Copy" or "Prompt"     => VarRefTemplate ?? DefaultTemplate,
            "Wait"                                      => WaitTemplate   ?? DefaultTemplate,
            _                                           => DefaultTemplate,
        };
    }
}

/// <summary>
/// Step グリッドの Wait 列向け（Action 値が "Wait" のときだけ "(Value 列を使用)"
/// プレースホルダ表示、それ以外は数値編集テンプレート）。
/// </summary>
public class PianistWaitTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NumericTemplate  { get; set; }
    public DataTemplate? DisabledTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not PianistStep step) return NumericTemplate;
        return step.Action == "Wait"
            ? (DisabledTemplate ?? NumericTemplate)
            : NumericTemplate;
    }
}
