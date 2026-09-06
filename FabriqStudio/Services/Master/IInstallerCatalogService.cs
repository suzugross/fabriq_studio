using FabriqStudio.Models.Master;

namespace FabriqStudio.Services.Master;

/// <summary>
/// インストーラのサイレント引数辞書（master_template/installer_catalog.json）と、
/// exe のインストーラ種別判定（Inno Setup / NSIS / InstallShield / WiX Burn）。
/// </summary>
public interface IInstallerCatalogService
{
    /// <summary>辞書を読み込む（無ければ空。判定だけで動く）。</summary>
    Task EnsureLoadedAsync();

    /// <summary>ファイル名の辞書照合 → exe の種別判定 → バージョン情報 の順で補完案を作る。</summary>
    InstallerSuggestion Suggest(string filePath);
}
