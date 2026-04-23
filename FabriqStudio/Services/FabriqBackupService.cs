using System.IO;
using System.Text;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// fabriq バックアップ実装。
/// <para>
/// ルール:
/// <list type="bullet">
///   <item>トップレベル除外ディレクトリ（.git, .claude, dev, logs, apps, commands, evidence, kernel/ps1）は丸ごとスキップ</item>
///   <item>グローバル拡張子除外（.ps1, .bat, .bak, .tmp, .log）</item>
///   <item>グローバルファイル名除外（VERSION, Guide.txt, preset.csv, CLAUDE.md 等）</item>
///   <item>相対パス固有除外（ランタイム状態ファイル）</item>
///   <item><b>素材フォルダ例外</b>: modules/&lt;kind&gt;/&lt;module&gt;/&lt;subdir&gt;/ 以下は
///         全ての除外ルールを無視して丸ごとコピーする</item>
/// </list>
/// </para>
/// </summary>
public class FabriqBackupService : IFabriqBackupService
{
    // ─── 除外ポリシー（将来的に設定ファイルへ外出ししたい場合はここを起点に） ───

    /// <summary>fabriq ルート直下で丸ごとスキップするディレクトリ（相対パス、'/' 区切り）。</summary>
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".claude", "dev", "logs", "apps", "commands", "evidence",
        "kernel/ps1",
    };

    /// <summary>どこに配置されていても常に除外する拡張子。</summary>
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".bat", ".bak", ".tmp", ".log",
    };

    /// <summary>どこに配置されていても常に除外するファイル名。</summary>
    private static readonly HashSet<string> ExcludedFilenames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gitignore", ".gitkeep",
        "CLAUDE.md", "CHANGELOG.md",
        "KERNEL_VERSION", "KERNEL_API.md",
        "VERSION", "REQUIRES_KERNEL",
        "Guide.txt", "preset.csv",
        "Fabriq.exe",
    };

    /// <summary>特定相対パスのみ除外（主にランタイム状態ファイル）。</summary>
    private static readonly HashSet<string> ExcludedRelativePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel/json/skip_request.flag",
        "kernel/json/art_pulse.txt",
        "kernel/json/resume_state.json",
        "kernel/json/status.json",
        "kernel/json/session.json",
        "kernel/txt/art_sentences.txt",
        "kernel/txt/silence.flag",
    };

    public Task<FabriqBackupResult> BackupAsync(FabriqBackupRequest request)
        => Task.Run(() => BackupSync(request));

    private FabriqBackupResult BackupSync(FabriqBackupRequest request)
    {
        if (!Directory.Exists(request.SourceRoot))
            throw new DirectoryNotFoundException($"コピー元が存在しません: {request.SourceRoot}");

        var now          = DateTime.Now;
        var folderName   = $"fabriq_backup_{now:yyyyMMdd_HHmmss}";
        var backupFolder = Path.Combine(request.ParentFolder, folderName);
        Directory.CreateDirectory(backupFolder);

        var state = new CopyState();
        CopyDirectoryFiltered(request.SourceRoot, backupFolder, currentRelative: "", state);

        // USER_MEMO.txt と BACKUP_INFO.txt はミラー作成後に配置（除外ルールは適用しない）
        WriteUserMemo(backupFolder, request.Memo);
        WriteBackupInfo(backupFolder, now, state, request);

        return new FabriqBackupResult(
            backupFolder, state.CopiedFiles, state.ExcludedFiles, state.TotalBytes, state.Errors);
    }

    /// <summary>モジュール配下のサブディレクトリ（=素材フォルダ）か判定。</summary>
    private static bool IsModuleAssetFolder(string relativeDir)
    {
        // 形式: modules/<kind>/<module>/<subdir> … 以下（セグメント数 4 以上 = 素材フォルダ内）
        var segs = relativeDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return segs.Length >= 4
            && segs[0].Equals("modules", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>除外ルールを適用しながら 1 ディレクトリをコピーする。</summary>
    private void CopyDirectoryFiltered(
        string srcRoot, string dstRoot, string currentRelative, CopyState state)
    {
        var srcDir = string.IsNullOrEmpty(currentRelative)
            ? srcRoot
            : Path.Combine(srcRoot, currentRelative);

        Directory.CreateDirectory(Path.Combine(dstRoot, currentRelative));

        // ── ファイル ──
        foreach (var srcFile in Directory.GetFiles(srcDir))
        {
            var name    = Path.GetFileName(srcFile);
            var relFile = CombineRelative(currentRelative, name);

            if (IsFileExcluded(relFile, name))
            {
                state.ExcludedFiles++;
                continue;
            }

            TryCopyFile(srcFile, Path.Combine(dstRoot, relFile), state);
        }

        // ── サブディレクトリ ──
        foreach (var srcSub in Directory.GetDirectories(srcDir))
        {
            var name   = Path.GetFileName(srcSub);
            var relSub = CombineRelative(currentRelative, name);

            if (IsDirExcluded(relSub))
                continue;

            if (IsModuleAssetFolder(relSub))
            {
                // 素材フォルダ: 除外ルールを外し、丸ごと再帰コピー
                CopyDirectoryRaw(srcSub, Path.Combine(dstRoot, relSub), state);
            }
            else
            {
                CopyDirectoryFiltered(srcRoot, dstRoot, relSub, state);
            }
        }
    }

    /// <summary>除外ルールを適用せず、ディレクトリを丸ごとコピーする（素材フォルダ用）。</summary>
    private static void CopyDirectoryRaw(string srcDir, string dstDir, CopyState state)
    {
        Directory.CreateDirectory(dstDir);

        foreach (var srcFile in Directory.GetFiles(srcDir))
        {
            var dstFile = Path.Combine(dstDir, Path.GetFileName(srcFile));
            TryCopyFile(srcFile, dstFile, state);
        }

        foreach (var srcSub in Directory.GetDirectories(srcDir))
        {
            var dstSub = Path.Combine(dstDir, Path.GetFileName(srcSub));
            CopyDirectoryRaw(srcSub, dstSub, state);
        }
    }

    private static void TryCopyFile(string src, string dst, CopyState state)
    {
        try
        {
            File.Copy(src, dst, overwrite: false);
            var info = new FileInfo(dst);
            state.CopiedFiles++;
            state.TotalBytes += info.Length;
        }
        catch (Exception ex)
        {
            state.Errors.Add($"{src}: {ex.Message}");
        }
    }

    private static bool IsDirExcluded(string relativeDir)
    {
        var normalized = relativeDir.Replace('\\', '/');
        return ExcludedDirs.Contains(normalized);
    }

    private static bool IsFileExcluded(string relativeFile, string fileName)
    {
        var normalized = relativeFile.Replace('\\', '/');
        if (ExcludedRelativePaths.Contains(normalized)) return true;
        if (ExcludedFilenames.Contains(fileName))       return true;
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && ExcludedExtensions.Contains(ext)) return true;
        return false;
    }

    private static string CombineRelative(string a, string b)
        => string.IsNullOrEmpty(a) ? b : Path.Combine(a, b);

    // ─── メタ情報書き出し ────────────────────────────────────────

    private static void WriteUserMemo(string backupFolder, string memo)
    {
        var path = Path.Combine(backupFolder, "USER_MEMO.txt");
        var body = string.IsNullOrWhiteSpace(memo) ? "(no memo)" : memo.TrimEnd() + Environment.NewLine;
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteBackupInfo(
        string backupFolder, DateTime now, CopyState state, FabriqBackupRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("fabriq studio — fabriq backup");
        sb.AppendLine("================================");
        sb.AppendLine($"Created at         : {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Source root        : {request.SourceRoot}");
        sb.AppendLine($"Copied files       : {state.CopiedFiles}");
        sb.AppendLine($"Excluded files     : {state.ExcludedFiles}");
        sb.AppendLine($"Total bytes        : {state.TotalBytes:N0}");
        if (state.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Errors ({state.Errors.Count})");
            sb.AppendLine("----------------");
            foreach (var e in state.Errors.Take(50)) sb.AppendLine(e);
        }
        sb.AppendLine();
        sb.AppendLine("Excluded top-level dirs: " + string.Join(", ", ExcludedDirs));
        sb.AppendLine("Excluded extensions    : " + string.Join(", ", ExcludedExtensions));
        sb.AppendLine("Excluded filenames     : " + string.Join(", ", ExcludedFilenames));
        sb.AppendLine("Asset folder rule      : modules/<kind>/<name>/<subdir>/ は除外を無視して丸ごと保存");

        File.WriteAllText(Path.Combine(backupFolder, "BACKUP_INFO.txt"),
            sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed class CopyState
    {
        public int           CopiedFiles   { get; set; }
        public int           ExcludedFiles { get; set; }
        public long          TotalBytes    { get; set; }
        public List<string>  Errors        { get; } = new();
    }
}
