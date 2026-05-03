using System.Windows;
using FabriqStudio.Models;

namespace FabriqStudio.Views;

/// <summary>
/// Pianist テスト実行の前確認ダイアログ。NewPCName 選択 + profile サマリ + 副作用警告を提示。
///
/// 戻り値は <see cref="PianistTestRunChoice"/>（OK 押下時のみ非 null）。Owner 側は
/// チェックボックスの結果を見て Studio MainWindow の WindowState を切り替えるかどうか
/// 判断する。
/// </summary>
public partial class PianistTestRunDialog : Window
{
    /// <summary>選択された NewPCName。"*" は共通行のみで実行する意。</summary>
    public string SelectedNewPCName { get; private set; } = "*";

    /// <summary>実行直前に Studio ウィンドウを最小化するか。</summary>
    public bool MinimizeStudio { get; private set; } = true;

    private PianistTestRunDialog(
        PianistProfileEntry      profile,
        PianistProfileData       data,
        IReadOnlyList<string>    availableHosts,
        bool                     hasPassphrase)
    {
        InitializeComponent();

        ProfileNameText.Text = profile.Name;
        ProfilePathText.Text = profile.FolderPath;

        // ホスト候補: "*"（先頭固定） + values.csv に登録された行 + hostlist.csv の他ホスト
        var hostsInValues = data.Values.Rows
            .Where(r => !r.IsStar && !string.IsNullOrEmpty(r.NewPCName))
            .Select(r => r.NewPCName)
            .ToList();
        var combined = new List<string> { "*" };
        combined.AddRange(hostsInValues);
        foreach (var h in availableHosts)
            if (!combined.Contains(h, StringComparer.Ordinal))
                combined.Add(h);

        foreach (var h in combined) HostBox.Items.Add(h);
        HostBox.SelectedIndex = 0;

        // サマリ: Phase 数 / Step 数 / Action 種別カウント / ENC: セルあり判定
        var phaseCount = data.Steps.Select(s => s.PhaseID).Distinct(StringComparer.Ordinal).Count();
        var stepCount  = data.Steps.Count;
        var actionStat = data.Steps
            .GroupBy(s => s.Action, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key}={g.Count()}");
        var hasEncCells = data.Values.Rows
            .SelectMany(r => data.Values.VariableColumns.Select(c => r[c]))
            .Any(v => v.StartsWith("ENC:", StringComparison.Ordinal));

        SummaryText.Text =
            $"Phase {phaseCount} / Step {stepCount}" +
            $"\n  Action: {string.Join(", ", actionStat)}" +
            (hasEncCells ? "\n  ENC: セルを含む（パスフレーズで復号）" : "");

        // パスフレーズ未設定 + ENC: あり → 警告表示
        PassphraseWarning.Visibility = (!hasPassphrase && hasEncCells)
            ? Visibility.Visible
            : Visibility.Collapsed;

        Loaded += (_, _) => OkBtn.Focus();
    }

    public static PianistTestRunChoice? Show(
        PianistProfileEntry      profile,
        PianistProfileData       data,
        IReadOnlyList<string>    availableHosts,
        bool                     hasPassphrase,
        Window?                  owner = null)
    {
        var dlg = new PianistTestRunDialog(profile, data, availableHosts, hasPassphrase)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() != true) return null;
        return new PianistTestRunChoice(dlg.SelectedNewPCName, dlg.MinimizeStudio);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        SelectedNewPCName = HostBox.SelectedItem as string ?? "*";
        MinimizeStudio    = MinimizeCheckBox.IsChecked == true;
        DialogResult      = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>テスト実行ダイアログでユーザーが OK 押下したときの選択結果。</summary>
public record PianistTestRunChoice(string NewPCName, bool MinimizeStudio);
