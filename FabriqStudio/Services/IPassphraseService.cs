namespace FabriqStudio.Services;

/// <summary>
/// ワークスペース（fabriq ルート）に紐づくマスターパスフレーズの管理。
/// <para>
/// パスフレーズ自体はどこにも保存しない。保存するのは検証トークン
/// <c>kernel/txt/passphrase_verify.txt</c> だけで、これは固定平文
/// "surkitinisme" を当該パスフレーズで暗号化した "ENC:" 文字列である。
/// fabriq 本体（kernel/common.ps1 Test-MasterPassphrase / main.ps1）が
/// 同じファイル・同じ平文で照合するため、書式を変えてはならない。
/// </para>
/// <para>
/// セッションのパスフレーズ（<see cref="ICryptoService.MasterPassphrase"/>）は
/// ワークスペースに属する。別のワークスペースへ持ち越すと、そのワークスペースの
/// トークンで検証できない値を書き込んでしまうため、持ち越しは呼び出し側で断つ。
/// </para>
/// </summary>
public interface IPassphraseService
{
    /// <summary>検証トークンの絶対パス。ワークスペース未オープン時は null。</summary>
    string? TokenPath { get; }

    /// <summary>
    /// 現在のワークスペースにパスフレーズが設定済みか。
    /// 判定は fabriq 側と同じ（トークンが存在し、空でなく、"ENC:" で始まる）。
    /// </summary>
    bool IsConfiguredInWorkspace { get; }

    /// <summary>
    /// 指定パスフレーズが現在のワークスペースのトークンと合致するか。
    /// トークンが無い / 不正 / 復号失敗の場合は false。
    /// </summary>
    bool Verify(string passphrase);

    /// <summary>
    /// パスフレーズをセッションに適用する。未設定のワークスペースなら検証トークンを書き出す。
    /// 設定済みのワークスペースに別のパスフレーズを適用することはできない
    /// （既存の ENC: 値が復号不能になるため。リセットはトークンの手動削除で行う）。
    /// </summary>
    /// <returns>null = 成功、string = ユーザー表示用エラーメッセージ</returns>
    string? Apply(string passphrase);

    /// <summary>
    /// セッションのパスフレーズを解除する。検証トークンは削除しない
    /// （＝ワークスペースの「設定済み」状態は変わらない）。
    /// </summary>
    void ClearSession();
}
