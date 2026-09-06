using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Models.Gpo;
using FabriqStudio.Services.Gpo;

namespace FabriqStudio.ViewModels.Gpo;

/// <summary>
/// 「GPO 辞書からポリシーを追加／編集」ダイアログの VM。左に検索一覧、右に状態・要素の設定。
/// 追加時は <see cref="Result"/> に選択（<see cref="GpoSelection"/>）が入る。
/// </summary>
public sealed partial class GpoPickerDialogViewModel : ObservableObject
{
    private readonly IGpoCatalogService _service;
    private readonly GpoSelection?      _existing;

    public GpoBrowserViewModel      Browser { get; }
    public GpoPolicyConfigViewModel Config  { get; } = new();

    public string Title   => _existing is null ? "GPO 辞書からポリシーを追加" : "ポリシーの設定を編集";
    public string OkLabel => _existing is null ? "追加" : "更新";

    public GpoSelection? Result { get; private set; }

    /// <summary>true = 追加／更新で閉じる、false = キャンセル。</summary>
    public event Action<bool>? RequestClose;

    public GpoPickerDialogViewModel(IGpoCatalogService service, GpoSelection? existing)
    {
        _service  = service;
        _existing = existing;
        Browser   = new GpoBrowserViewModel(service);

        Browser.PropertyChanged += OnBrowserPropertyChanged;
        Config.Changed          += () => OkCommand.NotifyCanExecuteChanged();
        _service.CatalogChanged += OnCatalogChanged;

        if (existing is not null)
        {
            if (!Browser.SelectPolicy(existing.PolicyId))
                Browser.SearchText = existing.DisplayName;
        }
    }

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GpoBrowserViewModel.SelectedPolicy)) return;
        var p = Browser.SelectedPolicy;
        var reuse = _existing is not null && p is not null &&
                    p.Id.Equals(_existing.PolicyId, StringComparison.OrdinalIgnoreCase);
        Config.Load(p, reuse ? _existing : null);
        OkCommand.NotifyCanExecuteChanged();
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) Browser.RefreshCatalogState();
        else d.BeginInvoke(new Action(Browser.RefreshCatalogState));
    }

    private bool CanOk() => Config.HasPolicy && !Config.HasErrors;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok()
    {
        Result = Config.ToSelection();
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    /// <summary>ダイアログを閉じたら呼ぶ（辞書イベントの購読を外す）。</summary>
    public void Detach() => _service.CatalogChanged -= OnCatalogChanged;
}
