using System.IO;
using FabriqStudio.Services;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>
/// ワークスペースの検証トークン（kernel/txt/passphrase_verify.txt）の扱い。
/// 期待値は fabriq 本体の kernel/common.ps1 Test-MasterPassphrase と
/// kernel/main.ps1 $verifyTokenPath に合わせる（固定平文 "surkitinisme" / "ENC:" 始まり）。
/// </summary>
public sealed class PassphraseServiceTests
{
    private const string TokenRel   = "kernel/txt/passphrase_verify.txt";
    private const string VerifyText = "surkitinisme";

    private static (PassphraseService Svc, CryptoService Crypto) Build(string? root)
    {
        var crypto = new CryptoService();
        return (new PassphraseService(new StubWorkspace(root), crypto), crypto);
    }

    // ── 設定済み判定 ─────────────────────────────────────────────

    [Fact]
    public void NotConfigured_WhenTokenMissing()
    {
        using var ws = new TempWorkspace();
        var (svc, _) = Build(ws.Root);

        Assert.False(svc.IsConfiguredInWorkspace);
        Assert.False(svc.Verify("anything"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("surkitinisme")]              // 平文のまま置かれている（fabriq 側も無効と見なす）
    [InlineData("Zm9vYmFy")]                  // ENC: プレフィクスなし
    public void NotConfigured_WhenTokenInvalid(string content)
    {
        using var ws = new TempWorkspace();
        ws.File(TokenRel, content);
        var (svc, _) = Build(ws.Root);

        Assert.False(svc.IsConfiguredInWorkspace);
    }

    [Fact]
    public void Configured_WhenTokenValid()
    {
        using var ws = new TempWorkspace();
        var (svc, crypto) = Build(ws.Root);
        ws.File(TokenRel, crypto.Encrypt(VerifyText, "pass-1"));

        Assert.True(svc.IsConfiguredInWorkspace);
        Assert.True(svc.Verify("pass-1"));
        Assert.False(svc.Verify("pass-2"));
        Assert.False(svc.Verify(""));
    }

    [Fact]
    public void TokenPath_MatchesFabriqLocation()
    {
        using var ws = new TempWorkspace();
        var (svc, _) = Build(ws.Root);

        Assert.Equal(Path.Combine(ws.Root, @"kernel\txt\passphrase_verify.txt"), svc.TokenPath);
        Assert.Null(Build(null).Svc.TokenPath);
    }

    // ── 初回設定 ─────────────────────────────────────────────────

    [Fact]
    public void Apply_WritesToken_AndIsPowerShellCompatible()
    {
        using var ws = new TempWorkspace();
        var (svc, crypto) = Build(ws.Root);

        Assert.Null(svc.Apply("pass-1"));
        Assert.Equal("pass-1", crypto.MasterPassphrase);
        Assert.True(svc.IsConfiguredInWorkspace);

        // fabriq 本体（Test-MasterPassphrase）が読める形か
        var token = File.ReadAllText(ws.Abs(TokenRel)).Trim();
        Assert.StartsWith("ENC:", token);
        Assert.Equal(VerifyText, crypto.Decrypt(token, "pass-1"));
    }

    [Fact]
    public void Apply_Fails_WithoutWorkspace()
    {
        var (svc, crypto) = Build(null);

        Assert.NotNull(svc.Apply("pass-1"));
        Assert.Null(crypto.MasterPassphrase);
    }

    [Fact]
    public void Apply_Fails_OnEmptyPassphrase()
    {
        using var ws = new TempWorkspace();
        var (svc, crypto) = Build(ws.Root);

        Assert.NotNull(svc.Apply(""));
        Assert.Null(crypto.MasterPassphrase);
        Assert.False(svc.IsConfiguredInWorkspace);
    }

    // ── 設定済みワークスペースへの適用 ───────────────────────────

    [Fact]
    public void Apply_Succeeds_WhenPassphraseMatches()
    {
        using var ws = new TempWorkspace();
        var (svc, crypto) = Build(ws.Root);
        svc.Apply("pass-1");
        var before = File.ReadAllText(ws.Abs(TokenRel));
        crypto.MasterPassphrase = null;

        Assert.Null(svc.Apply("pass-1"));
        Assert.Equal("pass-1", crypto.MasterPassphrase);
        Assert.Equal(before, File.ReadAllText(ws.Abs(TokenRel)));
    }

    /// <summary>
    /// 設定済みのワークスペースでは別のパスフレーズを受け付けない。
    /// トークンだけ差し替えると、既存の ENC: 値が二度と復号できなくなる。
    /// </summary>
    [Fact]
    public void Apply_Rejects_DifferentPassphrase_OnConfiguredWorkspace()
    {
        using var ws = new TempWorkspace();
        var (svc, crypto) = Build(ws.Root);
        svc.Apply("pass-1");
        var before = File.ReadAllText(ws.Abs(TokenRel));

        Assert.NotNull(svc.Apply("pass-2"));
        Assert.Equal("pass-1", crypto.MasterPassphrase);              // セッションは変わらない
        Assert.Equal(before, File.ReadAllText(ws.Abs(TokenRel)));     // トークンも変わらない
    }

    // ── セッション解除 ───────────────────────────────────────────

    [Fact]
    public void ClearSession_KeepsToken()
    {
        using var ws = new TempWorkspace();
        var (svc, crypto) = Build(ws.Root);
        svc.Apply("pass-1");

        svc.ClearSession();

        Assert.False(crypto.HasPassphrase);
        Assert.True(svc.IsConfiguredInWorkspace);   // ワークスペース側は設定済みのまま
        Assert.True(svc.Verify("pass-1"));
    }
}
