namespace FabriqStudio.Services.Collect;

/// <summary>この PC のサードパーティドライバの採取（fabriq の driver_config Export と同じ規則）。</summary>
public interface IDriverExportService
{
    /// <summary>この PC のモデル名（SMBIOS。fabriq は Win32_ComputerSystem.Model を使う。同じ値）。</summary>
    string GetModelName();

    /// <summary>
    /// <paramref name="destDir"/> を作り直して dism /online /export-driver で採取する（UAC 昇格）。
    /// fabriq の driver_export_config.ps1 と同じ手順。
    /// </summary>
    Task<DriverCaptureResult> ExportAsync(string destDir, CancellationToken ct = default);
}
