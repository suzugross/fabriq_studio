using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FabriqStudio.Services;
using FabriqStudio.Views;

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

    /// <summary>
    /// テンプレートから新規ワークスペースを作成して開く。
    /// エラーは ErrorMessage プロパティに表示する（WelcomeView 内に表示）。
    /// </summary>
    [RelayCommand]
    private async Task CreateNewWorkspaceAsync()
    {
        // 1. 作成先の親フォルダを選択
        var parentDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "新規ワークスペースの作成先フォルダを選択してください"
        };
        if (parentDialog.ShowDialog() != true) return;

        // 2. 新しいフォルダ名を入力
        var folderName = NewWorkspaceDialog.Show(parentDialog.FolderName);
        if (string.IsNullOrWhiteSpace(folderName)) return;

        var targetPath = Path.Combine(parentDialog.FolderName, folderName);

        // 3. 既存チェック
        if (Directory.Exists(targetPath))
        {
            ErrorMessage = $"フォルダ「{folderName}」は既に存在します。別のフォルダ名を指定してください。";
            return;
        }

        // 4. テンプレートをコピー
        ErrorMessage = null;
        var error = await _workspace.CreateFromTemplateAsync(targetPath);
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }

        // 5. 作成したワークスペースを開く（WorkspaceChanged → MainViewModel が画面遷移）
        try
        {
            _workspace.Open(targetPath);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
