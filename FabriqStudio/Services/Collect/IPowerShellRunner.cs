namespace FabriqStudio.Services.Collect;

/// <summary>
/// 採取で使う PowerShell / 外部コマンドの実行。Studio 全体は管理者で起動しないので、
/// 昇格が要る操作だけ <see cref="RunElevatedAsync"/>（UAC）で行う。
/// </summary>
public interface IPowerShellRunner
{
    /// <summary>非昇格で実行し、標準出力（UTF-8）を返す。</summary>
    Task<PowerShellResult> RunAsync(string script, CancellationToken ct = default);

    /// <summary>
    /// UAC で昇格して実行する。昇格プロセスの出力は取れないので、全ストリームを一時ファイルに落として返す。
    /// ユーザーが UAC を断ると <see cref="PowerShellResult.Cancelled"/>。
    /// </summary>
    Task<PowerShellResult> RunElevatedAsync(string script, CancellationToken ct = default);

    /// <summary>任意の実行ファイルを非昇格で実行し、標準出力を返す（reg.exe 等）。</summary>
    Task<PowerShellResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct = default);
}
