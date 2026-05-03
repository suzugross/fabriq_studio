using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Helpers;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;
using FabriqStudio.Views;

namespace FabriqStudio.ViewModels;

/// <summary>
/// Pianist Profile Editor のシェル ViewModel（Phase 2）。
///
/// 左ペインで <c>modules/extended/pianist/profiles/</c> 配下のプロファイルを一覧し、
/// 選択行に対して <see cref="IPianistProfileService.LoadProfileAsync"/> でデータを取得して
/// 右ペインに表示する。Phase 2 では右ペインは読み取り専用のメタ情報サマリのみ。
/// メタ編集 / Phase 一覧 / 変数 grid / shortcuts / 暗号化トグルは Phase 3 以降で順次追加する。
///
/// Singleton 登録: ワークスペースを切り替えても VM 自体は再構築せず、
/// <see cref="IWorkspaceService.WorkspaceChanged"/> を購読して内部状態を refresh する。
/// </summary>
public partial class PianistProfileEditorViewModel : ObservableObject, IDirtyAwareViewModel
{
    // ─── IDirtyAwareViewModel ───────────────────────────────────────
    public bool HasUnsavedChanges => IsDirty;
    public string DirtyDescription => SelectedProfile is not null
        ? $"Pianist Profile: {SelectedProfile.Name}"
        : "Pianist Profile";

    /// <summary>
    /// 現在のプロファイルをディスクから再読み込みして in-memory 編集を破棄する。
    /// 同期的に IsDirty=false にしてからバックグラウンドで再読込する（fire-and-forget）。
    /// </summary>
    public void DiscardChanges()
    {
        IsDirty = false;
        if (SelectedProfile is not null)
            _ = LoadCurrentProfileAsync(SelectedProfile);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    /// <summary>ロード完了までは Dirty 検知を全面抑制する。</summary>
    private bool _isInitializing;

    /// <summary>Phase 切替時の <see cref="CurrentInstructions"/> 再代入など、
    /// プログラム的な書込みで一時的に Dirty 検知を抑制するためのフラグ。</summary>
    private bool _suppressDirty;

    /// <summary>左ペインのプロファイル切替時、未保存破棄ガードのために前回確定値を保持。</summary>
    private PianistProfileEntry? _confirmedProfile;

    /// <summary>ガードでのロールバック中にハンドラ再入を抑止するフラグ。</summary>
    private bool _ignoreSelectedProfileChange;

    private readonly IPianistProfileService _pianistService;
    private readonly IWorkspaceService      _workspace;
    private readonly ICsvService            _csvService;
    private readonly ICryptoService         _crypto;
    private readonly IPianistTestRunService _testRunService;

    /// <summary>テスト実行中のキャンセル用 CTS（ユーザーが「中止」ボタンを押したときに発火）。</summary>
    private CancellationTokenSource? _testRunCts;

    [ObservableProperty] private ObservableCollection<PianistProfileEntry> _profiles = new();

    [ObservableProperty] private PianistProfileEntry? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentData))]
    private PianistProfileData? _currentData;

    /// <summary>右ペインの「サマリ」と「空状態」の表示分岐に使う派生プロパティ。</summary>
    public bool HasCurrentData => CurrentData is not null;

    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _loadError;

    /// <summary>選択中の profile が旧 long format の values.csv を持っていたか（§5.5）。</summary>
    [ObservableProperty] private bool _isLegacyValuesDetected;

    // ─── Phase 一覧タブ ───────────────────────────────────────────
    /// <summary>procedure.csv の Step 群を PhaseID で集約したサマリ一覧（左ペイン用）。</summary>
    [ObservableProperty] private ObservableCollection<PianistPhaseSummary> _phases = new();

    [ObservableProperty] private PianistPhaseSummary? _selectedPhase;

    /// <summary>
    /// Phase 詳細ペインの DataGrid に流す Step フィルタビュー。
    /// CurrentData.Steps を新しい <see cref="CollectionViewSource"/> でラップし、
    /// <see cref="SelectedPhase"/> の PhaseID で行フィルタする。
    /// </summary>
    [ObservableProperty] private ICollectionView? _currentPhaseStepsView;

    // ─── instructions/<PhaseID>.txt の 4 section（pianist v1.4.0 DSL） ──────────────
    //
    // 内部表現: CurrentData.Instructions[PhaseID] には raw text を保持し、Phase 切替時に
    // PianistInstructionParser でパースして以下のプロパティへ流し込む。
    // 各プロパティ変更時に再シリアライズ → raw text を Dictionary に書き戻す方式。
    // これにより Service / 永続化レイヤは触らずに UI だけ 4 sub-tab 化できる。

    /// <summary>[RPA] section の本文。</summary>
    [ObservableProperty] private string _currentRpa = "";

    /// <summary>[Manual] section の本文。</summary>
    [ObservableProperty] private string _currentManual = "";

    /// <summary>
    /// Variables sub-tab のメインリスト。values.csv の VariableColumns から構築され、
    /// 各エントリは「[Variables] section に含めるか」のチェックボックスを持つ。
    /// values.csv が変数の絶対参照源 — pianist の Expand-Variables は ValuesDict のみ
    /// から resolve するため、[Variables] に書ける変数は values.csv 列が前提。
    /// </summary>
    [ObservableProperty] private ObservableCollection<PianistVariableSelection> _currentVariableSelections = new();

    /// <summary>
    /// [Variables] section に書かれているが values.csv に対応する列が存在しない「孤児」エントリ。
    /// タイポか values.csv の列削除残骸を示す。orphan セクションで赤背景 + 削除ボタンを出す。
    /// </summary>
    [ObservableProperty] private ObservableCollection<PianistVariableSelection> _orphanVariableSelections = new();

    /// <summary>
    /// [Samples] section のエントリ列（編集対象）。<see cref="PianistSampleEntry.File"/> は
    /// &lt;profile&gt;/screenshots/ 配下のファイル名のみ。
    /// </summary>
    [ObservableProperty] private ObservableCollection<PianistSampleEntry> _currentSamples = new();

    /// <summary>
    /// 現在 Phase の instructions ファイルが legacy（marker なし）形式だったか。
    /// true の場合 Phase 詳細ヘッダに「保存時に新形式へ変換されます」案内バナーを出す。
    /// </summary>
    [ObservableProperty] private bool _currentInstructionsWasLegacy;

    // ─── 変数タブ（values.csv） ────────────────────────────────
    /// <summary>hostlist.csv から読み出した全 NewPCName（重複除去 + 空除去）。</summary>
    private List<string> _allHostNames = new();

    /// <summary>
    /// 行追加用 ComboBox の候補（hostlist の NewPCName から、既に values.csv に
    /// 居るものを除外したもの）。§5.2.A.2 の auto-memory ルールに従い
    /// Clear+Add ではなく差分マージで更新する。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableHostNames))]
    [NotifyPropertyChangedFor(nameof(HostListEmpty))]
    [NotifyPropertyChangedFor(nameof(HostListAllConsumed))]
    [NotifyCanExecuteChangedFor(nameof(AddRowCommand))]
    private ObservableCollection<string> _availableNewPCNames = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRowCommand))]
    private string? _selectedNewPCName;

    public bool HasAvailableHostNames  => AvailableNewPCNames.Count > 0;
    /// <summary>hostlist.csv 自体に NewPCName が 0 件（§5.2.A.1 ケース 1）。</summary>
    public bool HostListEmpty          => _allHostNames.Count == 0;
    /// <summary>hostlist には居るが全て values.csv に追加済み（§5.2.A.1 ケース 2）。</summary>
    public bool HostListAllConsumed    => _allHostNames.Count > 0 && AvailableNewPCNames.Count == 0;

    // ─── 保存 ───────────────────────────────────────────────────
    /// <summary>保存中フラグ（保存ボタンの多重押し抑止）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;

    // ─── プレビュー: バリデーション結果 ─────────────────────────
    /// <summary>整合性チェック結果（プレビュータブで表示）。</summary>
    [ObservableProperty] private ObservableCollection<PianistValidationIssue> _validationIssues = new();

    /// <summary>整合性チェックを実行済みか（未実行のときの空状態切替に使用）。</summary>
    [ObservableProperty] private bool _hasRunValidation;

    // ─── テスト実行 ─────────────────────────────────────────────
    /// <summary>テスト実行中フラグ（ボタン disable / View ヘッダの状態表示に使う）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelTestRunCommand))]
    private bool _isTestRunning;

    /// <summary>テスト実行のステータス文字列（ヘッダに薄字で表示）。</summary>
    [ObservableProperty] private string? _testRunStatus;

    public PianistProfileEditorViewModel(
        IPianistProfileService pianistService,
        IWorkspaceService      workspace,
        ICsvService            csvService,
        ICryptoService         crypto,
        IPianistTestRunService testRunService)
    {
        _pianistService = pianistService;
        _workspace      = workspace;
        _csvService     = csvService;
        _crypto         = crypto;
        _testRunService = testRunService;

        _workspace.WorkspaceChanged += (_, e) =>
        {
            if (e.NewPath is null) { ClearAll(); return; }
            _ = LoadProfilesAsync();
            _ = LoadHostListAsync();
        };

        if (_workspace.IsOpen)
        {
            _ = LoadProfilesAsync();
            _ = LoadHostListAsync();
        }
    }

    /// <summary>新規プロファイル名のバリデータ（ダイアログから呼び出す）。</summary>
    public string? ValidateNewProfileName(string name)
        => _pianistService.ValidateNewProfileName(name);

    /// <summary>
    /// pianist_list.csv 編集ダイアログを開く（左ペインヘッダの「📋 一覧」ボタン）。
    /// 全 profile 横断のカタログで、特定 profile に紐付かないため独立ダイアログとして提供。
    /// </summary>
    [RelayCommand]
    private void EditPianistList()
    {
        if (!_workspace.IsOpen)
        {
            MessageBox.Show("ワークスペースが開かれていません。", "pianist_list.csv 編集",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        PianistListEditDialog.Show(_pianistService);
    }

    /// <summary>
    /// プロファイル新規作成ダイアログを開き、作成後にリストへ追加 + 自動選択する。
    /// 既存の <see cref="WelcomeViewModel.CreateNewWorkspaceAsync"/> と同じく VM がダイアログを
    /// 直接呼ぶパターン（fabriq studio 内で一貫している）。
    /// </summary>
    [RelayCommand]
    private async Task CreateNewProfileAsync()
    {
        if (!_workspace.IsOpen)
        {
            MessageBox.Show("ワークスペースが開かれていません。", "Pianist Profile 新規作成",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = PianistNewProfileDialog.Show(ValidateNewProfileName);
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            await _pianistService.CreateNewProfileAsync(name);
            await LoadProfilesAsync();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"作成エラー: {ex.Message}", "Pianist Profile 新規作成",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 指定 Profile を物理削除する（左ペイン ContextMenu「🗑 プロファイル削除」から呼ばれる）。
    ///
    /// フロー:
    /// 1. Dirty 状態の警告（編集中の Profile を消す場合は念押し）
    /// 2. 削除内容サマリ（フォルダパス + instructions/screenshots ファイル数）を確認ダイアログで表示
    /// 3. 確定 → pianist_list.csv 内の該当 ProfileName 行があれば「catalog 行も削除？」追加確認
    /// 4. フォルダ削除実行 → 必要なら catalog 保存 → Profiles 再ロード + SelectedProfile クリア
    /// </summary>
    [RelayCommand]
    private async Task DeleteProfileAsync(PianistProfileEntry? entry)
    {
        if (entry is null) return;
        if (!_workspace.IsOpen) return;

        // ── 1. Dirty 警告（削除対象が現在編集中の Profile の場合）──────
        var isDeletingCurrent = SelectedProfile is not null
            && string.Equals(SelectedProfile.Name, entry.Name, StringComparison.Ordinal);
        if (isDeletingCurrent && IsDirty)
        {
            var keep = MessageBox.Show(
                $"「{entry.Name}」に未保存の編集があります。\n削除すると編集内容も失われます。続行しますか？",
                "未保存の変更",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (keep != MessageBoxResult.OK) return;
        }

        // ── 2. 削除内容サマリ + 確認ダイアログ ──────────────────────────
        var (instrCount, sampleCount, otherCount) = SummarizeProfileFolder(entry.FolderPath);
        var summary =
            $"プロファイル「{entry.Name}」を削除しますか？\n\n" +
            $"削除対象:\n  {entry.FolderPath}\n\n" +
            $"内訳:\n" +
            $"  instructions/ … {instrCount} ファイル\n" +
            $"  screenshots/  … {sampleCount} ファイル\n" +
            $"  その他        … {otherCount} ファイル\n\n" +
            "⚠ この操作は取り消せません。";

        var confirm = MessageBox.Show(summary, "プロファイル削除の確認",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        // ── 3. catalog (pianist_list.csv) 連動確認 ─────────────────────
        bool removeFromCatalog = false;
        IReadOnlyList<PianistListEntry> catalog = Array.Empty<PianistListEntry>();
        try
        {
            catalog = await _pianistService.LoadPianistListAsync();
        }
        catch { /* catalog 読めなくても profile 削除は続行 */ }

        var matchingCatalogRows = catalog
            .Where(c => string.Equals(c.ProfileName, entry.Name, StringComparison.Ordinal))
            .ToList();
        if (matchingCatalogRows.Count > 0)
        {
            var ans = MessageBox.Show(
                $"pianist_list.csv に「{entry.Name}」が {matchingCatalogRows.Count} 行登録されています。\n\n" +
                "「はい」: catalog からも同時削除（推奨：fabriq 実行時の Validate Error を防ぐ）\n" +
                "「いいえ」: catalog はそのまま（後で手動更新）\n" +
                "「キャンセル」: 削除を中止",
                "pianist_list.csv 連動確認",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ans == MessageBoxResult.Cancel) return;
            removeFromCatalog = ans == MessageBoxResult.Yes;
        }

        // ── 4. フォルダ削除実行 ────────────────────────────────────────
        var error = await _pianistService.DeleteProfileAsync(entry);
        if (error is not null)
        {
            MessageBox.Show($"プロファイル削除に失敗しました:\n\n{error}", "プロファイル削除",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // catalog から該当行を削除して保存
        if (removeFromCatalog && catalog.Count > 0)
        {
            var newCatalog = catalog
                .Where(c => !string.Equals(c.ProfileName, entry.Name, StringComparison.Ordinal))
                .ToList();
            var saveErr = await _pianistService.SavePianistListAsync(newCatalog);
            if (saveErr is not null)
            {
                MessageBox.Show(
                    $"profile フォルダは削除しましたが pianist_list.csv の更新に失敗:\n\n{saveErr}",
                    "pianist_list.csv 更新",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── 5. リスト再構築 + 選択解除 ─────────────────────────────
        if (isDeletingCurrent)
        {
            _confirmedProfile = null;
            IsDirty = false;
            SelectedProfile = null;   // CurrentData クリアは partial で連鎖
        }
        await LoadProfilesAsync();
    }

    /// <summary>
    /// プロファイルフォルダ内のファイルを 3 区分で集計（削除確認ダイアログ用）。
    /// 失敗時は (0, 0, 0) を返す（削除自体は阻害しない）。
    /// </summary>
    private static (int instructions, int samples, int other) SummarizeProfileFolder(string folder)
    {
        try
        {
            if (!System.IO.Directory.Exists(folder)) return (0, 0, 0);
            int instr = 0, sam = 0, other = 0;
            var instrDir = System.IO.Path.Combine(folder, "instructions");
            var shotsDir = System.IO.Path.Combine(folder, "screenshots");
            if (System.IO.Directory.Exists(instrDir))
                instr = System.IO.Directory.EnumerateFiles(instrDir, "*", System.IO.SearchOption.AllDirectories).Count();
            if (System.IO.Directory.Exists(shotsDir))
                sam = System.IO.Directory.EnumerateFiles(shotsDir, "*", System.IO.SearchOption.AllDirectories).Count();
            // 直下のファイル + サブディレクトリ（instructions/screenshots 除く）
            other = System.IO.Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.TopDirectoryOnly).Count();
            return (instr, sam, other);
        }
        catch { return (0, 0, 0); }
    }

    /// <summary>profiles/ 配下を再スキャン。SelectedProfile は名前で復元する。</summary>
    private async Task LoadProfilesAsync()
    {
        IsLoading = true;
        LoadError = null;

        var preservedName = SelectedProfile?.Name;

        try
        {
            var items = await _pianistService.GetProfilesAsync();
            Profiles  = new ObservableCollection<PianistProfileEntry>(items);

            SelectedProfile = preservedName is null
                ? Profiles.FirstOrDefault()
                : Profiles.FirstOrDefault(p => p.Name == preservedName) ?? Profiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            LoadError = $"Pianist プロファイル一覧の読み込みに失敗: {ex.Message}";
            CurrentData = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedProfileChanged(PianistProfileEntry? value)
    {
        // ロールバック中は自分の再代入なのでスキップ
        if (_ignoreSelectedProfileChange) return;

        // 別 Profile（= 名前が異なる）への切替で、かつ未保存編集があれば確認
        // value=null は撤去操作（ワークスペース解除等）なのでガードしない
        if (IsDirty
            && _confirmedProfile is not null
            && value is not null
            && !string.Equals(_confirmedProfile.Name, value.Name, StringComparison.Ordinal))
        {
            var result = MessageBox.Show(
                $"「Pianist Profile: {_confirmedProfile.Name}」に未保存の変更があります。\n\n" +
                "別のプロファイルに切り替えると、変更内容は失われます。\n" +
                "破棄して切り替えますか？",
                "未保存の変更",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                // キャンセル時のロールバックは Dispatcher.BeginInvoke で次サイクルに遅延させる。
                // 同期的に SelectedProfile を戻すと、ListBox 側のクリック処理がまだ進行中で
                // ListBoxItem.IsSelected がクリック先（B）に再アサートされ、見た目のハイライトが
                // 戻らない（編集画面は元のままなのに左ペインだけ移動先に張り付く）現象が起きる。
                var rollbackTo = _confirmedProfile;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        _ignoreSelectedProfileChange = true;
                        try { SelectedProfile = rollbackTo; }
                        finally { _ignoreSelectedProfileChange = false; }
                    }),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            // OK: そのまま破棄して新 Profile をロード
        }

        _confirmedProfile = value;

        if (value is null)
        {
            CurrentData = null;
            IsLegacyValuesDetected = false;
            return;
        }
        _ = LoadCurrentProfileAsync(value);
    }

    /// <summary>現在購読中の Values.Rows（CurrentData 切替時の unsubscribe 用）。</summary>
    private ObservableCollection<PianistValueRow>? _subscribedRows;
    /// <summary>現在購読中の VariableColumns（同上）。</summary>
    private ObservableCollection<string>? _subscribedColumns;

    private async Task LoadCurrentProfileAsync(PianistProfileEntry entry)
    {
        _isInitializing = true;
        IsLoading = true;
        LoadError = null;
        IsLegacyValuesDetected = false;

        // 旧データの購読を解除（profile 切替時のリーク防止）
        DetachDirtyTracking(CurrentData);
        if (_subscribedRows is not null)
        {
            _subscribedRows.CollectionChanged -= OnValueRowsChanged;
            _subscribedRows = null;
        }
        if (_subscribedColumns is not null)
        {
            _subscribedColumns.CollectionChanged -= OnVariableColumnsChanged;
            _subscribedColumns = null;
        }

        try
        {
            var data = await _pianistService.LoadProfileAsync(entry);
            CurrentData = data;
            IsLegacyValuesDetected = data.Values.WasLegacyFormat;
            RebuildPhases();
            // 行追加用候補を最新の使用状況で再計算（hostlist 自体は別ロード）
            RebuildAvailableHostNames();
            // $VarName サジェスト用候補を初回構築
            RebuildVariableReferences();
            // values.csv 行 / 変数列の変化に追従するために購読
            data.Values.Rows.CollectionChanged += OnValueRowsChanged;
            _subscribedRows = data.Values.Rows;
            data.Values.VariableColumns.CollectionChanged += OnVariableColumnsChanged;
            _subscribedColumns = data.Values.VariableColumns;

            // Metadata / Steps / Shortcuts と既存 Rows の Dirty 検知購読
            AttachDirtyTracking(data);

            IsDirty = false;
        }
        catch (Exception ex)
        {
            LoadError = $"プロファイル「{entry.Name}」の読み込みに失敗: {ex.Message}";
            CurrentData = null;
            ClearPhaseState();
            RebuildAvailableHostNames();
        }
        finally
        {
            IsLoading = false;
            _isInitializing = false;
        }
    }

    private void OnValueRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // 既存: 行追加・削除後の候補再計算
        RebuildAvailableHostNames();

        // 新規: 各行の PropertyChanged を購読/解除して Dirty 検知に組み込む
        if (e.NewItems is not null)
            foreach (PianistValueRow r in e.NewItems)
                r.PropertyChanged += OnDirtyTrigger;
        if (e.OldItems is not null)
            foreach (PianistValueRow r in e.OldItems)
                r.PropertyChanged -= OnDirtyTrigger;

        MarkDirty();
    }

    private void OnVariableColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RebuildVariableReferences();
        // values.csv の列構成が変わったら Variables sub-tab のチェックリストも再構築
        // （新列が追加 / 削除された場合に追従、既存 IsIncluded 状態は raw text 経由で復元される）
        DetachInstructionItemSubscriptions();
        _suppressDirty = true;
        try { RebuildVariableSelections(); }
        finally { _suppressDirty = false; }
        AttachInstructionItemSubscriptions();
        MarkDirty();
    }

    /// <summary>
    /// Pianist Profile 全データへの Dirty 検知購読を 1 箇所で一括設定する。
    /// values.csv の Rows / VariableColumns は <see cref="OnValueRowsChanged"/> /
    /// <see cref="OnVariableColumnsChanged"/> 側で別途扱うため、ここでは Metadata / Steps /
    /// Shortcuts と既存 Rows のアイテム購読のみを担当する。
    /// </summary>
    private void AttachDirtyTracking(PianistProfileData data)
    {
        data.Metadata.PropertyChanged       += OnDirtyTrigger;
        data.Steps.CollectionChanged        += OnStepsCollectionChanged;
        foreach (var step in data.Steps)
            step.PropertyChanged             += OnDirtyTrigger;
        foreach (var row in data.Values.Rows)
            row.PropertyChanged              += OnDirtyTrigger;
        data.Shortcuts.CollectionChanged    += OnShortcutsCollectionChanged;
        foreach (var sc in data.Shortcuts)
            sc.PropertyChanged               += OnDirtyTrigger;
    }

    private void DetachDirtyTracking(PianistProfileData? data)
    {
        if (data is null) return;
        data.Metadata.PropertyChanged       -= OnDirtyTrigger;
        data.Steps.CollectionChanged        -= OnStepsCollectionChanged;
        foreach (var step in data.Steps)
            step.PropertyChanged             -= OnDirtyTrigger;
        foreach (var row in data.Values.Rows)
            row.PropertyChanged              -= OnDirtyTrigger;
        data.Shortcuts.CollectionChanged    -= OnShortcutsCollectionChanged;
        foreach (var sc in data.Shortcuts)
            sc.PropertyChanged               -= OnDirtyTrigger;
    }

    private void OnStepsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PianistStep s in e.NewItems)
                s.PropertyChanged += OnDirtyTrigger;
        if (e.OldItems is not null)
            foreach (PianistStep s in e.OldItems)
                s.PropertyChanged -= OnDirtyTrigger;
        MarkDirty();
    }

    private void OnShortcutsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PianistShortcut s in e.NewItems)
                s.PropertyChanged += OnDirtyTrigger;
        if (e.OldItems is not null)
            foreach (PianistShortcut s in e.OldItems)
                s.PropertyChanged -= OnDirtyTrigger;
        MarkDirty();
    }

    private void OnDirtyTrigger(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => MarkDirty();

    /// <summary>初期化中・抑制フラグ中以外で IsDirty=true をセットする。</summary>
    private void MarkDirty()
    {
        if (_isInitializing || _suppressDirty) return;
        IsDirty = true;
    }

    private void ClearAll()
    {
        DetachDirtyTracking(CurrentData);
        Profiles.Clear();
        SelectedProfile = null;
        CurrentData = null;
        LoadError = null;
        IsLegacyValuesDetected = false;
        ClearPhaseState();
        _confirmedProfile = null;
        IsDirty           = false;
    }

    // ─── Phase 集約 ────────────────────────────────────────────────

    /// <summary>
    /// CurrentData.Steps を PhaseID で集約して <see cref="Phases"/> を再構築し、
    /// 先頭 Phase を選択状態にする。procedure.csv が空のときは未選択にする。
    /// </summary>
    private void RebuildPhases()
    {
        Phases.Clear();
        if (CurrentData is null)
        {
            ClearPhaseState();
            return;
        }

        // PhaseID 単位で集約。PhaseLabel / Color は最初に出現した Step の値を採用。
        // 出現順を保つため GroupBy 後の順序は CurrentData.Steps の登場順に依存する
        // （LINQ GroupBy はキー初出現順を保つ）。
        var groups = CurrentData.Steps
            .GroupBy(s => s.PhaseID, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                return new PianistPhaseSummary
                {
                    PhaseID    = g.Key,
                    PhaseLabel = first.PhaseLabel,
                    Color      = first.Color,
                    StepCount  = g.Count()
                };
            });

        foreach (var p in groups)
            Phases.Add(p);

        SelectedPhase = Phases.FirstOrDefault();
    }

    partial void OnSelectedPhaseChanged(PianistPhaseSummary? value)
    {
        // 旧 Phase の Variables/Samples 編集購読を解除（Phase 切替時のリーク防止）
        DetachInstructionItemSubscriptions();

        if (value is null || CurrentData is null)
        {
            CurrentPhaseStepsView = null;
            ResetCurrentInstructionState();
            return;
        }

        // 新しい CollectionViewSource を毎回作る（DefaultView 共有による副作用を避けるため）。
        var src = new CollectionViewSource { Source = CurrentData.Steps };
        var phaseId = value.PhaseID;
        src.View.Filter = obj =>
            obj is PianistStep s && string.Equals(s.PhaseID, phaseId, StringComparison.Ordinal);
        CurrentPhaseStepsView = src.View;

        // raw text → 4 section プロパティへ展開（プログラム的代入は Dirty 検知から除外）
        var rawText = CurrentData.Instructions.TryGetValue(value.PhaseID, out var t) ? t : "";
        LoadInstructionsForCurrentPhase(rawText);
    }

    /// <summary>
    /// raw instruction text を <see cref="PianistInstructionParser"/> でパースして
    /// 4 セクションのプロパティへ流し込む。Sample エントリの物理ファイル存在判定もここで行う。
    /// 内部で <see cref="_suppressDirty"/> を立てて流し込むので Dirty にならない。
    /// </summary>
    private void LoadInstructionsForCurrentPhase(string rawText)
    {
        var parsed = PianistInstructionParser.Parse(rawText);
        var screenshotsDir = CurrentData is not null
            ? System.IO.Path.Combine(CurrentData.Entry.FolderPath, "screenshots")
            : null;

        _suppressDirty = true;
        try
        {
            CurrentRpa    = parsed.RPA;
            CurrentManual = parsed.Manual;

            CurrentSamples.Clear();
            foreach (var s in parsed.Samples)
            {
                s.Exists = screenshotsDir is not null
                    && System.IO.File.Exists(System.IO.Path.Combine(screenshotsDir, s.File));
                CurrentSamples.Add(s);
            }

            CurrentInstructionsWasLegacy = parsed.WasLegacyFormat;

            RebuildVariableSelections(parsed.Variables);
            AttachInstructionItemSubscriptions();
        }
        finally { _suppressDirty = false; }
    }

    /// <summary>
    /// 4 セクションのプロパティ群を空にする（Phase 未選択 / プロファイル未ロード時）。
    /// </summary>
    private void ResetCurrentInstructionState()
    {
        _suppressDirty = true;
        try
        {
            CurrentRpa    = "";
            CurrentManual = "";
            CurrentVariableSelections.Clear();
            OrphanVariableSelections.Clear();
            CurrentSamples.Clear();
            CurrentInstructionsWasLegacy = false;
        }
        finally { _suppressDirty = false; }
    }

    /// <summary>RPA section テキスト変更 → 再シリアライズ + Dirty。</summary>
    partial void OnCurrentRpaChanged(string value) => RewriteCurrentInstructionsRaw();

    /// <summary>Manual section テキスト変更 → 再シリアライズ + Dirty。</summary>
    partial void OnCurrentManualChanged(string value) => RewriteCurrentInstructionsRaw();

    /// <summary>
    /// 4 セクションプロパティから raw text を再構築して
    /// <see cref="PianistProfileData.Instructions"/> 辞書へ書き戻し、Dirty を立てる。
    ///
    /// [Variables] 出力規則: <see cref="CurrentVariableSelections"/> +
    /// <see cref="OrphanVariableSelections"/> のうち <see cref="PianistVariableSelection.IsIncluded"/>
    /// が true なエントリの Name を 1 行 1 変数で書き出す。auto-discovered なら pianist 側で
    /// auto-union されるが、ユーザの明示チェック状態を尊重して round-trip を保つ。
    /// </summary>
    private void RewriteCurrentInstructionsRaw()
    {
        if (_isInitializing || _suppressDirty) return;
        if (CurrentData is null || SelectedPhase is null) return;

        var data = new PianistInstructionFile
        {
            RPA    = CurrentRpa ?? "",
            Manual = CurrentManual ?? "",
        };
        foreach (var v in CurrentVariableSelections)
            if (v.IsIncluded && !string.IsNullOrWhiteSpace(v.Name))
                data.Variables.Add(v.Name);
        foreach (var v in OrphanVariableSelections)
            if (v.IsIncluded && !string.IsNullOrWhiteSpace(v.Name))
                data.Variables.Add(v.Name);
        foreach (var s in CurrentSamples)
            data.Samples.Add(s);

        CurrentData.Instructions[SelectedPhase.PhaseID] = PianistInstructionParser.Serialize(data);
        MarkDirty();
    }

    // ─── 4 section プロパティ内のアイテム編集購読 ────────────
    // CurrentVariables / CurrentSamples の各アイテム PropertyChanged を購読することで
    // 「変数名を 1 文字書き換えた」「キャプションを変更した」などをすべて Dirty + raw 再書込に流す。

    private void AttachInstructionItemSubscriptions()
    {
        CurrentVariableSelections.CollectionChanged += OnInstructionVariableSelectionsChanged;
        OrphanVariableSelections.CollectionChanged  += OnInstructionVariableSelectionsChanged;
        CurrentSamples.CollectionChanged            += OnInstructionSamplesChanged;
        foreach (var v in CurrentVariableSelections) v.PropertyChanged += OnInstructionItemPropChanged;
        foreach (var v in OrphanVariableSelections)  v.PropertyChanged += OnInstructionItemPropChanged;
        foreach (var s in CurrentSamples)            s.PropertyChanged += OnInstructionItemPropChanged;
    }

    private void DetachInstructionItemSubscriptions()
    {
        CurrentVariableSelections.CollectionChanged -= OnInstructionVariableSelectionsChanged;
        OrphanVariableSelections.CollectionChanged  -= OnInstructionVariableSelectionsChanged;
        CurrentSamples.CollectionChanged            -= OnInstructionSamplesChanged;
        foreach (var v in CurrentVariableSelections) v.PropertyChanged -= OnInstructionItemPropChanged;
        foreach (var v in OrphanVariableSelections)  v.PropertyChanged -= OnInstructionItemPropChanged;
        foreach (var s in CurrentSamples)            s.PropertyChanged -= OnInstructionItemPropChanged;
    }

    private void OnInstructionVariableSelectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PianistVariableSelection v in e.NewItems) v.PropertyChanged += OnInstructionItemPropChanged;
        if (e.OldItems is not null)
            foreach (PianistVariableSelection v in e.OldItems) v.PropertyChanged -= OnInstructionItemPropChanged;
        RewriteCurrentInstructionsRaw();
    }

    private void OnInstructionSamplesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PianistSampleEntry s in e.NewItems) s.PropertyChanged += OnInstructionItemPropChanged;
        if (e.OldItems is not null)
            foreach (PianistSampleEntry s in e.OldItems) s.PropertyChanged -= OnInstructionItemPropChanged;
        RewriteCurrentInstructionsRaw();
    }

    private void OnInstructionItemPropChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // PianistSampleEntry.Exists は Studio が物理判定で立てるだけのフラグなので Dirty にしない
        if (sender is PianistSampleEntry &&
            (e.PropertyName == nameof(PianistSampleEntry.Exists) ||
             e.PropertyName == nameof(PianistSampleEntry.IsMissing) ||
             e.PropertyName == nameof(PianistSampleEntry.DisplayName)))
            return;
        RewriteCurrentInstructionsRaw();
    }

    /// <summary>
    /// procedure.csv の Value/Note 列に出現する <c>$VarName</c> 走査用 regex（pianist の
    /// Get-PhaseReferencedVariables と同一）。
    /// </summary>
    private static readonly Regex VarRefScanRegex = new(@"\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>
    /// Variables sub-tab のチェックリストを 3 集合の union から再構築する:
    ///   1. values.csv の VariableColumns（絶対参照源 — メインリスト）
    ///   2. 同 Phase の procedure.csv $VarName 参照（auto-discovered フラグ）
    ///   3. パース済 [Variables] section の宣言（IsIncluded フラグ + orphan 判定）
    ///
    /// values.csv に存在する列 → CurrentVariableSelections に積む。IsIncluded は
    /// [Variables] に書かれていれば true、無ければ false。
    /// values.csv に存在しない [Variables] 宣言 → OrphanVariableSelections に分離。
    /// </summary>
    private void RebuildVariableSelections(IEnumerable<string>? explicitVariables = null)
    {
        CurrentVariableSelections.Clear();
        OrphanVariableSelections.Clear();
        if (CurrentData is null || SelectedPhase is null) return;

        // 1. values.csv 列名（絶対参照源）
        var valuesCols = CurrentData.Values.VariableColumns.ToList();
        var valuesColsSet = new HashSet<string>(valuesCols, StringComparer.Ordinal);

        // 2. procedure.csv 由来 auto-discovered（同 Phase のみ）
        var phaseId = SelectedPhase.PhaseID;
        var auto = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in CurrentData.Steps)
        {
            if (!string.Equals(step.PhaseID, phaseId, StringComparison.Ordinal)) continue;
            foreach (var field in new[] { step.Value ?? "", step.Note ?? "" })
                foreach (Match m in VarRefScanRegex.Matches(field))
                    auto.Add(m.Groups[1].Value);
        }

        // 3. [Variables] section の明示宣言（呼び出し元から渡される）
        // 渡されなかった場合は現在の raw text を再パースして取得（columns 変化時の rebuild 用）
        IEnumerable<string> explicit_ = explicitVariables ?? ReparseExplicitVariables();
        var explicitSet = new HashSet<string>(explicit_, StringComparer.Ordinal);

        // メインリスト: values.csv 列 × IsIncluded（[Variables] にある）× IsAutoDiscovered
        // sample value: `*` 行のセル値（先頭 60 文字）
        var starRow = CurrentData.Values.Star;
        foreach (var col in valuesCols)
        {
            var raw = starRow is not null ? starRow[col] : "";
            var preview = string.IsNullOrEmpty(raw) ? "" : (raw.Length > 60 ? raw.Substring(0, 60) + "..." : raw);
            CurrentVariableSelections.Add(new PianistVariableSelection
            {
                Name             = col,
                IsIncluded       = explicitSet.Contains(col),
                IsAutoDiscovered = auto.Contains(col),
                SampleValue      = preview,
                IsOrphan         = false,
            });
        }

        // Orphan: [Variables] にあるが values.csv に列が無い
        foreach (var name in explicit_)
        {
            if (valuesColsSet.Contains(name)) continue;
            OrphanVariableSelections.Add(new PianistVariableSelection
            {
                Name             = name,
                IsIncluded       = true,
                IsAutoDiscovered = auto.Contains(name),
                SampleValue      = "",
                IsOrphan         = true,
            });
        }
    }

    /// <summary>
    /// 現在 Phase の raw instructions を再パースして [Variables] section の宣言名のみ取り出す。
    /// values.csv の列追加 / 削除イベントで CurrentVariableSelections を再構築する際に
    /// 既存の IsIncluded 情報を失わないようにする補助。
    /// </summary>
    private IEnumerable<string> ReparseExplicitVariables()
    {
        if (CurrentData is null || SelectedPhase is null) return Array.Empty<string>();
        if (!CurrentData.Instructions.TryGetValue(SelectedPhase.PhaseID, out var raw)) return Array.Empty<string>();
        return PianistInstructionParser.Parse(raw).Variables;
    }

    private void ClearPhaseState()
    {
        Phases.Clear();
        SelectedPhase         = null;
        CurrentPhaseStepsView = null;
        DetachInstructionItemSubscriptions();
        ResetCurrentInstructionState();
    }

    // ─── 変数タブ（values.csv） ────────────────────────────────

    /// <summary>kernel/csv/hostlist.csv から NewPCName を全件読み出す。</summary>
    private async Task LoadHostListAsync()
    {
        if (!_workspace.IsOpen)
        {
            _allHostNames = new();
            RebuildAvailableHostNames();
            return;
        }
        try
        {
            var hosts = await _csvService.ReadAsync<HostEntry>("kernel/csv/hostlist.csv");
            _allHostNames = hosts
                .Select(h => h.NewPCName ?? "")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            // hostlist.csv が無い / 読めない場合は 0 件扱い（プレースホルダ UX へ流れる）
            _allHostNames = new();
        }
        RebuildAvailableHostNames();
    }

    /// <summary>
    /// hostlist の NewPCName から、現在の values.csv に存在する分を除外したリストを再計算。
    /// <see cref="AvailableNewPCNames"/> は <c>Clear+Add</c> せず差分マージで更新する
    /// （feedback_combobox_itemssource: 共有 ItemsSource の Reset 通知が編集中
    /// ComboBox の SelectedItem を null 化するのを避けるため）。
    /// </summary>
    private void RebuildAvailableHostNames()
    {
        var used = CurrentData?.Values.Rows
            .Where(r => !r.IsStar && !string.IsNullOrEmpty(r.NewPCName))
            .Select(r => r.NewPCName)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var desired = _allHostNames.Where(n => !used.Contains(n)).ToList();

        // 既存 → 不要分削除
        for (int i = AvailableNewPCNames.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(AvailableNewPCNames[i]))
                AvailableNewPCNames.RemoveAt(i);
        }
        // 不足分追加
        foreach (var n in desired)
        {
            if (!AvailableNewPCNames.Contains(n))
                AvailableNewPCNames.Add(n);
        }

        // 派生フラグの再評価通知（コレクション参照は変わっていないので明示的に発火）
        OnPropertyChanged(nameof(HasAvailableHostNames));
        OnPropertyChanged(nameof(HostListEmpty));
        OnPropertyChanged(nameof(HostListAllConsumed));
        AddRowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>選択された NewPCName で values.csv に新規行を追加（§5.2.A 厳格ドロップダウン）。</summary>
    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void AddRow()
    {
        if (CurrentData is null || string.IsNullOrEmpty(SelectedNewPCName)) return;

        var row = new PianistValueRow
        {
            NewPCName = SelectedNewPCName,
            Table     = CurrentData.Values,
        };
        foreach (var col in CurrentData.Values.VariableColumns)
            row.Cells[col] = "";

        CurrentData.Values.Rows.Add(row);

        SelectedNewPCName = null;
        // RebuildAvailableHostNames() は CollectionChanged → OnValueRowsChanged 経由で発火
    }

    private bool CanAddRow()
        => CurrentData is not null
           && !string.IsNullOrEmpty(SelectedNewPCName)
           && AvailableNewPCNames.Contains(SelectedNewPCName);

    /// <summary>選択した値行を削除する（`*` 行は不可）。</summary>
    [RelayCommand]
    private void DeleteRow(PianistValueRow? row)
    {
        if (row is null || row.IsStar || CurrentData is null) return;

        var ok = MessageBox.Show(
            $"行「{row.NewPCName}」を削除しますか？\nこの操作は保存するまで実ファイルには反映されません。",
            "行削除の確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        CurrentData.Values.Rows.Remove(row);
    }

    /// <summary>
    /// hostlist.csv 編集画面（端末一覧）への移動要求（§5.2.A.1 ケース 1）。
    /// MainViewModel が <see cref="NavigateBackMessage"/> を捕まえて HostListView へ切り替える。
    /// 未保存の編集状態の警告は Phase 5（保存実装）以降で導入する。
    /// </summary>
    [RelayCommand]
    private void NavigateToHostList()
    {
        WeakReferenceMessenger.Default.Send(new NavigateBackMessage("HostList"));
    }

    // ─── 暗号化／復号（HostDetail と同パターン: 右クリック ContextMenu） ───
    //
    // 各メソッドは null = 成功、非 null = エラーメッセージ（呼び出し側で MessageBox 表示）。
    // セル値の更新は <see cref="PianistValueRow.this[string]"/> インデクサ経由で行い、
    // Item[col] 通知が WPF binding に流れて自動で再描画される。

    private string? RequirePassphrase()
        => _crypto.HasPassphrase
            ? null
            : "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";

    /// <summary>このセルを暗号化（既に ENC: の場合 / 空セルはスキップ）。</summary>
    public string? EncryptCell(PianistValueRow row, string columnName)
    {
        var err = RequirePassphrase();
        if (err is not null) return err;

        var v = row[columnName];
        if (string.IsNullOrEmpty(v))                                  return "空のセルは暗号化できません。";
        if (v.StartsWith("ENC:", StringComparison.Ordinal))           return "このセルは既に暗号化されています。";

        row[columnName] = _crypto.Encrypt(v, _crypto.MasterPassphrase!);
        return null;
    }

    /// <summary>このセルを復号（ENC: でない場合はスキップ）。</summary>
    public string? DecryptCell(PianistValueRow row, string columnName)
    {
        var err = RequirePassphrase();
        if (err is not null) return err;

        var v = row[columnName];
        if (!v.StartsWith("ENC:", StringComparison.Ordinal))
            return "このセルは暗号化されていません（ENC: prefix がありません）。";

        try { row[columnName] = _crypto.Decrypt(v, _crypto.MasterPassphrase!); return null; }
        catch (Exception ex) { return $"復号に失敗しました: {ex.Message}"; }
    }

    /// <summary>指定列の全セルを一括暗号化（空 / 既 ENC: は自動スキップ）。</summary>
    public BatchCryptoResult? EncryptColumn(string columnName)
    {
        var err = RequirePassphrase();
        if (err is not null) return new BatchCryptoResult(0, 0, new[] { err });
        if (CurrentData is null) return null;

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var row in CurrentData.Values.Rows)
        {
            var v = row[columnName];
            if (string.IsNullOrEmpty(v))                          { skipped++; continue; }
            if (v.StartsWith("ENC:", StringComparison.Ordinal))   { skipped++; continue; }
            try { row[columnName] = _crypto.Encrypt(v, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"{row.NewPCName}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    /// <summary>指定列の全 ENC: セルを一括復号。</summary>
    public BatchCryptoResult? DecryptColumn(string columnName)
    {
        var err = RequirePassphrase();
        if (err is not null) return new BatchCryptoResult(0, 0, new[] { err });
        if (CurrentData is null) return null;

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var row in CurrentData.Values.Rows)
        {
            var v = row[columnName];
            if (!v.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
            try { row[columnName] = _crypto.Decrypt(v, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"{row.NewPCName}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    /// <summary>指定行の全変数セルを一括暗号化。</summary>
    public BatchCryptoResult? EncryptRow(PianistValueRow row)
    {
        var err = RequirePassphrase();
        if (err is not null) return new BatchCryptoResult(0, 0, new[] { err });
        if (CurrentData is null) return null;

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var col in CurrentData.Values.VariableColumns)
        {
            var v = row[col];
            if (string.IsNullOrEmpty(v))                          { skipped++; continue; }
            if (v.StartsWith("ENC:", StringComparison.Ordinal))   { skipped++; continue; }
            try { row[col] = _crypto.Encrypt(v, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"{col}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    /// <summary>指定行の全 ENC: セルを一括復号。</summary>
    public BatchCryptoResult? DecryptRow(PianistValueRow row)
    {
        var err = RequirePassphrase();
        if (err is not null) return new BatchCryptoResult(0, 0, new[] { err });
        if (CurrentData is null) return null;

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var col in CurrentData.Values.VariableColumns)
        {
            var v = row[col];
            if (!v.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
            try { row[col] = _crypto.Decrypt(v, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"{col}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    // ─── 変数列の操作（§5.2.E） ─────────────────────────────────

    /// <summary>変数列名の許容パターン。pianist.ps1 Expand-Variables の正規表現と同等（§5.3）。</summary>
    private static readonly Regex ColumnNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>列名のバリデーション。エラーメッセージ or null（OK）を返す。</summary>
    public string? ValidateColumnName(string name, string? renamingFrom = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "列名を入力してください。";
        if (!ColumnNamePattern.IsMatch(name))
            return "列名は半角英字 / アンダースコアで始まり、半角英数 / アンダースコアのみ使用できます。";
        if (string.Equals(name, "NewPCName", StringComparison.Ordinal))
            return "「NewPCName」は予約列名のため使用できません。";
        if (CurrentData is null)
            return "プロファイルが読み込まれていません。";

        // リネーム時は自分自身との重複は許容する（renamingFrom）
        if (CurrentData.Values.VariableColumns.Any(c =>
                string.Equals(c, name, StringComparison.Ordinal)
                && !string.Equals(c, renamingFrom, StringComparison.Ordinal)))
            return $"列名「{name}」は既に存在します。";

        return null;
    }

    /// <summary>新規変数列を追加。失敗時はエラーメッセージを返す。</summary>
    public string? AddVariableColumn(string name)
    {
        var err = ValidateColumnName(name);
        if (err is not null) return err;
        if (CurrentData is null) return "プロファイルが読み込まれていません。";

        CurrentData.Values.VariableColumns.Add(name);
        foreach (var row in CurrentData.Values.Rows)
            row[name] = "";  // インデクサ経由で Item[name] 通知
        return null;
    }

    /// <summary>
    /// 変数列を削除（§5.2.E）。procedure.csv は自動書換しない（仕様通り）。
    /// 影響行のプレビューは <see cref="FindProcedureReferences"/> で別途取得して View 側で表示する。
    /// </summary>
    public string? RemoveVariableColumn(string name)
    {
        if (CurrentData is null) return "プロファイルが読み込まれていません。";
        if (!CurrentData.Values.VariableColumns.Contains(name))
            return $"列「{name}」が存在しません。";

        // 各行の Cells 辞書から削除（VariableColumns 削除を先にすると View 再構築で
        // 残存セルが見えなくなる前に行側を整える）
        foreach (var row in CurrentData.Values.Rows)
            row.Cells.Remove(name);

        CurrentData.Values.VariableColumns.Remove(name);
        return null;
    }

    /// <summary>
    /// 変数列をリネーム（§5.2.E.2）。<paramref name="rewriteProcedure"/> true で
    /// procedure.csv Value 列の <c>$old</c> 参照（単語境界付き）を <c>$new</c> に一括置換する。
    /// </summary>
    public string? RenameVariableColumn(string oldName, string newName, bool rewriteProcedure)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return null;

        var err = ValidateColumnName(newName, renamingFrom: oldName);
        if (err is not null) return err;
        if (CurrentData is null) return "プロファイルが読み込まれていません。";

        var idx = CurrentData.Values.VariableColumns.IndexOf(oldName);
        if (idx < 0) return $"列「{oldName}」が存在しません。";

        // 各行の Cells キーを差し替え（先に行側を整える → 後で VariableColumns 通知）
        foreach (var row in CurrentData.Values.Rows)
        {
            if (row.Cells.TryGetValue(oldName, out var v))
            {
                row.Cells.Remove(oldName);
                row.Cells[newName] = v;
            }
            // 旧列 / 新列の表示を強制 refresh
            row.RaiseCellChanged(oldName);
            row.RaiseCellChanged(newName);
        }

        // VariableColumns の Replace（CollectionChanged 単発で View 再構築）
        CurrentData.Values.VariableColumns[idx] = newName;

        if (rewriteProcedure)
        {
            var pattern = new Regex(@"\$" + Regex.Escape(oldName) + @"\b");
            var replacement = "$" + newName;
            foreach (var step in CurrentData.Steps)
                step.Value = pattern.Replace(step.Value ?? "", replacement);
        }

        return null;
    }

    /// <summary>
    /// procedure.csv の Value 列で <c>$columnName</c> を参照している Step を返す
    /// （§5.2.E.1: 単語境界付き正規表現で前方一致誤爆を防止）。
    /// </summary>
    public IReadOnlyList<PianistStep> FindProcedureReferences(string columnName)
    {
        if (CurrentData is null) return Array.Empty<PianistStep>();
        var pattern = new Regex(@"\$" + Regex.Escape(columnName) + @"\b");
        return CurrentData.Steps
            .Where(s => pattern.IsMatch(s.Value ?? ""))
            .ToList();
    }

    // ─── Phase 操作（§7.2 / §7.3 / §11） ────────────────────────

    /// <summary>Phase 9 色固定パレット（§4.1）。CSV には英名で書く。</summary>
    public static IReadOnlyList<string> PhaseColors { get; } = new[]
    {
        "Blue", "Green", "Yellow", "Orange", "Red", "Purple", "Cyan", "Pink", "Gray"
    };

    /// <summary>
    /// pianist の Action 10 種（§4.2）。CSV には <see cref="PianistActionOption.Code"/> を書き、
    /// UI ComboBox では <see cref="PianistActionOption.Label"/> を表示する。
    /// </summary>
    public static IReadOnlyList<PianistActionOption> ActionOptions { get; } = new[]
    {
        new PianistActionOption { Code = "Open",       Label = "アプリ・URL を開く" },
        new PianistActionOption { Code = "WaitWin",    Label = "ウィンドウが現れるのを待つ" },
        new PianistActionOption { Code = "AppFocus",   Label = "ウィンドウを前面にする" },
        new PianistActionOption { Code = "Type",       Label = "キーボードで文字入力" },
        new PianistActionOption { Code = "Key",        Label = "特殊キーを押す" },
        new PianistActionOption { Code = "Wait",       Label = "一時停止" },
        new PianistActionOption { Code = "Copy",       Label = "クリップボードにコピー" },
        new PianistActionOption { Code = "Paste",      Label = "貼り付け" },
        new PianistActionOption { Code = "Screenshot", Label = "スクリーンショットを撮る" },
        new PianistActionOption { Code = "Prompt",     Label = "オペレータに確認を求める" },
    };

    /// <summary>
    /// Key Action のプリセット（§4.3）。SendKeys 表記の Code と日本語解説をペアで保持する。
    /// ComboBox 側で <c>TextSearch.TextPath="Code"</c> を指定することで、ドロップダウンには
    /// 「Code  日本語解説」を 2 列で並べつつ、選択時にセルへ入るのは <see cref="PianistKeyPreset.Code"/>
    /// のみ。自由入力も従来通り可能（IsEditable=True）。
    ///
    /// 修飾子: <c>+</c>=Shift / <c>^</c>=Ctrl / <c>%</c>=Alt。
    /// 特殊キーは <c>{}</c> 囲み（<c>{TAB}</c>, <c>{F1}</c> 等）。
    /// 同キー連打は <c>{TAB 3}</c> のように半角スペースで回数を指定。
    /// </summary>
    public static IReadOnlyList<PianistKeyPreset> KeyPresets { get; } = new PianistKeyPreset[]
    {
        // ── 基本キー ────────────────────────────────────────────
        new() { Code = "{ENTER}",     Description = "Enter（決定）" },
        new() { Code = "{TAB}",       Description = "Tab（次のフィールドへ）" },
        new() { Code = "+{TAB}",      Description = "Shift + Tab（前のフィールドへ）" },
        new() { Code = "{ESC}",       Description = "Esc（キャンセル）" },
        new() { Code = "{BACKSPACE}", Description = "BackSpace（1 文字前を削除）" },
        new() { Code = "{DELETE}",    Description = "Delete（カーソル位置を削除）" },

        // ── 矢印キー ────────────────────────────────────────────
        new() { Code = "{UP}",        Description = "↑（上）" },
        new() { Code = "{DOWN}",      Description = "↓（下）" },
        new() { Code = "{LEFT}",      Description = "←（左）" },
        new() { Code = "{RIGHT}",     Description = "→（右）" },

        // ── ナビゲーション ──────────────────────────────────────
        new() { Code = "{HOME}",      Description = "Home（行頭へ）" },
        new() { Code = "{END}",       Description = "End（行末へ）" },
        new() { Code = "{PGUP}",      Description = "Page Up（1 ページ上）" },
        new() { Code = "{PGDN}",      Description = "Page Down（1 ページ下）" },
        new() { Code = "{INSERT}",    Description = "Insert（挿入モード切替）" },

        // ── ファンクションキー（よく使うもの） ─────────────────
        new() { Code = "{F1}",        Description = "F1（ヘルプ）" },
        new() { Code = "{F2}",        Description = "F2（リネーム）" },
        new() { Code = "{F5}",        Description = "F5（更新 / 再読込）" },
        new() { Code = "{F11}",       Description = "F11（全画面切替）" },
        new() { Code = "%{F4}",       Description = "Alt + F4（ウィンドウを閉じる）" },

        // ── Ctrl 系（編集 / アプリ操作） ───────────────────────
        new() { Code = "^c",          Description = "Ctrl + C（コピー）" },
        new() { Code = "^v",          Description = "Ctrl + V（貼り付け）" },
        new() { Code = "^x",          Description = "Ctrl + X（切り取り）" },
        new() { Code = "^a",          Description = "Ctrl + A（全選択）" },
        new() { Code = "^z",          Description = "Ctrl + Z（元に戻す）" },
        new() { Code = "^y",          Description = "Ctrl + Y（やり直し）" },
        new() { Code = "^s",          Description = "Ctrl + S（保存）" },
        new() { Code = "^n",          Description = "Ctrl + N（新規）" },
        new() { Code = "^o",          Description = "Ctrl + O（開く）" },
        new() { Code = "^w",          Description = "Ctrl + W（タブ / ウィンドウを閉じる）" },
        new() { Code = "^t",          Description = "Ctrl + T（新規タブ）" },
        new() { Code = "^f",          Description = "Ctrl + F（検索）" },
        new() { Code = "^p",          Description = "Ctrl + P（印刷）" },
        new() { Code = "^{HOME}",     Description = "Ctrl + Home（文書先頭へ）" },
        new() { Code = "^{END}",      Description = "Ctrl + End（文書末尾へ）" },
        new() { Code = "^{LEFT}",     Description = "Ctrl + ←（前の単語へ）" },
        new() { Code = "^{RIGHT}",    Description = "Ctrl + →（次の単語へ）" },

        // ── Shift 系（選択拡張） ──────────────────────────────
        new() { Code = "+{LEFT}",     Description = "Shift + ←（左へ選択拡張）" },
        new() { Code = "+{RIGHT}",    Description = "Shift + →（右へ選択拡張）" },
        new() { Code = "+{UP}",       Description = "Shift + ↑（上へ選択拡張）" },
        new() { Code = "+{DOWN}",     Description = "Shift + ↓（下へ選択拡張）" },
        new() { Code = "+{HOME}",     Description = "Shift + Home（行頭まで選択）" },
        new() { Code = "+{END}",      Description = "Shift + End（行末まで選択）" },

        // ── Ctrl + Shift 系（単語 / 文書単位の選択） ──────────
        new() { Code = "^+{LEFT}",    Description = "Ctrl + Shift + ←（前単語まで選択）" },
        new() { Code = "^+{RIGHT}",   Description = "Ctrl + Shift + →（次単語まで選択）" },
        new() { Code = "^+{HOME}",    Description = "Ctrl + Shift + Home（文書先頭まで選択）" },
        new() { Code = "^+{END}",     Description = "Ctrl + Shift + End（文書末尾まで選択）" },

        // ── 繰り返し（{KEY N} 形式: 任意のキーを N 回。N を直接編集すれば回数変更可） ──
        new() { Code = "{TAB 3}",       Description = "Tab を 3 回（連続したフィールド送り）" },
        new() { Code = "{TAB 5}",       Description = "Tab を 5 回" },
        new() { Code = "+{TAB 3}",      Description = "Shift + Tab を 3 回（逆方向にフィールド送り）" },
        new() { Code = "{DOWN 5}",      Description = "↓ を 5 回（リストスクロール）" },
        new() { Code = "{DOWN 10}",     Description = "↓ を 10 回（大きくスクロール）" },
        new() { Code = "{ENTER 2}",     Description = "Enter を 2 回（ダイアログ連続確定）" },
        new() { Code = "{BACKSPACE 5}", Description = "BackSpace を 5 回" },
    };

    /// <summary>Wait Action / Wait 列の数値プリセット（§4.3、ms 単位）。</summary>
    public static IReadOnlyList<string> WaitPresets { get; } = new[]
    {
        "300", "500", "1000", "1500", "3000", "5000", "10000",
    };

    /// <summary>
    /// Type / Paste / Copy / Prompt の Value 入力でサジェスト表示する <c>$VarName</c> 候補。
    /// VariableColumns に `$` prefix を付けたもの。差分マージで更新する
    /// （feedback_combobox_itemssource ルール）。
    /// </summary>
    [ObservableProperty] private ObservableCollection<string> _variableReferences = new();

    private void RebuildVariableReferences()
    {
        var desired = CurrentData?.Values.VariableColumns
            .Select(c => "$" + c)
            .ToList()
            ?? new List<string>();

        // 不要分削除
        for (int i = VariableReferences.Count - 1; i >= 0; i--)
            if (!desired.Contains(VariableReferences[i]))
                VariableReferences.RemoveAt(i);

        // 不足分追加
        foreach (var v in desired)
            if (!VariableReferences.Contains(v))
                VariableReferences.Add(v);
    }

    /// <summary>PhaseID のバリデーション。</summary>
    public string? ValidatePhaseId(string phaseId, string? renamingFrom = null)
    {
        if (string.IsNullOrWhiteSpace(phaseId))
            return "PhaseID を入力してください。";
        if (phaseId.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            return "PhaseID にファイル名禁止文字が含まれています（instructions/&lt;PhaseID&gt;.txt のファイル名にも使われます）。";
        if (CurrentData is null) return "プロファイル未選択です。";

        var existing = CurrentData.Steps
            .Select(s => s.PhaseID)
            .Distinct(StringComparer.Ordinal);
        if (existing.Any(id =>
                string.Equals(id, phaseId, StringComparison.Ordinal)
                && !string.Equals(id, renamingFrom, StringComparison.Ordinal)))
            return $"PhaseID「{phaseId}」は既に procedure.csv に存在します。";

        return null;
    }

    /// <summary>
    /// instructions/&lt;PhaseID&gt;.txt が物理的に存在するか（リネーム時の上書き確認用）。
    /// </summary>
    public bool DoesInstructionsFileExist(string phaseId)
    {
        if (CurrentData is null) return false;
        var path = System.IO.Path.Combine(
            CurrentData.Entry.FolderPath, "instructions", $"{phaseId}.txt");
        return System.IO.File.Exists(path);
    }

    /// <summary>
    /// Phase を新規作成（§7.2: 新規時は instructions/&lt;新 PhaseID&gt;.txt を空ファイルで自動生成、
    /// 既存ファイルがあれば再利用 / 上書きしない）。
    /// 1 件の placeholder Step（StepNo=1, Action=Wait, Value=0）が procedure.csv に追加される。
    /// </summary>
    public string? CreatePhase(string phaseId, string label, string color)
    {
        var err = ValidatePhaseId(phaseId);
        if (err is not null) return err;
        if (CurrentData is null) return "プロファイル未選択です。";
        if (!PhaseColors.Contains(color)) color = "Blue";

        // Placeholder Step（後でユーザーが自由に編集）
        CurrentData.Steps.Add(new PianistStep
        {
            PhaseID    = phaseId,
            PhaseLabel = label,
            Color      = color,
            StepNo     = 1,
            Action     = "Wait",
            Value      = "0",
        });

        // instructions/&lt;PhaseID&gt;.txt: 既存があれば再利用（§7.3）、無ければ 4-section 空雛形を作成
        // pianist v1.4.0 以降の section marker DSL に揃える（[RPA] / [Manual] のヘッダだけ用意）
        try
        {
            var dir = System.IO.Path.Combine(CurrentData.Entry.FolderPath, "instructions");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"{phaseId}.txt");
            const string emptyTemplate = "[RPA]\r\n\r\n[Manual]\r\n\r\n";
            if (!System.IO.File.Exists(path))
                System.IO.File.WriteAllText(path, emptyTemplate, new System.Text.UTF8Encoding(false));

            // メモリ上の Instructions も同期（既存内容を読み込む）
            if (!CurrentData.Instructions.ContainsKey(phaseId))
                CurrentData.Instructions[phaseId] = System.IO.File.Exists(path)
                    ? System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8)
                    : emptyTemplate;
        }
        catch (Exception ex)
        {
            return $"instructions/{phaseId}.txt の作成に失敗: {ex.Message}";
        }

        RebuildPhases();
        SelectedPhase = Phases.FirstOrDefault(p => p.PhaseID == phaseId);
        return null;
    }

    /// <summary>
    /// Phase を リネーム（§7.2: 既定 ON で instructions/.txt も追従リネーム）。
    /// 同 PhaseID の全 Step に対して PhaseID / PhaseLabel / Color を一括更新する。
    /// </summary>
    public string? RenamePhase(string oldId, string newId, string newLabel, string newColor,
                               bool renameInstructionsFile)
    {
        var err = ValidatePhaseId(newId, renamingFrom: oldId);
        if (err is not null) return err;
        if (CurrentData is null) return "プロファイル未選択です。";
        if (!PhaseColors.Contains(newColor)) newColor = "Blue";

        var idChanged = !string.Equals(oldId, newId, StringComparison.Ordinal);

        // 全 Step を更新
        foreach (var s in CurrentData.Steps.Where(s => s.PhaseID == oldId).ToList())
        {
            s.PhaseID    = newId;
            s.PhaseLabel = newLabel;
            s.Color      = newColor;
        }

        // instructions/.txt rename
        if (idChanged && renameInstructionsFile)
        {
            try
            {
                var dir     = System.IO.Path.Combine(CurrentData.Entry.FolderPath, "instructions");
                var oldPath = System.IO.Path.Combine(dir, $"{oldId}.txt");
                var newPath = System.IO.Path.Combine(dir, $"{newId}.txt");
                if (System.IO.File.Exists(oldPath))
                {
                    if (System.IO.File.Exists(newPath))
                        System.IO.File.Delete(newPath); // 上書き（呼び出し側で事前確認済み）
                    System.IO.File.Move(oldPath, newPath);
                }
                if (CurrentData.Instructions.TryGetValue(oldId, out var content))
                {
                    CurrentData.Instructions.Remove(oldId);
                    CurrentData.Instructions[newId] = content;
                }
            }
            catch (Exception ex)
            {
                return $"instructions/{oldId}.txt → {newId}.txt のリネームに失敗: {ex.Message}";
            }
        }

        RebuildPhases();
        SelectedPhase = Phases.FirstOrDefault(p => p.PhaseID == newId);
        return null;
    }

    /// <summary>
    /// Phase を削除（§7.2: 既定 OFF — 削除しない場合 instructions/.txt は孤児として残る）。
    /// 同 PhaseID の全 Step が procedure.csv から取り除かれる。
    /// </summary>
    public string? DeletePhase(string phaseId, bool deleteInstructionsFile)
    {
        if (CurrentData is null) return "プロファイル未選択です。";

        var toRemove = CurrentData.Steps.Where(s => s.PhaseID == phaseId).ToList();
        foreach (var s in toRemove)
            CurrentData.Steps.Remove(s);

        if (deleteInstructionsFile)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    CurrentData.Entry.FolderPath, "instructions", $"{phaseId}.txt");
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                CurrentData.Instructions.Remove(phaseId);
            }
            catch (Exception ex)
            {
                return $"instructions/{phaseId}.txt の削除に失敗: {ex.Message}";
            }
        }

        RebuildPhases();
        return null;
    }

    /// <summary>VM の Phases を再計算する公開ラッパ（View 側からは無効になった private を呼べないため）。</summary>
    public void RefreshPhases() => RebuildPhases();

    /// <summary>
    /// 現在選択中の Phase に Step を 1 件追加（PhaseID/PhaseLabel/Color は Phase から継承、
    /// StepNo は Phase 内の最大値 + 1、Action は "Wait"、Wait 列は空、Value="0"）。
    /// </summary>
    [RelayCommand]
    private void AddStep()
    {
        if (CurrentData is null || SelectedPhase is null) return;

        var phaseSteps = CurrentData.Steps.Where(s => s.PhaseID == SelectedPhase.PhaseID).ToList();
        var nextStepNo = phaseSteps.Count == 0 ? 1 : phaseSteps.Max(s => s.StepNo) + 1;

        var newStep = new PianistStep
        {
            PhaseID    = SelectedPhase.PhaseID,
            PhaseLabel = SelectedPhase.PhaseLabel,
            Color      = SelectedPhase.Color,
            StepNo     = nextStepNo,
            Action     = "Wait",
            Value      = "0",
        };
        CurrentData.Steps.Add(newStep);

        // Phase の StepCount を更新（Phases リストの再構築）
        RebuildPhases();
        SelectedPhase = Phases.FirstOrDefault(p => p.PhaseID == newStep.PhaseID);
    }

    /// <summary>
    /// Step グリッドのドラッグ&ドロップ Drop 確定時に <see cref="Helpers.DataGridRowDragDropBehavior"/>
    /// から呼ばれる（ProfileDetailView と同じ Behavior を共有）。
    ///
    /// <paramref name="req"/> の SourceIndex / TargetIndex は **filter-view** 上の index
    /// （CurrentPhaseStepsView 内の位置）。これを裏側の <see cref="PianistProfileData.Steps"/>
    /// 上の index に翻訳して <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/>
    /// を呼び、最後に当該 Phase の StepNo を 1, 2, 3... に振り直す。
    ///
    /// 異なる Phase 間のドロップは発生しない（DataGrid のフィルタにより同一 Phase の行しか
    /// 表示されていないため、ドロップ先は必然的に同一 Phase）。
    ///
    /// pianist.ps1 は procedure.csv の物理出現順で foreach 実行するため、StepNo の値ではなく
    /// Steps 内の順序が実行順を決める。StepNo 振り直しは視認性 / 整合性チェック §12 の
    /// StepNo 重複回避のため。
    /// </summary>
    [RelayCommand]
    private void MoveStepRow(RowMoveRequest? req)
    {
        if (req is null || CurrentData is null || SelectedPhase is null) return;

        var phaseId = SelectedPhase.PhaseID;
        var phaseSteps = CurrentData.Steps
            .Where(s => string.Equals(s.PhaseID, phaseId, StringComparison.Ordinal))
            .ToList();

        if (req.SourceIndex < 0 || req.SourceIndex >= phaseSteps.Count) return;
        var source = phaseSteps[req.SourceIndex];

        // Behavior 側で渡される TargetIndex は ObservableCollection.Move の "抜き取り後 index"
        // 仕様（filter-view 内）。filter-view から source を抜いた残りリストの TargetIndex 位置に
        // 挿入したい、と読み替える。
        var afterRemoval = phaseSteps.Where((_, i) => i != req.SourceIndex).ToList();
        if (req.TargetIndex < 0 || req.TargetIndex > afterRemoval.Count) return;

        // 裏側 Steps 上の挿入位置を逆算: 末尾なら同 Phase 最終要素の次、それ以外なら anchor の位置
        int dstIdxInAll;
        if (req.TargetIndex == afterRemoval.Count)
        {
            var lastSamePhase = CurrentData.Steps
                .Select((s, i) => (s, i))
                .Where(x => string.Equals(x.s.PhaseID, phaseId, StringComparison.Ordinal)
                            && !ReferenceEquals(x.s, source))
                .LastOrDefault();
            dstIdxInAll = lastSamePhase.s is null
                ? CurrentData.Steps.Count
                : lastSamePhase.i + 1;
        }
        else
        {
            var anchor = afterRemoval[req.TargetIndex];
            dstIdxInAll = CurrentData.Steps.IndexOf(anchor);
        }

        var srcIdxInAll = CurrentData.Steps.IndexOf(source);
        if (srcIdxInAll < 0 || dstIdxInAll < 0) return;

        // ObservableCollection.Move の抜き取り後補正
        if (srcIdxInAll < dstIdxInAll) dstIdxInAll--;
        if (srcIdxInAll == dstIdxInAll) return;

        CurrentData.Steps.Move(srcIdxInAll, dstIdxInAll);
        RenumberPhaseStepNos(phaseId);
    }

    /// <summary>同 PhaseID の Step を物理出現順で 1, 2, 3... に振り直す。</summary>
    private void RenumberPhaseStepNos(string phaseId)
    {
        if (CurrentData is null) return;
        int n = 1;
        foreach (var s in CurrentData.Steps)
        {
            if (string.Equals(s.PhaseID, phaseId, StringComparison.Ordinal))
            {
                if (s.StepNo != n) s.StepNo = n;
                n++;
            }
        }
    }

    /// <summary>
    /// Step を 1 件削除する。Step DataGrid の ItemsSource は filtered ICollectionView のため
    /// DataGrid 標準の <c>CanUserDeleteRows</c> では Phases の StepCount が更新されず UX が壊れる。
    /// 明示コマンドで削除し <see cref="RebuildPhases"/> を呼ぶことで Phase 一覧を一貫した状態に保つ。
    /// 当該 Phase の最後の Step を消すと Phase ごと一覧から消える（PhaseID が procedure.csv に
    /// 存在しなくなるため）。
    /// </summary>
    [RelayCommand]
    private void DeleteStep(PianistStep? step)
    {
        if (step is null || CurrentData is null) return;

        var ok = MessageBox.Show(
            $"Step {step.StepNo} ({step.Action}) を削除しますか？\nこの操作は保存するまで実ファイルには反映されません。",
            "Step 削除の確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        var phaseId = step.PhaseID;
        CurrentData.Steps.Remove(step);

        RebuildPhases();
        // 同じ Phase を選択し直す（最後の Step だった場合は Phase 自体が無くなるため先頭にフォールバック）
        SelectedPhase = Phases.FirstOrDefault(p => p.PhaseID == phaseId) ?? Phases.FirstOrDefault();
    }

    // ─── バリデーション（§12） ──────────────────────────────────

    /// <summary>pianist の正規アクション 10 種（§4.2）。</summary>
    private static readonly HashSet<string> ValidActions = new(StringComparer.Ordinal)
    {
        "Open", "WaitWin", "AppFocus", "Type", "Key",
        "Wait", "Copy", "Paste", "Screenshot", "Prompt"
    };

    /// <summary>variable 列名の許容パターン（VarName 参照側と完全一致）。</summary>
    private static readonly Regex VarReferencePattern = new(@"\$([A-Za-z_][A-Za-z0-9_]*)");

    /// <summary>
    /// 整合性チェックを実行して issue リストを返す（§12 全項目）。CurrentData が null なら空。
    /// </summary>
    public IReadOnlyList<PianistValidationIssue> RunValidation()
    {
        var issues = new List<PianistValidationIssue>();
        if (CurrentData is null) return issues;

        var phaseGroups = CurrentData.Steps.GroupBy(s => s.PhaseID, StringComparer.Ordinal).ToList();
        var phaseIdSet  = phaseGroups.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var definedVars = CurrentData.Values.VariableColumns.ToHashSet(StringComparer.Ordinal);

        // Phase 内 PhaseLabel / Color の不一致（同 PhaseID で値がブレている）
        foreach (var g in phaseGroups)
        {
            var labels = g.Select(s => s.PhaseLabel).Distinct().ToList();
            if (labels.Count > 1)
                issues.Add(new PianistValidationIssue
                {
                    Level    = PianistValidationIssue.Severity.Warning,
                    Category = "Phase",
                    Message  = $"Phase {g.Key} 内で PhaseLabel が複数値: {string.Join(" / ", labels)}",
                    Source   = g.Key,
                });
            var colors = g.Select(s => s.Color).Distinct().ToList();
            if (colors.Count > 1)
                issues.Add(new PianistValidationIssue
                {
                    Level    = PianistValidationIssue.Severity.Warning,
                    Category = "Phase",
                    Message  = $"Phase {g.Key} 内で Color が複数値: {string.Join(" / ", colors)}",
                    Source   = g.Key,
                });
        }

        // Phase 内 StepNo 重複なし
        foreach (var g in phaseGroups)
        {
            var dupes = g.GroupBy(s => s.StepNo).Where(x => x.Count() > 1).ToList();
            foreach (var d in dupes)
                issues.Add(new PianistValidationIssue
                {
                    Level    = PianistValidationIssue.Severity.Warning,
                    Category = "Step",
                    Message  = $"Phase {g.Key} で StepNo={d.Key} が {d.Count()} 回重複",
                    Source   = $"{g.Key} StepNo={d.Key}",
                });
        }

        // Action は 10 種のいずれか
        foreach (var step in CurrentData.Steps)
        {
            if (!ValidActions.Contains(step.Action))
                issues.Add(new PianistValidationIssue
                {
                    Level    = PianistValidationIssue.Severity.Error,
                    Category = "Step",
                    Message  = $"未知の Action「{step.Action}」（許容: {string.Join(", ", ValidActions)}）",
                    Source   = $"{step.PhaseID} Step {step.StepNo}",
                });
        }

        // Color は 9 色のいずれか（PhaseColors）
        foreach (var step in CurrentData.Steps)
        {
            if (!PhaseColors.Contains(step.Color))
                issues.Add(new PianistValidationIssue
                {
                    Level    = PianistValidationIssue.Severity.Error,
                    Category = "Phase",
                    Message  = $"未知の Color「{step.Color}」（許容: {string.Join(", ", PhaseColors)}）",
                    Source   = $"{step.PhaseID} Step {step.StepNo}",
                });
        }

        // Value 内の $VarName 参照が values.csv 列に存在
        foreach (var step in CurrentData.Steps)
        {
            var matches = VarReferencePattern.Matches(step.Value ?? "");
            foreach (Match m in matches)
            {
                var name = m.Groups[1].Value;
                if (!definedVars.Contains(name))
                    issues.Add(new PianistValidationIssue
                    {
                        Level    = PianistValidationIssue.Severity.Warning,
                        Category = "Variable",
                        Message  = $"未定義変数 ${name} を参照（values.csv に列がありません）",
                        Source   = $"{step.PhaseID} Step {step.StepNo}",
                    });
            }
        }

        // values.csv 列名が ^[A-Za-z_][A-Za-z0-9_]*$
        foreach (var col in CurrentData.Values.VariableColumns)
        {
            if (!ColumnNamePattern.IsMatch(col))
                issues.Add(new PianistValidationIssue
                {
                    Level    = PianistValidationIssue.Severity.Error,
                    Category = "Variable",
                    Message  = $"変数列名「{col}」が無効（^[A-Za-z_][A-Za-z0-9_]*$ に違反、procedure.csv から $参照不能）",
                    Source   = $"values.csv 列: {col}",
                });
        }

        // values.csv に NewPCName 列がある（旧 long format ではない）
        if (CurrentData.Values.WasLegacyFormat)
            issues.Add(new PianistValidationIssue
            {
                Level    = PianistValidationIssue.Severity.Warning,
                Category = "Variable",
                Message  = "values.csv が旧 long format（Key,Value,Encrypted,Note）です。新 wide format への変換を推奨",
                Source   = "values.csv",
            });

        // * 行が高々 1 行
        var starRows = CurrentData.Values.Rows.Count(r => r.IsStar);
        if (starRows > 1)
            issues.Add(new PianistValidationIssue
            {
                Level    = PianistValidationIssue.Severity.Warning,
                Category = "Variable",
                Message  = $"`*` 行が {starRows} 行存在します（高々 1 行が推奨、解決規則の挙動が曖昧になります）",
                Source   = "values.csv",
            });

        // default_phase が procedure.csv に存在する PhaseID
        var dp = CurrentData.Metadata.DefaultPhase;
        if (!string.IsNullOrEmpty(dp) && !phaseIdSet.Contains(dp))
            issues.Add(new PianistValidationIssue
            {
                Level    = PianistValidationIssue.Severity.Warning,
                Category = "Meta",
                Message  = $"default_phase「{dp}」に該当する Phase が procedure.csv にありません",
                Source   = "pianist.json",
            });

        // instructions/<PhaseID>.txt の有無
        foreach (var pid in phaseIdSet)
        {
            if (!CurrentData.Instructions.ContainsKey(pid))
                issues.Add(new PianistValidationIssue
                {
                    Level    = PianistValidationIssue.Severity.Info,
                    Category = "Instructions",
                    Message  = $"instructions/{pid}.txt がありません（実行可、手順書欄はプレースホルダ表示）",
                    Source   = pid,
                });
        }

        // [Samples] section が参照する画像ファイルが <profile>/screenshots/ に実在するか
        // pianist runtime は欠損時 "(missing) <name>" placeholder で動くので Warning 扱い
        var screenshotsDir = System.IO.Path.Combine(CurrentData.Entry.FolderPath, "screenshots");
        foreach (var (pid, raw) in CurrentData.Instructions)
        {
            var parsed = PianistInstructionParser.Parse(raw);
            foreach (var sample in parsed.Samples)
            {
                var path = System.IO.Path.Combine(screenshotsDir, sample.File);
                if (!System.IO.File.Exists(path))
                    issues.Add(new PianistValidationIssue
                    {
                        Level    = PianistValidationIssue.Severity.Warning,
                        Category = "Samples",
                        Message  = $"画像ファイル「screenshots/{sample.File}」が存在しません（pianist で `(missing)` プレースホルダ表示）",
                        Source   = $"{pid} [Samples]",
                    });
            }
        }

        // 並び替え: Error → Warning → Info
        return issues
            .OrderBy(i => i.Level)
            .ThenBy(i => i.Category, StringComparer.Ordinal)
            .ThenBy(i => i.Source, StringComparer.Ordinal)
            .ToList();
    }

    // ─── 保存（§10） ────────────────────────────────────────────

    private bool CanSave() => IsDirty && !IsSaving && CurrentData is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (CurrentData is null) return;

        // §5.2.D: 保存前に hostlist と照合して orphan NewPCName を警告
        var orphans = FindOrphanHostNames();
        if (orphans.Count > 0)
        {
            var lines = string.Join("\n", orphans.Take(20).Select(n => "  - " + n));
            var more  = orphans.Count > 20 ? $"\n  ... 他 {orphans.Count - 20} 件" : "";
            var ok = MessageBox.Show(
                $"values.csv の以下の NewPCName が hostlist.csv に存在しません ({orphans.Count} 件):\n\n{lines}{more}\n\n"
                + "このまま保存しますか？（保存後、対応する端末を hostlist に追加することを推奨）",
                "Orphan NewPCName 検出",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        // §4.4: WaitWin の Wait 空 → 10000 自動補完
        AutoFillWaitWinTimeout();

        IsSaving   = true;
        SaveStatus = null;
        SaveError  = null;

        try
        {
            var error = await _pianistService.SaveProfileAsync(CurrentData, _crypto);
            if (error is not null)
            {
                SaveError = error;
            }
            else
            {
                SaveStatus = $"✓ 保存しました ({DateTime.Now:HH:mm:ss})";
                IsDirty    = false;
            }
        }
        catch (Exception ex)
        {
            SaveError = $"保存例外: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>hostlist にない（= orphan）NewPCName のリストを返す（§5.2.D）。</summary>
    private List<string> FindOrphanHostNames()
    {
        if (CurrentData is null) return new();
        var hosts = new HashSet<string>(_allHostNames, StringComparer.Ordinal);
        return CurrentData.Values.Rows
            .Where(r => !r.IsStar && !string.IsNullOrEmpty(r.NewPCName))
            .Select(r => r.NewPCName)
            .Where(n => !hosts.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>WaitWin の Wait 列が空の Step に対し 10000 ms（既定タイムアウト）を自動補完する（§4.4）。</summary>
    private void AutoFillWaitWinTimeout()
    {
        if (CurrentData is null) return;
        foreach (var step in CurrentData.Steps)
        {
            if (string.Equals(step.Action, "WaitWin", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(step.Wait))
            {
                step.Wait = "10000";
            }
        }
    }

    /// <summary>CurrentData 変更時に保存ステータスをクリアする。</summary>
    partial void OnCurrentDataChanged(PianistProfileData? value)
    {
        SaveStatus = null;
        SaveError  = null;
        SaveCommand.NotifyCanExecuteChanged();

        // 別 profile に切り替わった時点で前回の検証結果は無効
        ValidationIssues.Clear();
        HasRunValidation = false;
    }

    [RelayCommand]
    private void RunValidationNow()
    {
        var results = RunValidation();
        ValidationIssues = new ObservableCollection<PianistValidationIssue>(results);
        HasRunValidation = true;
    }

    // ─── テスト実行（fabriq エンジンへ子プロセスで投入） ─────────

    private bool CanTestRun()
        => CurrentData is not null
           && SelectedProfile is not null
           && !IsTestRunning
           && !IsSaving
           && _workspace.IsOpen;

    /// <summary>
    /// 現在選択中の Pianist Profile を fabriq エンジン（pianist.ps1）に渡してテスト実行する。
    ///
    /// フロー:
    /// 1. Dirty なら「保存して実行 / 破棄して実行 / キャンセル」3 択
    /// 2. <see cref="RunValidation"/> を実行し Error あれば中断確認
    /// 3. <see cref="PianistTestRunDialog"/> でホスト選択 + 副作用警告
    /// 4. 任意で Studio MainWindow を最小化（焦点を逃がす）
    /// 5. <see cref="IPianistTestRunService.RunAsync"/> 実行（GUI が閉じるまでブロック）
    /// 6. ログ + ModuleResult を <see cref="LogViewerDialog"/> で表示
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTestRun))]
    private async Task TestRunAsync()
    {
        if (CurrentData is null || SelectedProfile is null) return;

        // ── 1. Dirty ガード ──────────────────────────────────────
        if (IsDirty)
        {
            var choice = MessageBox.Show(
                $"「{SelectedProfile.Name}」に未保存の変更があります。\n\n" +
                "「はい」: 保存してから実行\n" +
                "「いいえ」: 未保存のまま実行（保存していないファイルは fabriq から見えません）\n" +
                "「キャンセル」: 実行を中止",
                "未保存の変更",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Cancel) return;
            if (choice == MessageBoxResult.Yes)
            {
                await SaveAsync();
                if (IsDirty)
                {
                    // 保存に失敗した（orphan キャンセル等）
                    return;
                }
            }
        }

        // ── 2. バリデーション結果を実行前に提示 ────────────────
        var issues = RunValidation();
        var errors = issues.Where(i => i.Level == PianistValidationIssue.Severity.Error).ToList();
        if (errors.Count > 0)
        {
            var lines = string.Join("\n", errors.Take(10).Select(e => $"  - {e.Message}"));
            var more  = errors.Count > 10 ? $"\n  ... 他 {errors.Count - 10} 件" : "";
            var ok = MessageBox.Show(
                $"整合性チェックで Error が {errors.Count} 件検出されました:\n\n{lines}{more}\n\n"
                + "このまま実行しますか？（pianist.ps1 が読めない / 異常動作する可能性があります）",
                "整合性 Error 検出",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        // ── 3. 実行ホスト + 副作用警告ダイアログ ──────────────
        var choice2 = PianistTestRunDialog.Show(
            profile:        SelectedProfile,
            data:           CurrentData,
            availableHosts: _allHostNames,
            hasPassphrase:  _crypto.HasPassphrase);
        if (choice2 is null) return;

        // ── 4. オプションで Studio を最小化 ─────────────────────
        Window? mainWin = Application.Current?.MainWindow;
        WindowState? originalState = null;
        if (choice2.MinimizeStudio && mainWin is not null)
        {
            originalState     = mainWin.WindowState;
            mainWin.WindowState = WindowState.Minimized;
        }

        // ── 5. 実行 ──────────────────────────────────────────────
        IsTestRunning = true;
        TestRunStatus = $"▶ テスト実行中… ({SelectedProfile.Name} / {choice2.NewPCName})";
        _testRunCts   = new CancellationTokenSource();

        PianistTestRunResult? result = null;
        Exception? launchError = null;
        try
        {
            result = await _testRunService.RunAsync(
                profileName: SelectedProfile.Name,
                newPCName:   choice2.NewPCName,
                ct:          _testRunCts.Token);
        }
        catch (Exception ex)
        {
            launchError = ex;
        }
        finally
        {
            IsTestRunning = false;
            TestRunStatus = null;
            _testRunCts?.Dispose();
            _testRunCts = null;

            // 最小化していた場合は元に戻す
            if (originalState is not null && mainWin is not null)
                mainWin.WindowState = originalState.Value;
        }

        // ── 6. 結果表示 ──────────────────────────────────────────
        if (launchError is not null)
        {
            MessageBox.Show(
                $"テスト実行の起動に失敗しました:\n\n{launchError.Message}",
                "Pianist テスト実行",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (result is null) return;

        var titleSuffix = result.WasCancelled
            ? "（中止）"
            : (result.ModuleResultStatus is not null ? $"（{result.ModuleResultStatus}）" : "");
        var title = $"Pianist テスト実行ログ — {SelectedProfile.Name}{titleSuffix}";

        // ヘッダに ModuleResult Message を追加してから本ログ
        var displayLog = new System.Text.StringBuilder();
        displayLog.AppendLine($"Profile : {SelectedProfile.Name}");
        displayLog.AppendLine($"Host    : {choice2.NewPCName}");
        displayLog.AppendLine($"Status  : {result.ModuleResultStatus ?? "(unknown)"}");
        if (result.ModuleResultMessage is not null)
            displayLog.AppendLine($"Message : {result.ModuleResultMessage}");
        if (result.ModuleResultVerified is not null)
            displayLog.AppendLine($"Verified: {result.ModuleResultVerified}");
        displayLog.AppendLine($"ExitCode: {result.ExitCode}");
        displayLog.AppendLine(new string('-', 60));
        displayLog.AppendLine();
        displayLog.Append(result.Log);

        LogViewerDialog.ShowLog(title, displayLog.ToString());
    }

    /// <summary>テスト実行の中断（外部 / Phase D で「中止」ボタン経由）。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelTestRun))]
    private void CancelTestRun() => _testRunCts?.Cancel();

    private bool CanCancelTestRun() => IsTestRunning && _testRunCts is not null;

    // ─── 旧 long format → wide format 移行（§5.5） ───────────────

    /// <summary>
    /// 旧 long format の values.csv（Key,Value,Encrypted,Note）を読み出して wide format に変換し、
    /// メモリ上の <see cref="PianistProfileData.Values"/> を差し替える。確定（書き出し）は
    /// 通常の保存ボタンで行う。
    /// </summary>
    [RelayCommand]
    private async Task MigrateLegacyValuesAsync()
    {
        if (CurrentData is null || !IsLegacyValuesDetected) return;

        var confirm = MessageBox.Show(
            "旧 long format（Key,Value,Encrypted,Note）の values.csv を新 wide format に変換しますか？\n\n"
            + "・各 Key 列が wide format の列名になります\n"
            + "・各 Value セル値が `*` 行のセル値に入ります\n"
            + "・Encrypted=1 の値には `ENC:` prefix を付加（既に付いていればそのまま）\n"
            + "・Note 列は破棄されます（wide format に対応する欄がないため）\n\n"
            + "メモリ上で変換します。実ファイルへの書き出しは保存ボタンで行ってください。",
            "values.csv 形式変換",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var legacy = await _pianistService.LoadLegacyValuesAsync(CurrentData.Entry);

            // 新 wide テーブルを構築
            var wide = new PianistValueTable();
            wide.WasLegacyFormat = false;
            foreach (var e in legacy)
                if (!string.IsNullOrWhiteSpace(e.Key) && !wide.VariableColumns.Contains(e.Key))
                    wide.VariableColumns.Add(e.Key);

            // `*` 行を生成し、各 Key の Value をセル化（必要なら ENC: prefix）
            var star = new PianistValueRow { NewPCName = "*", Table = wide };
            foreach (var col in wide.VariableColumns)
                star.Cells[col] = "";
            foreach (var e in legacy)
            {
                if (string.IsNullOrWhiteSpace(e.Key)) continue;
                var v = e.Value ?? "";
                var encrypted = string.Equals(e.Encrypted, "1", StringComparison.Ordinal);
                if (encrypted && !string.IsNullOrEmpty(v) && !v.StartsWith("ENC:", StringComparison.Ordinal))
                    v = "ENC:" + v;
                star.Cells[e.Key] = v;
            }
            wide.Rows.Add(star);
            wide.EnsureStarRow();

            // 旧 Values に紐づいていた購読（CollectionChanged + 各行 PropertyChanged）を解除
            if (_subscribedRows is not null)
            {
                foreach (var r in _subscribedRows) r.PropertyChanged -= OnDirtyTrigger;
                _subscribedRows.CollectionChanged -= OnValueRowsChanged;
                _subscribedRows = null;
            }
            if (_subscribedColumns is not null)
            {
                _subscribedColumns.CollectionChanged -= OnVariableColumnsChanged;
                _subscribedColumns = null;
            }

            CurrentData.Values = wide;

            // 新テーブルへの購読（Rows / 各行 / VariableColumns）を貼り直す
            CurrentData.Values.Rows.CollectionChanged += OnValueRowsChanged;
            foreach (var r in CurrentData.Values.Rows) r.PropertyChanged += OnDirtyTrigger;
            _subscribedRows = CurrentData.Values.Rows;
            CurrentData.Values.VariableColumns.CollectionChanged += OnVariableColumnsChanged;
            _subscribedColumns = CurrentData.Values.VariableColumns;

            // 移行操作自体を未保存編集としてマーク（保存ボタンで wide format 書出し）
            MarkDirty();

            IsLegacyValuesDetected = false;
            // CurrentData の Values 差し替えだけでは binding 一部が再評価されないため、
            // CurrentData プロパティを一旦 null にして set し直す手もあるが、
            // 今は派生プロパティを明示通知することで grid 再構築を促す。
            OnPropertyChanged(nameof(CurrentData));
            RebuildAvailableHostNames();

            MessageBox.Show(
                $"変換完了: {wide.VariableColumns.Count} 変数列を `*` 行に展開しました。\n保存ボタンで wide format として書き出してください。",
                "変換完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"変換エラー: {ex.Message}", "values.csv 形式変換",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
