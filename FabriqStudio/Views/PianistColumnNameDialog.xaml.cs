using System.Windows;
using System.Windows.Controls;
using FabriqStudio.Models;

namespace FabriqStudio.Views;

/// <summary>
/// 変数列の新規追加 / リネーム用ダイアログ。
///
/// バリデーションは外部から関数として注入する（VM の <c>ValidateColumnName</c> を直接呼ぶ）。
/// リネームモード時は procedure.csv 一括書換チェックボックス + 影響行プレビューボタンを表示。
/// </summary>
public partial class PianistColumnNameDialog : Window
{
    /// <summary>OK 押下時の入力列名（trim 済み）。</summary>
    public string ColumnName { get; private set; } = "";

    /// <summary>リネーム時のみ意味を持つ。procedure.csv を一括書換するかどうか。</summary>
    public bool RewriteProcedure { get; private set; } = true;

    private readonly Func<string, string?> _validator;
    private readonly Func<IReadOnlyList<PianistStep>>? _previewProvider;
    private readonly bool   _isRename;
    private readonly string _initialValue;

    private PianistColumnNameDialog(
        string title,
        string headerText,
        string initialValue,
        bool isRename,
        Func<string, string?> validator,
        Func<IReadOnlyList<PianistStep>>? previewProvider)
    {
        InitializeComponent();

        // ⚠ readonly フィールドを **TextBox.Text 設定より前に** 代入する（同名のフィールドが
        // null だと TextChanged → Validate() が NullReferenceException でクラッシュする）。
        _validator       = validator;
        _previewProvider = previewProvider;
        _isRename        = isRename;
        _initialValue    = initialValue;

        Title              = title;
        HeaderText.Text    = headerText;
        ColumnNameBox.Text = initialValue;

        if (isRename)
        {
            RenameOptions.Visibility = Visibility.Visible;
            OldRefRun.Text = "$" + initialValue;
            NewRefRun.Text = "$" + initialValue;
        }

        Loaded += (_, _) =>
        {
            ColumnNameBox.Focus();
            ColumnNameBox.SelectAll();
            Validate();
        };
    }

    /// <summary>
    /// 新規追加用ダイアログを表示。
    /// </summary>
    public static string? ShowAdd(Func<string, string?> validator, Window? owner = null)
    {
        var dlg = new PianistColumnNameDialog(
            title:           "変数列を追加",
            headerText:      "新しい列名（^[A-Za-z_][A-Za-z0-9_]*$, NewPCName は予約）",
            initialValue:    "",
            isRename:        false,
            validator:       validator,
            previewProvider: null)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true ? dlg.ColumnName : null;
    }

    /// <summary>
    /// リネーム用ダイアログを表示。
    /// </summary>
    public static (string newName, bool rewriteProcedure)? ShowRename(
        string oldName,
        Func<string, string?> validator,
        Func<IReadOnlyList<PianistStep>> previewProvider,
        Window? owner = null)
    {
        var dlg = new PianistColumnNameDialog(
            title:           $"変数列をリネーム: {oldName}",
            headerText:      "新しい列名（^[A-Za-z_][A-Za-z0-9_]*$, NewPCName は予約）",
            initialValue:    oldName,
            isRename:        true,
            validator:       validator,
            previewProvider: previewProvider)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true
            ? (dlg.ColumnName, dlg.RewriteProcedure)
            : null;
    }

    private void Validate()
    {
        var name  = ColumnNameBox.Text?.Trim() ?? "";
        var error = _validator(name);

        // リネームで新旧同一は no-op として OK 無効化
        if (_isRename && string.Equals(name, _initialValue, System.StringComparison.Ordinal))
            error = "新しい列名が現在の列名と同じです。";

        ErrorText.Text       = error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        OkBtn.IsEnabled      = string.IsNullOrEmpty(error);

        // リネームのプレビュー表示用: 新列名がプレビューに反映されるよう Run も更新
        if (_isRename)
            NewRefRun.Text = "$" + (string.IsNullOrEmpty(name) ? "..." : name);
    }

    private void ColumnNameBox_TextChanged(object sender, TextChangedEventArgs e) => Validate();

    private void PreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_previewProvider is null) return;

        var steps = _previewProvider();
        if (steps.Count == 0)
        {
            MessageBox.Show(this,
                $"procedure.csv で $「{_initialValue}」を参照している Step はありません。",
                "影響行プレビュー", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = steps.Take(20)
            .Select(s => $"  {s.PhaseID}  Step {s.StepNo}  ({s.Action}): {s.Value}");
        var msg = $"以下の {steps.Count} 件の Step が「${_initialValue}」を参照しています:\n\n"
                  + string.Join("\n", lines)
                  + (steps.Count > 20 ? $"\n  ... 他 {steps.Count - 20} 件" : "");
        MessageBox.Show(this, msg, "影響行プレビュー",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        ColumnName       = ColumnNameBox.Text.Trim();
        RewriteProcedure = RewriteCheckBox.IsChecked == true;
        DialogResult     = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
