namespace FabriqStudio.Services.Master;

/// <summary>
/// 生成物の書き込み先パスを解決する唯一の場所。
/// 現在は <c>modules/&lt;tier&gt;/&lt;module&gt;/…</c> と <c>profiles/&lt;名&gt;.csv</c>。
/// fabriq 側の Profile Data Overlay（profiles/&lt;名&gt;/modules/…）が実装されたら
/// ここだけを切り替えれば Emitter 群は無変更で追随できる。
/// </summary>
public interface IMasterTargetResolver
{
    /// <summary>ワークスペースルート。未オープン時は例外。</summary>
    string RootPath { get; }

    /// <summary>profiles/ の絶対パス。</summary>
    string ProfilesDir { get; }

    /// <summary>モジュールディレクトリの絶対パス（standard → extended の順に探索）。無ければ null。</summary>
    string? FindModuleDir(string moduleDir);

    /// <summary>モジュール種別（"standard" / "extended"）。無ければ null。</summary>
    string? FindModuleKind(string moduleDir);

    /// <summary>モジュール設定 CSV の絶対パス（存在しなくてもパスは返す。モジュール自体が無ければ null）。</summary>
    string? GetModuleCsvPath(string moduleDir, string csvName);

    /// <summary>プロファイル CSV の絶対パス。</summary>
    string GetProfilePath(string profileName);

    /// <summary>ルートからの相対パス（表示・ICsvService 用、区切りは '/'）。</summary>
    string ToRelative(string absolutePath);
}
