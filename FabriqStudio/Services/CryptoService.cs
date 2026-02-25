using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FabriqStudio.Services;

/// <summary>
/// AES-256-CBC / PBKDF2-HMAC-SHA256 暗号化・復号の実装。
/// 共通パラメータは PowerShell 側 (kernel/common.ps1 Unprotect-FabriqValue) と厳密一致。
///
/// ■ アルゴリズム仕様
///   鍵導出   : PBKDF2-HMAC-SHA256, 100,000 iterations, 固定ソルト
///   暗号化   : AES-256-CBC, PKCS7 padding
///   エンコード: UTF-8（平文）, Base64（暗号文）
///   Key/IV   : GetBytes(32) → Key, GetBytes(16) → IV（呼び出し順序厳守）
/// </summary>
public class CryptoService : ICryptoService
{
    // ── 共通パラメータ（PowerShell 側と完全一致させること）──
    private static readonly byte[] FixedSalt =
        Encoding.UTF8.GetBytes("fabriq-fixed-salt-2024");
    private const int Iterations = 100_000;
    private const int KeySize    = 32;   // AES-256
    private const int IvSize     = 16;   // AES block size (128 bit)
    private const string EncPrefix = "ENC:";

    public string? MasterPassphrase { get; set; }

    public bool HasPassphrase => !string.IsNullOrEmpty(MasterPassphrase);

    public string Encrypt(string plainText, string passphrase)
    {
        var (key, iv) = DeriveKeyIv(passphrase);

        using var aes = Aes.Create();
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key     = key;
        aes.IV      = iv;

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            cs.Write(plainBytes, 0, plainBytes.Length);
        }
        return EncPrefix + Convert.ToBase64String(ms.ToArray());
    }

    public string Decrypt(string cipherText, string passphrase)
    {
        var base64 = cipherText.StartsWith(EncPrefix, StringComparison.Ordinal)
            ? cipherText[EncPrefix.Length..]
            : cipherText;

        var (key, iv) = DeriveKeyIv(passphrase);

        using var aes = Aes.Create();
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key     = key;
        aes.IV      = iv;

        var cipherBytes = Convert.FromBase64String(base64);
        using var ms = new MemoryStream(cipherBytes);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static (byte[] Key, byte[] IV) DeriveKeyIv(string passphrase)
    {
        using var kdf = new Rfc2898DeriveBytes(
            passphrase, FixedSalt, Iterations, HashAlgorithmName.SHA256);
        var key = kdf.GetBytes(KeySize);
        var iv  = kdf.GetBytes(IvSize);
        return (key, iv);
    }
}
