using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// Pianist プロファイルを Studio から fabriq エンジンに渡してテスト実行する。
///
/// 実装上のキーポイント:
/// - <c>powershell.exe -STA -EncodedCommand</c> で子プロセスを起動し、
///   <c>kernel/common.ps1</c> を dot-source した上で <c>pianist.ps1</c> を呼ぶ
///   （pianist.ps1 は kernel 関数群に強く依存し、単体実行不可のため）
/// - profile picker は <c>Import-ModuleCsv</c> をモック上書きすることで
///   合成 1 行を返し auto-skip させる（pianist.ps1:891 の <c>Items.Count -eq 1</c>
///   shortcut を利用）— workspace 側 pianist_list.csv を改変しない
/// - <see cref="ICryptoService.MasterPassphrase"/> をラッパスクリプト内で
///   <c>$global:FabriqMasterPassphrase</c> に注入し ENC: セルの復号を有効化
/// - GUI 操作待ちのため既定タイムアウトなし。<see cref="System.Threading.CancellationToken"/>
///   経由でユーザーがキャンセルしたときだけ <c>Process.Kill(entireProcessTree: true)</c>
///   する設計
/// </summary>
public interface IPianistTestRunService
{
    /// <summary>
    /// 指定 profile を fabriq エンジンで実行し、stdout/stderr ログとプロセス情報を返す。
    /// 子プロセスは pianist の WinForms GUI を表示し、ユーザーが「Done / Cancel」で
    /// 終了するまでブロックする。
    /// </summary>
    /// <param name="profileName">対象 Pianist Profile 名（pianist_list.csv の ProfileName 列に相当）</param>
    /// <param name="newPCName">$env:SELECTED_NEW_PCNAME に流す値（"*" で `*` 行のみ解決）</param>
    /// <param name="ct">キャンセル時に子プロセスツリー全体を Kill する</param>
    /// <returns>ログ + ExitCode + ModuleResult（解析できれば）</returns>
    Task<PianistTestRunResult> RunAsync(
        string profileName,
        string newPCName,
        CancellationToken ct = default);
}

/// <summary>
/// Pianist テスト実行 1 件分の結果。
///
/// <see cref="ModuleResultStatus"/> は pianist.ps1 が末尾に出力する Sentinel
/// （<c>===PIANIST_TEST_RESULT===</c> 直後の JSON）を parse して取得する。
/// プロセスがクラッシュ / Kill された場合は null。
/// </summary>
public record PianistTestRunResult(
    string  Log,
    int     ExitCode,
    string? ModuleResultStatus,
    string? ModuleResultMessage,
    bool?   ModuleResultVerified,
    bool    WasCancelled);
