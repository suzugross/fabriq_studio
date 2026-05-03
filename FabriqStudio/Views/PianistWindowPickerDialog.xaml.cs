using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FabriqStudio.Helpers;

namespace FabriqStudio.Views;

/// <summary>
/// Visible-window picker for the Pianist Step editor's <c>WaitWin</c> /
/// <c>AppFocus</c> Value column.
///
/// Behaviors:
/// - Lists currently visible top-level windows via <see cref="WindowEnumerator"/>.
/// - "ノイズを隠す" filters shell / IME background plus this process (Studio +
///   the picker itself) so the user never accidentally targets us.
/// - "5 秒待ってから取得" minimizes the dialog to let the user open a transient
///   dialog (Win+R, Save As, ...) before re-enumerating.
/// - Selecting a row populates the candidate ComboBox via
///   <see cref="WindowTitleSplitter"/>; the user can pick a substring or edit
///   freely. Double-click on a row commits with the current candidate.
/// </summary>
public partial class PianistWindowPickerDialog : Window
{
    /// <summary>OK 押下時の挿入文字列（trim 済み、null なら未確定 / キャンセル）。</summary>
    public string? PickedText { get; private set; }

    private readonly uint _selfPid;
    private readonly ObservableCollection<WindowInfo> _windows = new();

    private PianistWindowPickerDialog()
    {
        InitializeComponent();
        _selfPid = (uint)Process.GetCurrentProcess().Id;

        // Sortable view, default = Title ascending. The user can flip to
        // ProcessName via the column header.
        var view = CollectionViewSource.GetDefaultView(_windows);
        view.SortDescriptions.Add(new SortDescription(nameof(WindowInfo.Title), ListSortDirection.Ascending));
        WindowsGrid.ItemsSource = view;

        Loaded += (_, _) => RefreshList();
    }

    /// <summary>
    /// ピッカーを開いて挿入文字列を取得する。null = キャンセル / 未選択。
    /// </summary>
    /// <param name="initialValue">将来用に受けるが v1 では未使用（プロンプト §3.5：
    /// 初期値はあえて反映せず一覧から選ぶ UX）</param>
    public static string? Show(string initialValue, Window? owner = null)
    {
        var dlg = new PianistWindowPickerDialog
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true ? dlg.PickedText : null;
    }

    // ─── window enumeration ───────────────────────────────────────────

    private void RefreshList()
    {
        // Best-effort preserve selection by Title across refreshes.
        var preservedTitle = (WindowsGrid.SelectedItem as WindowInfo)?.Title;

        var raw = WindowEnumerator.EnumerateVisibleWindows();
        var filtered = HideNoiseCheck.IsChecked == true
            ? WindowEnumerator.FilterNoise(raw, _selfPid).ToList()
            : raw;

        // Diff-merge into the existing ObservableCollection so the grid does
        // not lose its selection / scroll position from a wholesale reset
        // (same rationale as feedback_combobox_itemssource — an ObservableCollection
        // Reset throws away the selection we are trying to preserve).
        var desiredHandles = filtered.Select(w => w.Handle).ToHashSet();

        for (int i = _windows.Count - 1; i >= 0; i--)
            if (!desiredHandles.Contains(_windows[i].Handle))
                _windows.RemoveAt(i);

        var existingHandles = _windows.Select(w => w.Handle).ToHashSet();
        foreach (var w in filtered)
            if (!existingHandles.Contains(w.Handle))
                _windows.Add(w);

        // Restore selection if still present.
        if (preservedTitle is not null)
        {
            var match = _windows.FirstOrDefault(w => w.Title == preservedTitle);
            if (match is not null) WindowsGrid.SelectedItem = match;
        }
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void HideNoiseCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Ignore events fired during InitializeComponent before _windows / grid are wired.
        if (!IsLoaded) return;
        RefreshList();
    }

    // ─── countdown capture ────────────────────────────────────────────

    private bool _countdownRunning;

    private async void CountdownBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_countdownRunning) return;
        _countdownRunning  = true;
        CountdownBtn.IsEnabled = false;
        RefreshBtn.IsEnabled   = false;

        try
        {
            var prevState = WindowState;
            WindowState = WindowState.Minimized;

            for (int i = 5; i > 0; i--)
            {
                CountdownLabel.Text = $"⏱ {i} 秒後に取得...";
                await Task.Delay(1000);
            }

            CountdownLabel.Text = "";
            WindowState = prevState == WindowState.Minimized ? WindowState.Normal : prevState;
            Activate();
            // Brief topmost flash to come back above the captured target window.
            Topmost = true;
            Topmost = false;

            RefreshList();
        }
        finally
        {
            _countdownRunning      = false;
            CountdownBtn.IsEnabled = true;
            RefreshBtn.IsEnabled   = true;
        }
    }

    // ─── selection → candidate dropdown ───────────────────────────────

    private void WindowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = WindowsGrid.SelectedItem as WindowInfo;
        if (row is null)
        {
            SelectionLabel.Text     = "選択中: （未選択）";
            CandidateCombo.ItemsSource = null;
            CandidateCombo.Text     = "";
            return;
        }

        SelectionLabel.Text = $"選択中: {row.Title}";

        var candidates = WindowTitleSplitter.GetCandidates(row.Title);
        CandidateCombo.ItemsSource = candidates;
        CandidateCombo.Text        = candidates.Count > 0 ? candidates[0] : row.Title;
    }

    private void WindowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (WindowsGrid.SelectedItem is WindowInfo)
            CommitOk();
    }

    // ─── OK / Cancel ──────────────────────────────────────────────────

    private void OkBtn_Click(object sender, RoutedEventArgs e) => CommitOk();

    private void CommitOk()
    {
        var text = CandidateCombo.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            MessageBox.Show(this,
                "挿入する文字列が空です。一覧から行を選択するか、挿入文字列欄に直接入力してください。",
                "ウィンドウを選択",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        PickedText   = text;
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
