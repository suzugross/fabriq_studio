using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Master;

namespace FabriqStudio.ViewModels;

/// <summary>関連付け 1 行の VM。アプリの候補は識別子ごとに遅延生成する。</summary>
public sealed partial class AppAssocEntryViewModel : ObservableObject
{
    private readonly Func<string, IReadOnlyList<AppAssocCandidate>> _candidateSource;
    private readonly Action _onChanged;
    private IReadOnlyList<AppAssocCandidate>? _candidates;
    private bool _applying;

    public AppAssocEntryViewModel(AppAssocEntry entry, Func<string, IReadOnlyList<AppAssocCandidate>> candidateSource, Action onChanged)
    {
        _candidateSource = candidateSource;
        _onChanged       = onChanged;
        _identifier      = entry.Identifier;
        _progId          = entry.ProgId;
        _applicationName = entry.ApplicationName;
    }

    [ObservableProperty] private string _identifier;
    [ObservableProperty] private string _progId;
    [ObservableProperty] private string _applicationName;

    /// <summary>この識別子を扱えるアプリの候補（辞書 / XML / この PC）。</summary>
    public IReadOnlyList<AppAssocCandidate> Candidates => _candidates ??= _candidateSource(Identifier);

    [ObservableProperty] private AppAssocCandidate? _selectedCandidate;

    partial void OnSelectedCandidateChanged(AppAssocCandidate? value)
    {
        if (value is null || _applying) return;
        _applying = true;
        ProgId          = value.ProgId;
        ApplicationName = value.AppName;
        _applying = false;
    }

    partial void OnProgIdChanged(string value)          { if (!_applying) _onChanged(); }
    partial void OnApplicationNameChanged(string value) { if (!_applying) _onChanged(); }

    /// <summary>候補を（一括変更などで）外から適用する。</summary>
    public void Apply(string progId, string appName)
    {
        _applying = true;
        ProgId          = progId;
        ApplicationName = appName;
        SelectedCandidate = Candidates.FirstOrDefault(c => c.ProgId.Equals(progId, StringComparison.OrdinalIgnoreCase));
        _applying = false;
        _onChanged();
    }

    /// <summary>候補の再計算（XML を読み直したとき）。</summary>
    public void InvalidateCandidates()
    {
        _candidates = null;
        OnPropertyChanged(nameof(Candidates));
    }

    public AppAssocEntry ToEntry() => new()
    {
        Identifier      = Identifier.Trim(),
        ProgId          = ProgId.Trim(),
        ApplicationName = ApplicationName.Trim(),
    };
}

/// <summary>主要カテゴリ（ブラウザー / PDF / メール …）の一括差し替え 1 行。</summary>
public sealed partial class AppAssocCategoryViewModel : ObservableObject
{
    private readonly Action<AppAssocCategoryViewModel> _apply;

    public AppAssocCategoryViewModel(AppAssocCategory category, Action<AppAssocCategoryViewModel> apply)
    {
        Category = category;
        _apply   = apply;
    }

    public AppAssocCategory Category { get; }
    public string Label => Category.Label;

    public ObservableCollection<string> Apps { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string? _selectedApp;

    private bool CanApply() => !string.IsNullOrWhiteSpace(SelectedApp);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => _apply(this);
}

/// <summary>
/// 既定のアプリ関連付け（AppAssoc.xml）の編集ダイアログ VM。
/// ベース XML（ワークスペース / 同梱ひな形 / 任意のファイル / この PC のエクスポート）を読み込み、
/// 行ごとの差し替えとカテゴリ一括差し替えを行い、対象パスへ保存する。エントリの削除はさせない
/// （エントリが欠けると初回サインイン時に「既定のアプリがリセットされました」通知が出るため）。
/// </summary>
public sealed partial class AppAssocEditorViewModel : ObservableObject
{
    private readonly IAppAssocService _service;
    private bool _loading;

    public string TargetPath { get; }
    public string TargetRelLabel { get; }

    public ObservableCollection<AppAssocEntryViewModel>    Entries    { get; } = [];
    public ObservableCollection<AppAssocCategoryViewModel> Categories { get; } = [];
    public ICollectionView View { get; }

    [ObservableProperty] private string  _filterText = "";
    [ObservableProperty] private string  _sourceInfo = "";
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool    _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasEntries;

    public bool   TargetExists => File.Exists(TargetPath);
    public string CountText    => Entries.Count == 0 ? "" : $"{Entries.Count} 件";

    /// <summary>true = 保存して閉じる、false = キャンセル。</summary>
    public event Action<bool>? RequestClose;

    public AppAssocEditorViewModel(IAppAssocService service, string targetPath, string? targetRelLabel = null)
    {
        _service       = service;
        TargetPath     = targetPath;
        TargetRelLabel = targetRelLabel ?? targetPath;

        View = CollectionViewSource.GetDefaultView(Entries);
        View.Filter = o => o is AppAssocEntryViewModel e && MatchesFilter(e);

        foreach (var c in service.Categories)
            Categories.Add(new AppAssocCategoryViewModel(c, ApplyCategory));
    }

    /// <summary>ダイアログを開いたときの初期読み込み: ワークスペースの XML があればそれ、無ければ同梱ひな形。</summary>
    public void LoadInitial()
    {
        if (File.Exists(TargetPath)) LoadFile(TargetPath, "ワークスペースの XML", markDirty: false);
        else                         LoadFile(_service.BaseXmlPath, "同梱のひな形（Windows 11 24H2 の既定 + Chrome）", markDirty: true);
    }

    // ── 読み込み ─────────────────────────────────────────────────

    [RelayCommand]
    private void LoadWorkspace()
    {
        if (!File.Exists(TargetPath)) { ErrorMessage = "ワークスペースにまだ XML がありません。"; return; }
        LoadFile(TargetPath, "ワークスペースの XML", markDirty: false);
    }

    [RelayCommand]
    private void LoadBase() => LoadFile(_service.BaseXmlPath, "同梱のひな形（Windows 11 24H2 の既定 + Chrome）", markDirty: true);

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "既定のアプリの関連付け XML を選択",
            Filter = "XML (*.xml)|*.xml|すべて (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        LoadFile(dialog.FileName, Path.GetFileName(dialog.FileName), markDirty: true);
    }

    [RelayCommand]
    private async Task ExportFromPcAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var path = await _service.ExportFromThisPcAsync();
            if (path is null) { ErrorMessage = "エクスポートできませんでした（管理者権限の確認でキャンセルされたか、Dism が失敗）。"; return; }
            LoadFile(path, "この PC のエクスポート", markDirty: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"エクスポート失敗: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadFile(string path, string label, bool markDirty)
    {
        ErrorMessage = null;
        try
        {
            var doc = AppAssocDocument.Load(path);
            _loading = true;
            Entries.Clear();
            foreach (var e in doc.Entries)
                Entries.Add(new AppAssocEntryViewModel(e, CandidatesFor, OnEntryChanged));
            _loading = false;

            HasEntries = Entries.Count > 0;
            SourceInfo = $"{label}: {Entries.Count} 件";
            IsDirty    = markDirty;
            Status     = null;
            RebuildCategoryApps();
            OnPropertyChanged(nameof(CountText));
            View.Refresh();
        }
        catch (Exception ex)
        {
            _loading = false;
            ErrorMessage = $"読み込み失敗: {ex.Message}";
        }
    }

    private void OnEntryChanged()
    {
        if (_loading) return;
        IsDirty = true;
    }

    // ── 候補 ─────────────────────────────────────────────────────

    /// <summary>識別子の候補: 辞書 → XML 内の同じ識別子 → この PC。ProgId で重複除去。</summary>
    private IReadOnlyList<AppAssocCandidate> CandidatesFor(string identifier)
    {
        var list = new List<AppAssocCandidate>();
        void Add(AppAssocCandidate c)
        {
            if (c.ProgId.Length == 0) return;
            if (list.Any(x => x.ProgId.Equals(c.ProgId, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(c);
        }

        foreach (var app in _service.Apps)
            if (app.ProgIds.TryGetValue(identifier, out var progId))
                Add(new AppAssocCandidate { AppName = app.Name, ProgId = progId, Source = "辞書" });

        foreach (var e in Entries.Where(e => e.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
            Add(new AppAssocCandidate { AppName = e.ApplicationName, ProgId = e.ProgId, Source = "XML" });

        foreach (var c in _service.LocalCandidates(identifier))
            Add(c);

        return list;
    }

    /// <summary>アプリ名 + 識別子から ProgId を引く（辞書 → XML 内 → この PC）。</summary>
    private string? ResolveProgId(string appName, string identifier)
    {
        var app = _service.Apps.FirstOrDefault(a => a.Name.Equals(appName, StringComparison.OrdinalIgnoreCase));
        if (app is not null && app.ProgIds.TryGetValue(identifier, out var fromDict)) return fromDict;

        var fromXml = Entries.FirstOrDefault(e =>
            e.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase) &&
            e.ApplicationName.Equals(appName, StringComparison.OrdinalIgnoreCase));
        if (fromXml is not null && fromXml.ProgId.Length > 0) return fromXml.ProgId;

        var local = _service.LocalCandidates(identifier)
            .FirstOrDefault(c => c.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase));
        return local?.ProgId;
    }

    private void RebuildCategoryApps()
    {
        foreach (var cat in Categories)
        {
            var keep  = cat.SelectedApp;
            var names = new List<string>();
            void Add(string n)
            {
                if (string.IsNullOrWhiteSpace(n)) return;
                if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
            }

            foreach (var app in _service.Apps)
                if (cat.Category.Identifiers.Any(id => app.ProgIds.ContainsKey(id))) Add(app.Name);

            foreach (var e in Entries)
                if (cat.Category.Identifiers.Contains(e.Identifier, StringComparer.OrdinalIgnoreCase)) Add(e.ApplicationName);

            foreach (var id in cat.Category.Identifiers)
                foreach (var c in _service.LocalCandidates(id)) Add(c.AppName);

            cat.Apps.Clear();
            foreach (var n in names) cat.Apps.Add(n);
            cat.SelectedApp = keep is not null && cat.Apps.Contains(keep) ? keep : null;
        }
    }

    /// <summary>カテゴリ内の識別子を、選んだアプリの ProgId に差し替える（ProgId が分からない識別子は変えない）。</summary>
    private void ApplyCategory(AppAssocCategoryViewModel cat)
    {
        var app = cat.SelectedApp;
        if (string.IsNullOrWhiteSpace(app)) return;

        var changed = 0;
        var same    = 0;
        var unknown = new List<string>();
        foreach (var e in Entries.Where(e => cat.Category.Identifiers.Contains(e.Identifier, StringComparer.OrdinalIgnoreCase)))
        {
            var progId = ResolveProgId(app, e.Identifier);
            if (progId is null) { unknown.Add(e.Identifier); continue; }
            if (e.ProgId.Equals(progId, StringComparison.OrdinalIgnoreCase) &&
                e.ApplicationName.Equals(app, StringComparison.OrdinalIgnoreCase)) { same++; continue; }
            e.Apply(progId, app);
            changed++;
        }

        Status = $"{cat.Label} → {app}: {changed} 件を変更"
                 + (same > 0 ? $"、{same} 件は既に同じ" : "")
                 + (unknown.Count > 0 ? $"、{unknown.Count} 件は {app} の ProgId が不明で未変更（{string.Join(" ", unknown.Take(6))}{(unknown.Count > 6 ? " …" : "")}）" : "");
        if (changed > 0) IsDirty = true;
    }

    // ── フィルタ ─────────────────────────────────────────────────

    partial void OnFilterTextChanged(string value) => View.Refresh();

    private bool MatchesFilter(AppAssocEntryViewModel e)
    {
        var q = FilterText.Trim();
        if (q.Length == 0) return true;
        return e.Identifier.Contains(q, StringComparison.OrdinalIgnoreCase)
               || e.ApplicationName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || e.ProgId.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ── 保存 ─────────────────────────────────────────────────────

    private bool CanSave() => HasEntries;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        ErrorMessage = null;
        var empty = Entries.Count(e => e.ProgId.Trim().Length == 0);
        if (empty > 0)
        {
            ErrorMessage = $"ProgId が空の行が {empty} 件あります。候補から選ぶか入力してください。";
            return;
        }
        try
        {
            var doc = new AppAssocDocument();
            foreach (var e in Entries) doc.Entries.Add(e.ToEntry());
            doc.Save(TargetPath);
            IsDirty = false;
            Saved   = true;
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失敗: {ex.Message}";
        }
    }

    public bool Saved { get; private set; }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
