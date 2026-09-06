using CommunityToolkit.Mvvm.ComponentModel;
using FabriqStudio.Models.Gpo;

namespace FabriqStudio.ViewModels.Gpo;

/// <summary>
/// ポリシー要素（gpedit のオプション欄のサブ項目）の入力 VM 基底。
/// 値は回答保存形式の文字列（bool "1"/"0"、enum は項目値、list/multiText は改行区切り）で出し入れする。
/// </summary>
public abstract partial class GpoElementViewModel : ObservableObject
{
    protected readonly Action OnChanged;

    protected GpoElementViewModel(GpoElement element, Action onChanged)
    {
        Element   = element;
        OnChanged = onChanged;
    }

    public GpoElement Element { get; }

    public string  Id           => Element.Id;
    public string  Label        => Element.DisplayLabel;
    public string? Note         => Element.Note;
    public bool    HasNote      => !string.IsNullOrEmpty(Element.Note);
    public bool    Required     => Element.Required;
    public string  RequiredMark => Element.Required ? " ＊" : "";

    /// <summary>状態が「有効」のときだけ入力できる。</summary>
    [ObservableProperty] private bool _isEnabled = true;

    public abstract string Value { get; set; }

    public static GpoElementViewModel Create(GpoElement e, Action onChanged) => e.Type switch
    {
        GpoElementType.Boolean   => new GpoBoolElementViewModel(e, onChanged),
        GpoElementType.Enum      => new GpoEnumElementViewModel(e, onChanged),
        GpoElementType.List      => new GpoLinesElementViewModel(e, onChanged),
        GpoElementType.MultiText => new GpoLinesElementViewModel(e, onChanged),
        _                        => new GpoTextElementViewModel(e, onChanged),
    };
}

/// <summary>boolean 要素（チェックボックス）。</summary>
public sealed partial class GpoBoolElementViewModel : GpoElementViewModel
{
    [ObservableProperty] private bool _isChecked;

    public GpoBoolElementViewModel(GpoElement element, Action onChanged) : base(element, onChanged) { }

    partial void OnIsCheckedChanged(bool value) => OnChanged();

    public override string Value
    {
        get => IsChecked ? "1" : "0";
        set => IsChecked = value.Trim() == "1";
    }
}

/// <summary>enum 要素（ドロップダウン）。値は選択肢の登録値（ToString）で保存する。</summary>
public sealed partial class GpoEnumElementViewModel : GpoElementViewModel
{
    [ObservableProperty] private GpoEnumItem? _selected;

    public GpoEnumElementViewModel(GpoElement element, Action onChanged) : base(element, onChanged)
        => _selected = DefaultItem;

    public IReadOnlyList<GpoEnumItem> Items => Element.Items;

    private GpoEnumItem? DefaultItem
    {
        get
        {
            if (Items.Count == 0) return null;
            var i = Element.DefaultItem is { } d && d >= 0 && d < Items.Count ? d : 0;
            return Items[i];
        }
    }

    partial void OnSelectedChanged(GpoEnumItem? value) => OnChanged();

    public override string Value
    {
        get => Selected?.Value.ToString() ?? "";
        set
        {
            var v = value.Trim();
            Selected = Items.FirstOrDefault(i => i.Value.ToString() == v) ?? DefaultItem;
        }
    }
}

/// <summary>text / decimal / longDecimal 要素（1 行入力）。数値は範囲を検証する。</summary>
public sealed partial class GpoTextElementViewModel : GpoElementViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _text = "";

    public GpoTextElementViewModel(GpoElement element, Action onChanged) : base(element, onChanged) { }

    public bool   IsNumeric      => Element.IsNumeric;
    public bool   HasRange       => Element.IsNumeric;
    public string RangeText      => Element.RangeText;
    public bool   HasSuggestions => Element.Suggestions.Count > 0;
    public string SuggestionText => HasSuggestions ? "候補: " + string.Join(", ", Element.Suggestions) : "";

    /// <summary>空は有効（必須チェックはコンパイル側）。数値は整数かつ範囲内。</summary>
    public bool IsValid
    {
        get
        {
            var t = Text.Trim();
            if (t.Length == 0 || !IsNumeric) return true;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                t = ulong.TryParse(t[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex) ? hex.ToString() : "x";
            if (!ulong.TryParse(t, out var n)) return false;
            if (Element.Type == GpoElementType.Decimal && n > uint.MaxValue) return false;
            if (Element.MinValue is { } min && n < min) return false;
            if (Element.MaxValue is { } max && n > max) return false;
            return true;
        }
    }

    partial void OnTextChanged(string value) => OnChanged();

    public override string Value
    {
        get => Text;
        set => Text = value;
    }
}

/// <summary>list / multiText 要素（複数行入力。1 行 1 件）。</summary>
public sealed partial class GpoLinesElementViewModel : GpoElementViewModel
{
    [ObservableProperty] private string _text = "";

    public GpoLinesElementViewModel(GpoElement element, Action onChanged) : base(element, onChanged) { }

    public string Hint => Element.Type == GpoElementType.List
        ? Element.ExplicitValue     ? "1 行に 1 件、「名前=値」の形式で入力"
        : Element.ValuePrefix != null ? $"1 行に 1 件（値名は {Element.ValuePrefix}1, {Element.ValuePrefix}2 … と自動で付きます）"
        :                              "1 行に 1 件（値名 = 値 として書き込みます）"
        : "1 行に 1 件（REG_MULTI_SZ）";

    partial void OnTextChanged(string value) => OnChanged();

    public override string Value
    {
        get => Text.Replace("\r\n", "\n");
        set => Text = value.Replace("\r\n", "\n");
    }
}
