using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.Views;

/// <summary>
/// modules/extended/pianist/pianist_list.csv の編集ダイアログ。
///
/// pianist_list.csv は全 profile 横断のカタログで、特定 profile に紐付かないため
/// 専用ダイアログとして profile editor のシェルから独立して開く。
///
/// UX:
/// - 行追加 ComboBox: profiles/ に実在しかつ pianist_list.csv に未登録の profile 名のみ表示
///   （タイポ事故防止のため ProfileName は手入力させない）
/// - 行は Enabled / Group / Description / Segment が編集可、ProfileName は読み取り専用
/// - 保存時に重複 ProfileName / 実在しない ProfileName を検証してエラー表示
/// </summary>
public partial class PianistListEditDialog : Window
{
    private readonly IPianistProfileService _service;

    public ObservableCollection<PianistListEntry> Entries           { get; } = new();
    public ObservableCollection<string>           AvailableProfiles { get; } = new();

    /// <summary>profiles/ に実在する profile 名のキャッシュ。検証 / 候補生成に使う。</summary>
    private List<string> _allProfileNames = new();

    private PianistListEditDialog(IPianistProfileService service)
    {
        InitializeComponent();
        _service    = service;
        DataContext = this;
        Loaded     += async (_, _) => await LoadAsync();
    }

    /// <summary>ダイアログを開く。true = 保存して閉じた / false = キャンセル。</summary>
    public static bool Show(IPianistProfileService service, Window? owner = null)
    {
        var dlg = new PianistListEditDialog(service)
        {
            Owner = owner ?? Application.Current.MainWindow,
        };
        return dlg.ShowDialog() == true;
    }

    private async Task LoadAsync()
    {
        try
        {
            var list = await _service.LoadPianistListAsync();
            foreach (var e in list) Entries.Add(e);

            var profiles = await _service.GetProfilesAsync();
            _allProfileNames = profiles.Select(p => p.Name).ToList();

            RefreshAvailableProfiles();
            // 行 Add/Remove に追従して候補リストを更新
            Entries.CollectionChanged += (_, _) => RefreshAvailableProfiles();

            // ロード時点での orphan 警告（profiles/ に存在しない ProfileName が含まれている場合）
            ShowOrphanWarningIfAny();
        }
        catch (Exception ex)
        {
            ShowWarn($"読み込みエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// profiles/ に実在 + Entries にまだ無い profile 名を AvailableProfiles に同期する。
    /// 共有 ItemsSource の Reset 通知が ComboBox の SelectedItem を null 化するのを避けるため
    /// Clear+Add ではなく差分マージで更新する（feedback_combobox_itemssource ルール）。
    /// </summary>
    private void RefreshAvailableProfiles()
    {
        var used    = Entries.Select(e => e.ProfileName).ToHashSet(StringComparer.Ordinal);
        var desired = _allProfileNames.Where(n => !used.Contains(n)).ToList();

        for (int i = AvailableProfiles.Count - 1; i >= 0; i--)
            if (!desired.Contains(AvailableProfiles[i]))
                AvailableProfiles.RemoveAt(i);

        foreach (var n in desired)
            if (!AvailableProfiles.Contains(n))
                AvailableProfiles.Add(n);

        EmptyHint.Visibility = AvailableProfiles.Count == 0 && _allProfileNames.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowOrphanWarningIfAny()
    {
        var orphans = Entries
            .Select(e => e.ProfileName)
            .Where(n => !string.IsNullOrEmpty(n) && !_allProfileNames.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (orphans.Count > 0)
            ShowWarn($"⚠ profiles/ に存在しない ProfileName が含まれています: {string.Join(", ", orphans)}\n保存前に該当行を削除するか、profile を作成してください。");
    }

    private void ShowWarn(string message)
    {
        WarnText.Text     = message;
        WarnBar.Visibility = Visibility.Visible;
    }

    private void HideWarn()
    {
        WarnBar.Visibility = Visibility.Collapsed;
    }

    // ─── 行追加 / 削除 ────────────────────────────────────────────

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        Entries.Add(new PianistListEntry
        {
            Enabled     = true,
            ProfileName = name,
            Group       = "",
            Description = "",
            // Segment 初期値は ProfileName と同じ（segment フィルタを使うときの直感的な既定）
            Segment     = name,
        });
        // SelectedItem は AvailableProfiles から自動的に消えるので明示的な解除不要
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not PianistListEntry entry) return;

        var ok = MessageBox.Show(this,
            $"プロファイル「{entry.ProfileName}」を pianist_list.csv から削除しますか？\n（profiles/ 配下の profile 自体は削除されません）",
            "行削除の確認",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        Entries.Remove(entry);
    }

    // ─── 保存 / キャンセル ────────────────────────────────────────

    private async void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        HideWarn();

        // 重複チェック
        var dupes = Entries
            .GroupBy(x => x.ProfileName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupes.Count > 0)
        {
            ShowWarn($"⚠ ProfileName が重複: {string.Join(", ", dupes)}");
            return;
        }

        // 実在チェック
        var orphans = Entries
            .Where(x => !_allProfileNames.Contains(x.ProfileName))
            .Select(x => x.ProfileName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (orphans.Count > 0)
        {
            ShowWarn($"⚠ profiles/ に存在しない ProfileName を保存できません: {string.Join(", ", orphans)}");
            return;
        }

        OkBtn.IsEnabled     = false;
        CancelBtn.IsEnabled = false;
        try
        {
            var error = await _service.SavePianistListAsync(Entries);
            if (error is not null)
            {
                MessageBox.Show(this, error, "保存エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;
        }
        finally
        {
            OkBtn.IsEnabled     = true;
            CancelBtn.IsEnabled = true;
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
