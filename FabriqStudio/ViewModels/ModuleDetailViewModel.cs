using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;
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
/// モジュール詳細表示／編集
///   - module.csv: メニュー名・カテゴリ等のメタ情報 + ロック/解除トグル + Dirty 検知 + 保存
///   - guide.txt: テキスト表示 + ロック/解除トグル + Dirty 検知 + 保存
///   - 汎用CSV: DataTable で動的表示 + DataTable 組み込みの RowChanged で Dirty 検知 + 保存
///
/// ロック機構:
///   IsLocked=true（初期値）→ module.csv / guide.txt TextBox / DataGrid が読み取り専用
///   IsLocked=false          → 編集可能
///
/// 保存:
///   CanExecute = (IsGuideDirty || HasCsvChanges || HasModuleCsvChanges) &amp;&amp; !IsLocked
/// </summary>
public partial class ModuleDetailViewModel : ObservableObject
{
    private readonly IFileService                _fileService;
    private readonly ICsvService                 _csvService;
    private readonly IWorkspaceService            _workspace;
    private readonly IRegistryCollectionService   _registryCollection;
    private readonly ICryptoService               _crypto;

    // ─── モジュール情報 ───────────────────────────────────────────
    [ObservableProperty] private ModuleMasterEntry? _module;

    /// <summary>現在のモジュールがレジストリ設定モジュール（reg_hklm_config / reg_hkcu_config）か。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromCollectionCommand))]
    private bool _isRegistryModule;

    /// <summary>Picker 起動時に渡す対象 Hive（"HKLM" or "HKCU"）。非レジストリモジュールでは null。</summary>
    private string? _detectedHive;

    // ─── ロック ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFromCollectionCommand))]
    private bool _isLocked = true;

    // ─── guide.txt ───────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuideDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _guideText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuideDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _originalGuideText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasGuideText;

    /// <summary>guide.txt が元の内容から変更されているか。</summary>
    public bool IsGuideDirty =>
        HasGuideText && GuideText != OriginalGuideText;

    // ─── 汎用CSV ─────────────────────────────────────────────────
    [ObservableProperty] private DataTable _configCsvData = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCsvRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFromCollectionCommand))]
    private bool _hasConfigCsv;
    [ObservableProperty] private string?   _configCsvFileName;

    /// <summary>module.csv 以外の CSV ファイル名一覧（View のドロップダウン用）。</summary>
    [ObservableProperty] private ObservableCollection<string> _configCsvFiles = [];

    /// <summary>現在選択中の CSV ファイル名。変更時に LoadSelectedCsvAsync が発火する。</summary>
    [ObservableProperty] private string? _selectedConfigCsvFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasCsvChanges;

    // ─── module.csv メタ情報 ──────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasModuleCsvChanges;

    /// <summary>Load 中の TextChanged による誤検知を抑制するフラグ。</summary>
    private bool _suppressModuleCsvDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCsvRowCommand))]
    private System.Data.DataRowView? _selectedCsvRow;

    // ─── 状態 ────────────────────────────────────────────────────
    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private string? _errorMessage;

    // ファイルパス（保存時に使用）
    private string? _guidePath;
    private string? _csvFilePath;
    private string? _moduleDir;

    /// <summary>初期ロード中の CSV 切り替え発火を抑制するフラグ。</summary>
    private bool _suppressCsvSwitch;

    public ModuleDetailViewModel(
        IFileService              fileService,
        ICsvService               csvService,
        IWorkspaceService         workspace,
        IRegistryCollectionService registryCollection,
        ICryptoService             crypto)
    {
        _fileService        = fileService;
        _csvService         = csvService;
        _workspace          = workspace;
        _registryCollection = registryCollection;
        _crypto             = crypto;
    }

    /// <summary>View の TextChanged から呼ばれ、module.csv 変更フラグを立てる。</summary>
    public void MarkModuleCsvDirty()
    {
        if (!_suppressModuleCsvDirty)
            HasModuleCsvChanges = true;
    }

    /// <summary>選択されたモジュールを読み込む。</summary>
    public void Load(ModuleMasterEntry module)
    {
        _suppressModuleCsvDirty = true;
        Module     = module;
        IsLocked   = true;
        SaveStatus = null;
        SaveError  = null;
        HasModuleCsvChanges = false;

        // レジストリモジュール判定
        var dir = module.ModuleDir ?? "";
        if (dir.Contains("reg_hklm_config", StringComparison.OrdinalIgnoreCase))
        {
            IsRegistryModule = true;
            _detectedHive    = "HKLM";
        }
        else if (dir.Contains("reg_hkcu_config", StringComparison.OrdinalIgnoreCase))
        {
            IsRegistryModule = true;
            _detectedHive    = "HKCU";
        }
        else
        {
            IsRegistryModule = false;
            _detectedHive    = null;
        }

        _ = LoadFilesAsync(module);
    }

    private async Task LoadFilesAsync(ModuleMasterEntry module)
    {
        _suppressCsvSwitch = true;      // CSV 切り替えハンドラを抑制
        IsLoading          = true;
        ErrorMessage       = null;

        DetachCsvHandlers();
        GuideText             = null;
        OriginalGuideText     = null;
        HasGuideText          = false;
        ConfigCsvData         = new DataTable();
        HasConfigCsv          = false;
        HasCsvChanges         = false;
        ConfigCsvFileName     = null;
        SelectedConfigCsvFile = null;
        ConfigCsvFiles.Clear();
        _guidePath            = null;
        _csvFilePath          = null;
        _moduleDir            = null;

        try
        {
            var root      = _workspace.RootPath
                ?? throw new InvalidOperationException(
                    "ワークスペースが開かれていません。fabriq フォルダを選択してください。");
            var moduleDir = Path.Combine(root, "modules", module.Kind, module.ModuleDir);

            // ── guide.txt ──────────────────────────────────────────
            _guidePath = Path.Combine(moduleDir, "guide.txt");
            var guideText = await _fileService.ReadTextAsync(_guidePath);
            GuideText         = guideText;
            OriginalGuideText = guideText;
            HasGuideText      = guideText is not null;

            // ── module.csv 以外の CSV 一覧を取得し、先頭を選択 ──────
            _moduleDir = moduleDir;

            if (Directory.Exists(moduleDir))
            {
                var csvNames = Directory
                    .GetFiles(moduleDir, "*.csv")
                    .Where(f => !string.Equals(
                        Path.GetFileName(f), "module.csv",
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .Select(f => Path.GetFileName(f))
                    .ToList();

                foreach (var name in csvNames)
                    ConfigCsvFiles.Add(name);

                // 先頭を選択して CSV をロード
                // （_suppressCsvSwitch で partial method の二重発火を防いでいるため直接 await する）
                SelectedConfigCsvFile = csvNames.FirstOrDefault();
                await LoadSelectedCsvAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            // Load() で立てた抑制フラグを非同期ロード完了後に解除。
            // View の DataTemplate 生成・バインディング初期評価による
            // TextChanged の誤検知がここまでに完了していることを保証する。
            _suppressCsvSwitch      = false;   // CSV 切り替えハンドラを再開
            _suppressModuleCsvDirty = false;
            HasModuleCsvChanges = false;
        }
    }

    private void OnCsvRowChanged(object sender, DataRowChangeEventArgs e)
        => HasCsvChanges = true;

    // ── CSV 切り替え ─────────────────────────────────────────────

    /// <summary>
    /// SelectedConfigCsvFile が変更されたときに呼ばれる。
    /// 初期ロード中は _suppressCsvSwitch で抑制される。
    /// </summary>
    partial void OnSelectedConfigCsvFileChanged(string? value)
    {
        if (!_suppressCsvSwitch)
            _ = LoadSelectedCsvAsync();
    }

    /// <summary>現在の ConfigCsvData から RowChanged/RowDeleted ハンドラを解除する。</summary>
    private void DetachCsvHandlers()
    {
        ConfigCsvData.RowChanged -= OnCsvRowChanged;
        ConfigCsvData.RowDeleted -= OnCsvRowChanged;
    }

    /// <summary>
    /// SelectedConfigCsvFile に対応する CSV を読み込み、ConfigCsvData を差し替える。
    /// 旧 DataTable のイベントハンドラ解除 → 新 DataTable のロード → ハンドラ登録 を行う。
    /// </summary>
    private async Task LoadSelectedCsvAsync()
    {
        DetachCsvHandlers();
        ConfigCsvData     = new DataTable();
        HasConfigCsv      = false;
        HasCsvChanges     = false;
        ConfigCsvFileName = null;
        _csvFilePath      = null;

        if (SelectedConfigCsvFile is null || _moduleDir is null) return;

        try
        {
            var csvFile       = Path.Combine(_moduleDir, SelectedConfigCsvFile);
            _csvFilePath      = csvFile;
            var table         = await _fileService.ReadCsvAsDataTableAsync(csvFile);
            HasConfigCsv      = table.Columns.Count > 0;
            ConfigCsvFileName = SelectedConfigCsvFile;

            // AcceptChanges を先に呼び初期状態をクリーンにしてからイベント購読する。
            // 逆順だと AcceptChanges が RowChanged を発火し HasCsvChanges が即 true になる。
            table.AcceptChanges();
            table.RowChanged += OnCsvRowChanged;
            table.RowDeleted += OnCsvRowChanged;
            ConfigCsvData = table;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"CSV 読み込みエラー: {ex.Message}";
        }
    }

    // ── ロック切り替え ────────────────────────────────────────────
    [RelayCommand]
    private void ToggleLock() => IsLocked = !IsLocked;

    // ── CSV 行追加 ────────────────────────────────────────────────
    private bool CanAddCsvRow() => HasConfigCsv && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanAddCsvRow))]
    private void AddCsvRow()
    {
        var row = ConfigCsvData.NewRow();
        foreach (DataColumn col in ConfigCsvData.Columns)
            row[col] = "";
        ConfigCsvData.Rows.Add(row);
    }

    // ── CSV 行削除 ────────────────────────────────────────────────
    private bool CanDeleteCsvRow() => SelectedCsvRow is not null && HasConfigCsv && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanDeleteCsvRow))]
    private void DeleteCsvRow()
    {
        if (SelectedCsvRow is null) return;
        SelectedCsvRow.Row.Delete();
        SelectedCsvRow = null;
    }

    // ── 保存コマンド ──────────────────────────────────────────────
    private bool CanSave() => (IsGuideDirty || HasCsvChanges || HasModuleCsvChanges) && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        SaveError  = null;
        SaveStatus = null;

        try
        {
            // guide.txt 保存
            if (IsGuideDirty && _guidePath is not null && GuideText is not null)
            {
                await _fileService.WriteTextAsync(_guidePath, GuideText);
                OriginalGuideText = GuideText;   // スナップショット更新 → ハイライト解除
            }

            // module.csv 保存
            if (HasModuleCsvChanges && Module is not null)
            {
                var relativePath = Path.Combine("modules", Module.Kind, Module.ModuleDir, "module.csv");
                await _csvService.WriteAsync(relativePath, new[] { Module });
                HasModuleCsvChanges = false;
            }

            // 汎用CSV 保存
            if (HasCsvChanges && _csvFilePath is not null)
            {
                await _fileService.WriteCsvFromDataTableAsync(_csvFilePath, ConfigCsvData);
                ConfigCsvData.AcceptChanges();   // RowState リセット → Dirty 解除
                HasCsvChanges = false;
            }

            SaveStatus = "✓ 保存しました";
            WeakReferenceMessenger.Default.Send(new WorkspaceDataUpdatedMessage("ModuleDetail"));
        }
        catch (Exception ex)
        {
            SaveError = $"保存エラー: {ex.Message}";
        }
    }

    // ── 辞書から追加 ─────────────────────────────────────────────
    private bool CanAddFromCollection() => HasConfigCsv && !IsLocked && IsRegistryModule;

    [RelayCommand(CanExecute = nameof(CanAddFromCollection))]
    private void AddFromCollection()
    {
        var entry = RegistryPickerWindow.Show(
            _registryCollection.Entries,
            Application.Current.MainWindow,
            _detectedHive);

        if (entry is null) return;

        var row = ConfigCsvData.NewRow();
        SetIfColumnExists(row, "Enabled",      "1");
        SetIfColumnExists(row, "AdminID",      NextAdminId().ToString());
        SetIfColumnExists(row, "SettingTitle",  entry.Title);
        SetIfColumnExists(row, "KeyPath",       entry.KeyPath);
        SetIfColumnExists(row, "KeyName",       entry.KeyName);
        SetIfColumnExists(row, "Type",          entry.Type);
        SetIfColumnExists(row, "Value",         entry.Value);
        ConfigCsvData.Rows.Add(row);
    }

    private static void SetIfColumnExists(DataRow row, string columnName, string value)
    {
        if (row.Table.Columns.Contains(columnName))
            row[columnName] = value;
    }

    private int NextAdminId()
    {
        if (!ConfigCsvData.Columns.Contains("AdminID")) return 1;

        var max = 0;
        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (int.TryParse(row["AdminID"]?.ToString(), out var id) && id > max)
                max = id;
        }
        return max + 1;
    }

    // ── セル暗号化・復号（View の code-behind から呼び出し）────────
    /// <summary>指定セルの値を暗号化する。戻り値 = エラーメッセージ（成功時 null）。</summary>
    public string? EncryptCell(System.Data.DataRowView row, string columnName)
    {
        if (!_crypto.HasPassphrase)
            return "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";

        var value = row[columnName]?.ToString() ?? "";
        if (string.IsNullOrEmpty(value))
            return "空のセルは暗号化できません。";
        if (value.StartsWith("ENC:", StringComparison.Ordinal))
            return "このセルは既に暗号化されています。";

        row[columnName] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
        return null;
    }

    /// <summary>指定セルの値を復号する。戻り値 = エラーメッセージ（成功時 null）。</summary>
    public string? DecryptCell(System.Data.DataRowView row, string columnName)
    {
        if (!_crypto.HasPassphrase)
            return "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";

        var value = row[columnName]?.ToString() ?? "";
        if (!value.StartsWith("ENC:", StringComparison.Ordinal))
            return "このセルは暗号化されていません（ENC: プレフィクスがありません）。";

        try
        {
            row[columnName] = _crypto.Decrypt(value, _crypto.MasterPassphrase!);
            return null;
        }
        catch (Exception ex)
        {
            return $"復号に失敗しました。パスフレーズが正しいか確認してください。\n{ex.Message}";
        }
    }

    // ── 列一括暗号化・復号 ─────────────────────────────────────────

    /// <summary>指定列の全行を暗号化する。</summary>
    public BatchCryptoResult EncryptColumn(string columnName)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);
        if (!CryptoHelper.IsEncryptableColumn(columnName))
            return new BatchCryptoResult(0, 0, [$"列 '{columnName}' は暗号化対象外です。"]);

        int processed = 0, skipped = 0;
        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var value = row[columnName]?.ToString() ?? "";
            if (string.IsNullOrEmpty(value) || value.StartsWith("ENC:", StringComparison.Ordinal))
            { skipped++; continue; }
            row[columnName] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
            processed++;
        }
        return new BatchCryptoResult(processed, skipped, []);
    }

    /// <summary>指定列の全行を復号する。</summary>
    public BatchCryptoResult DecryptColumn(string columnName)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var value = row[columnName]?.ToString() ?? "";
            if (!value.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
            try { row[columnName] = _crypto.Decrypt(value, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"行{ConfigCsvData.Rows.IndexOf(row) + 1}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    // ── 行一括暗号化・復号 ─────────────────────────────────────────

    /// <summary>指定行の全暗号化可能列を暗号化する。</summary>
    public BatchCryptoResult EncryptRow(DataRowView rowView)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        foreach (DataColumn col in ConfigCsvData.Columns)
        {
            if (!CryptoHelper.IsEncryptableColumn(col.ColumnName)) { skipped++; continue; }
            var value = rowView[col.ColumnName]?.ToString() ?? "";
            if (string.IsNullOrEmpty(value) || value.StartsWith("ENC:", StringComparison.Ordinal))
            { skipped++; continue; }
            rowView[col.ColumnName] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
            processed++;
        }
        return new BatchCryptoResult(processed, skipped, []);
    }

    /// <summary>指定行の全暗号化済み列を復号する。</summary>
    public BatchCryptoResult DecryptRow(DataRowView rowView)
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        foreach (DataColumn col in ConfigCsvData.Columns)
        {
            if (!CryptoHelper.IsEncryptableColumn(col.ColumnName)) { skipped++; continue; }
            var value = rowView[col.ColumnName]?.ToString() ?? "";
            if (!value.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
            try { rowView[col.ColumnName] = _crypto.Decrypt(value, _crypto.MasterPassphrase!); processed++; }
            catch (Exception ex) { errors.Add($"{col.ColumnName}: {ex.Message}"); }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    // ── テーブル全体暗号化・復号 ────────────────────────────────────

    /// <summary>テーブル全体の暗号化可能セルを一括暗号化する。</summary>
    public BatchCryptoResult EncryptAll()
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var encryptableCols = ConfigCsvData.Columns.Cast<DataColumn>()
            .Where(c => CryptoHelper.IsEncryptableColumn(c.ColumnName)).ToList();

        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            foreach (var col in encryptableCols)
            {
                var value = row[col]?.ToString() ?? "";
                if (string.IsNullOrEmpty(value) || value.StartsWith("ENC:", StringComparison.Ordinal))
                { skipped++; continue; }
                row[col] = _crypto.Encrypt(value, _crypto.MasterPassphrase!);
                processed++;
            }
        }
        return new BatchCryptoResult(processed, skipped, []);
    }

    /// <summary>テーブル全体の暗号化済みセルを一括復号する。</summary>
    public BatchCryptoResult DecryptAll()
    {
        var error = CryptoHelper.ValidatePassphrase(_crypto);
        if (error is not null) return new BatchCryptoResult(0, 0, [error]);

        int processed = 0, skipped = 0;
        var errors = new List<string>();
        var encryptableCols = ConfigCsvData.Columns.Cast<DataColumn>()
            .Where(c => CryptoHelper.IsEncryptableColumn(c.ColumnName)).ToList();

        foreach (DataRow row in ConfigCsvData.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            var rowIdx = ConfigCsvData.Rows.IndexOf(row) + 1;
            foreach (var col in encryptableCols)
            {
                var value = row[col]?.ToString() ?? "";
                if (!value.StartsWith("ENC:", StringComparison.Ordinal)) { skipped++; continue; }
                try { row[col] = _crypto.Decrypt(value, _crypto.MasterPassphrase!); processed++; }
                catch (Exception ex) { errors.Add($"行{rowIdx}/{col.ColumnName}: {ex.Message}"); }
            }
        }
        return new BatchCryptoResult(processed, skipped, errors);
    }

    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("ModuleEdit"));
}
