namespace FabriqStudio.Services;

/// <summary>
/// AES-256-CBC / PBKDF2-HMAC-SHA256 による暗号化・復号サービス。
/// PowerShell 側 (Unprotect-FabriqValue) と完全互換。
/// </summary>
public interface ICryptoService
{
    /// <summary>現在セッションのマスターパスフレーズ。未設定時は null。</summary>
    string? MasterPassphrase { get; set; }

    /// <summary>パスフレーズが設定済みかどうか。</summary>
    bool HasPassphrase { get; }

    /// <summary>平文を暗号化し "ENC:" + Base64 文字列を返す。</summary>
    string Encrypt(string plainText, string passphrase);

    /// <summary>"ENC:" プレフィクス付き暗号文を復号し平文を返す。</summary>
    string Decrypt(string cipherText, string passphrase);
}
