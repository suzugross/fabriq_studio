using System.Windows;
using System.Windows.Controls;

namespace FabriqStudio.Views;

/// <summary>
/// Phase 新規作成 / リネーム用ダイアログ。
///
/// <see cref="ShowNew"/> は空フィールドから入力させる。<see cref="ShowRename"/> は既存 Phase の
/// 値で初期化し、追加で「instructions/.txt 追従リネーム」チェックボックスを表示する（既定 ON）。
/// バリデーションは外部から関数として注入する。
/// </summary>
public partial class PianistPhaseEditDialog : Window
{
    public string PhaseId    { get; private set; } = "";
    public string PhaseLabel { get; private set; } = "";
    public string Color      { get; private set; } = "Blue";

    /// <summary>リネームモード時のみ意味を持つ。instructions/&lt;old&gt;.txt を追従リネームするか。</summary>
    public bool RenameInstructionsFile { get; private set; } = true;

    private readonly Func<string, string?> _validator;
    private readonly bool   _isRename;
    private readonly string _initialPhaseId;

    private PianistPhaseEditDialog(
        string title,
        string initialId,
        string initialLabel,
        string initialColor,
        IReadOnlyList<string> colors,
        bool isRename,
        Func<string, string?> validator)
    {
        InitializeComponent();

        // ⚠ readonly フィールドを **TextBox.Text 設定より前に** 代入する。
        // PhaseIdBox.Text 代入で TextChanged が発火 → Validate() が走る → _validator が
        // null だと NullReferenceException でクラッシュするため。
        _validator      = validator;
        _isRename       = isRename;
        _initialPhaseId = initialId;

        Title              = title;
        PhaseIdBox.Text    = initialId;
        PhaseLabelBox.Text = initialLabel;
        ColorCombo.ItemsSource = colors;
        ColorCombo.SelectedItem = colors.Contains(initialColor) ? initialColor : (colors.FirstOrDefault() ?? "Blue");

        if (isRename)
        {
            RenameInstructionsCheck.Visibility = Visibility.Visible;
            OldFileRun.Text = initialId;
            NewFileRun.Text = initialId;
            // PhaseID 入力中は新ファイル名 Run を更新する
            PhaseIdBox.TextChanged += (_, _) =>
            {
                NewFileRun.Text = string.IsNullOrWhiteSpace(PhaseIdBox.Text) ? "..." : PhaseIdBox.Text.Trim();
            };
        }

        Loaded += (_, _) =>
        {
            PhaseIdBox.Focus();
            PhaseIdBox.SelectAll();
            Validate();
        };
    }

    public static (string id, string label, string color)? ShowNew(
        IReadOnlyList<string> colors,
        Func<string, string?> validator,
        Window? owner = null)
    {
        var dlg = new PianistPhaseEditDialog(
            title:        "Phase を新規作成",
            initialId:    "",
            initialLabel: "",
            initialColor: "Blue",
            colors:       colors,
            isRename:     false,
            validator:    validator)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true
            ? (dlg.PhaseId, dlg.PhaseLabel, dlg.Color)
            : null;
    }

    public static (string id, string label, string color, bool renameFile)? ShowRename(
        string currentId,
        string currentLabel,
        string currentColor,
        IReadOnlyList<string> colors,
        Func<string, string?> validator,
        Window? owner = null)
    {
        var dlg = new PianistPhaseEditDialog(
            title:        $"Phase をリネーム: {currentId}",
            initialId:    currentId,
            initialLabel: currentLabel,
            initialColor: currentColor,
            colors:       colors,
            isRename:     true,
            validator:    validator)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true
            ? (dlg.PhaseId, dlg.PhaseLabel, dlg.Color, dlg.RenameInstructionsFile)
            : null;
    }

    private void Validate()
    {
        var id    = PhaseIdBox.Text?.Trim() ?? "";
        var error = _validator(id);

        if (_isRename && string.Equals(id, _initialPhaseId, StringComparison.Ordinal)
                      && PhaseLabelBox.Text == PhaseLabelBox.Text)  // ID 同一は no-op の可能性 — Label / Color 変更は許可
        {
            // PhaseID が同じでも Label / Color の変更を意図する場合があるため OK は有効のまま
            // （VM 側で oldId == newId の場合 PhaseID 変更はスキップ、Label / Color のみ反映する）
        }

        ErrorText.Text       = error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        OkBtn.IsEnabled      = string.IsNullOrEmpty(error);
    }

    private void PhaseIdBox_TextChanged(object sender, TextChangedEventArgs e) => Validate();

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        PhaseId                = PhaseIdBox.Text.Trim();
        PhaseLabel             = PhaseLabelBox.Text;
        Color                  = ColorCombo.SelectedItem as string ?? "Blue";
        RenameInstructionsFile = RenameInstructionsCheck.IsChecked == true;
        DialogResult           = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
