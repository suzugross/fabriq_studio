using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Services;

namespace FabriqStudio.ViewModels;

/// <summary>
/// ワークスペース未選択時に表示する初期画面の ViewModel。
/// ユーザーが fabriq ルートフォルダを選択し、
/// IWorkspaceService.Open() が成功すると WorkspaceChanged イベントが発火し、
/// MainViewModel が自動的にメイン画面へ遷移する。
/// </summary>
public partial class WelcomeViewModel : ObservableObject
{
    private readonly IWorkspaceService _workspace;

    [ObservableProperty] private string? _errorMessage;

    public WelcomeViewModel(IWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    /// <summary>フォルダ選択ダイアログを開き、選択パスを IWorkspaceService に渡す。</summary>
    [RelayCommand]
    private void SelectFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "fabriq ルートフォルダを選択してください"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _workspace.Open(dialog.FolderName);
            ErrorMessage = null;
        }
        catch (ArgumentException ex)
        {
            // Validate() の失敗メッセージを画面に表示する
            ErrorMessage = ex.Message;
        }
    }
}
