using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 端末詳細表示／編集 — HostListView でダブルクリックされた HostEntry を表示する。
///
/// ロック機構:
///   IsLocked=true（初期値）→ 全 TextBox が読み取り専用
///   IsLocked=false          → 編集可能（TwoWay バインド経由で Host を直接更新）
///
/// Dirty 検知:
///   Load() 時に OriginalHost スナップショットを作成（JSON Clone）。
///   Host.PropertyChanged を購読し、変更のたびに ContentEquals（JSON 比較）で IsDirty を更新。
///   View 側は DirtyToForegroundConverter（MultiBinding）でフィールド単位にハイライト。
///
/// 保存:
///   CanExecute = IsDirty &amp;&amp; !IsLocked（未変更またはロック中は保存ボタンを無効化）。
///   hostlist.csv 全行を read-modify-write（元のAdminIDで行を特定）。
///   保存後にスナップショットを更新して Dirty をリセット。
/// </summary>
public partial class HostDetailViewModel : ObservableObject
{
    private readonly ICsvService    _csvService;
    private readonly ICryptoService _crypto;

    // ─── 表示データ ────────────────────────────────────────────────
    [ObservableProperty] private HostEntry?                        _host;
    [ObservableProperty] private HostEntry?                        _originalHost;
    [ObservableProperty] private ObservableCollection<PrinterInfo> _printers = [];

    // ─── 状態 ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLocked = true;

    [ObservableProperty] private string? _saveStatus;
    [ObservableProperty] private string? _saveError;

    public HostDetailViewModel(ICsvService csvService, ICryptoService crypto)
    {
        _csvService = csvService;
        _crypto     = crypto;
    }

    /// <summary>選択された端末を読み込み、スナップショットを作成する。</summary>
    public void Load(HostEntry host)
    {
        // 前の Host の PropertyChanged を解除
        if (Host is not null)
            Host.PropertyChanged -= OnHostPropertyChanged;

        Host         = host;
        OriginalHost = host.Clone();
        IsLocked     = true;
        IsDirty      = false;
        SaveStatus   = null;
        SaveError    = null;
        Printers     = new ObservableCollection<PrinterInfo>(BuildPrinters(host));

        host.PropertyChanged += OnHostPropertyChanged;
    }

    // ── Host フィールド変更時: Dirty フラグ更新 ─────────────────
    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Host is null || OriginalHost is null) return;
        IsDirty = !Host.ContentEquals(OriginalHost);

        // プリンターフィールドが変わったら表示リストも更新
        if (e.PropertyName?.StartsWith("Printer", StringComparison.Ordinal) == true)
            Printers = new ObservableCollection<PrinterInfo>(BuildPrinters(Host));
    }

    // ── 保存コマンド ──────────────────────────────────────────────
    private bool CanSave() => IsDirty && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (Host is null || OriginalHost is null) return;
        SaveError  = null;
        SaveStatus = null;

        try
        {
            // 全行読み込み → 対象行を置換 → 全行書き戻し
            var allHosts = (await _csvService.ReadAsync<HostEntry>("kernel/csv/hostlist.csv"))
                           .ToList();

            var idx = allHosts.FindIndex(h =>
                string.Equals(h.AdminID, OriginalHost.AdminID, StringComparison.Ordinal));

            if (idx >= 0)
                allHosts[idx] = Host;
            else
                allHosts.Add(Host);

            await _csvService.WriteAsync("kernel/csv/hostlist.csv", allHosts);

            // スナップショット更新 → ハイライト解除
            OriginalHost = Host.Clone();
            IsDirty      = false;
            SaveStatus   = "✓ 保存しました";
            WeakReferenceMessenger.Default.Send(new WorkspaceDataUpdatedMessage("HostDetail"));
        }
        catch (Exception ex)
        {
            SaveError = $"保存エラー: {ex.Message}";
        }
    }

    // ── フィールド暗号化・復号（View の code-behind から呼び出し）────

    /// <summary>指定プロパティの値を暗号化する。戻り値 = エラーメッセージ（成功時 null）。</summary>
    public string? EncryptField(string propertyName)
    {
        if (Host is null) return "端末が選択されていません。";

        if (!_crypto.HasPassphrase)
            return "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";

        var prop = typeof(HostEntry).GetProperty(propertyName);
        if (prop is null) return $"プロパティ '{propertyName}' が見つかりません。";

        var value = prop.GetValue(Host)?.ToString() ?? "";
        if (string.IsNullOrEmpty(value))
            return "空のフィールドは暗号化できません。";
        if (value.StartsWith("ENC:", StringComparison.Ordinal))
            return "このフィールドは既に暗号化されています。";

        prop.SetValue(Host, _crypto.Encrypt(value, _crypto.MasterPassphrase!));
        return null;
    }

    /// <summary>指定プロパティの値を復号する。戻り値 = エラーメッセージ（成功時 null）。</summary>
    public string? DecryptField(string propertyName)
    {
        if (Host is null) return "端末が選択されていません。";

        if (!_crypto.HasPassphrase)
            return "パスフレーズが設定されていません。\n左ペイン下部の「🔑 パスフレーズ」から設定してください。";

        var prop = typeof(HostEntry).GetProperty(propertyName);
        if (prop is null) return $"プロパティ '{propertyName}' が見つかりません。";

        var value = prop.GetValue(Host)?.ToString() ?? "";
        if (!value.StartsWith("ENC:", StringComparison.Ordinal))
            return "このフィールドは暗号化されていません（ENC: プレフィクスがありません）。";

        try
        {
            prop.SetValue(Host, _crypto.Decrypt(value, _crypto.MasterPassphrase!));
            return null;
        }
        catch (Exception ex)
        {
            return $"復号に失敗しました。パスフレーズが正しいか確認してください。\n{ex.Message}";
        }
    }

    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("HostList"));

    // ── Printers 投影 ─────────────────────────────────────────────
    private static IEnumerable<PrinterInfo> BuildPrinters(HostEntry h) =>
    [
        new() { Number = 1,  Name = h.Printer1Name,  Driver = h.Printer1Driver,  Port = h.Printer1Port  },
        new() { Number = 2,  Name = h.Printer2Name,  Driver = h.Printer2Driver,  Port = h.Printer2Port  },
        new() { Number = 3,  Name = h.Printer3Name,  Driver = h.Printer3Driver,  Port = h.Printer3Port  },
        new() { Number = 4,  Name = h.Printer4Name,  Driver = h.Printer4Driver,  Port = h.Printer4Port  },
        new() { Number = 5,  Name = h.Printer5Name,  Driver = h.Printer5Driver,  Port = h.Printer5Port  },
        new() { Number = 6,  Name = h.Printer6Name,  Driver = h.Printer6Driver,  Port = h.Printer6Port  },
        new() { Number = 7,  Name = h.Printer7Name,  Driver = h.Printer7Driver,  Port = h.Printer7Port  },
        new() { Number = 8,  Name = h.Printer8Name,  Driver = h.Printer8Driver,  Port = h.Printer8Port  },
        new() { Number = 9,  Name = h.Printer9Name,  Driver = h.Printer9Driver,  Port = h.Printer9Port  },
        new() { Number = 10, Name = h.Printer10Name, Driver = h.Printer10Driver, Port = h.Printer10Port },
    ];
}
