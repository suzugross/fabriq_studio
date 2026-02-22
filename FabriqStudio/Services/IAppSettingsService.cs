namespace FabriqStudio.Services;

public interface IAppSettingsService
{
    /// <summary>fabriq のルートディレクトリの絶対パス</summary>
    string FabriqRootPath { get; }
}
