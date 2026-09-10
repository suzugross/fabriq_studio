using System.IO;

namespace FabriqStudio.Services;

/// <summary>
/// <see cref="IPassphraseService"/> の実装。検証トークンの読み書きと照合のみを持ち、
/// 暗号化そのものは <see cref="ICryptoService"/> に委譲する。
/// </summary>
public class PassphraseService : IPassphraseService
{
    // ── fabriq 本体と一致させる定数（kernel/common.ps1 Test-MasterPassphrase）──
    private const string VerifyPlainText = "surkitinisme";
    private const string TokenRelPath    = @"kernel\txt\passphrase_verify.txt";
    private const string EncPrefix       = "ENC:";

    private readonly IWorkspaceService _workspace;
    private readonly ICryptoService    _crypto;

    public PassphraseService(IWorkspaceService workspace, ICryptoService crypto)
    {
        _workspace = workspace;
        _crypto    = crypto;
    }

    public string? TokenPath
        => _workspace.RootPath is { } root ? Path.Combine(root, TokenRelPath) : null;

    public bool IsConfiguredInWorkspace => ReadToken() is not null;

    public bool Verify(string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase)) return false;

        var token = ReadToken();
        if (token is null) return false;

        try
        {
            return _crypto.Decrypt(token, passphrase) == VerifyPlainText;
        }
        catch
        {
            // 復号失敗（＝パスフレーズ不一致 / トークン破損）
            return false;
        }
    }

    public string? Apply(string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            return "パスフレーズが空です。";

        var path = TokenPath;
        if (path is null)
            return "ワークスペースが開かれていません。";

        if (!Verify(passphrase))
        {
            // 設定済みのワークスペースでは上書きしない。
            // 既存の ENC: 値は元のパスフレーズでしか復号できないため、
            // トークンだけを差し替えると値が失われる。
            if (IsConfiguredInWorkspace)
                return "パスフレーズが正しくありません。";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, _crypto.Encrypt(VerifyPlainText, passphrase));
            }
            catch (Exception ex)
            {
                // 書き出せないまま暗号化を許すと、fabriq 本体が照合できない値を作ってしまう
                return $"検証トークンを書き出せませんでした。\n{path}\n{ex.Message}";
            }
        }

        _crypto.MasterPassphrase = passphrase;
        return null;
    }

    public void ClearSession() => _crypto.MasterPassphrase = null;

    /// <summary>
    /// 検証トークンを読む。有効な形式（非空・"ENC:" 始まり）でなければ null。
    /// fabriq 側も同じ条件で「無効」と扱う。
    /// </summary>
    private string? ReadToken()
    {
        var path = TokenPath;
        if (path is null || !File.Exists(path)) return null;

        try
        {
            var token = File.ReadAllText(path).Trim();
            return token.StartsWith(EncPrefix, StringComparison.Ordinal) ? token : null;
        }
        catch
        {
            return null;
        }
    }
}
