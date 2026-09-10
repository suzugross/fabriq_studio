namespace FabriqStudio.Services.Collect;

/// <summary>PowerShell / 外部コマンドの実行結果。</summary>
public sealed record PowerShellResult(int ExitCode, string StdOut, string StdErr, bool Cancelled)
{
    public bool Succeeded => !Cancelled && ExitCode == 0;
}

/// <summary>winget search の 1 件。</summary>
public sealed record WingetPackage(string Name, string Id, string Version);

/// <summary>この PC にインストールされているストアアプリ（削除できるもの）の 1 件。</summary>
public sealed record StoreApp(string Name, string Publisher, string Version);

/// <summary>ドライバ採取（dism /export-driver）の結果。</summary>
public sealed record DriverCaptureResult(bool Succeeded, bool Cancelled, int Count, string Message);

/// <summary>設定 CSV への行追加の結果。</summary>
public sealed record CsvAppendResult(int Added, int Skipped, bool Materialized);
