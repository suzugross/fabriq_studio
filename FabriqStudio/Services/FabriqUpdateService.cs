using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// <see cref="IFabriqUpdateService"/> の実装。詳細は interface コメント参照。
/// <para>
/// ロジックのトップレベル構造:
/// <list type="number">
///   <item>LoadRulesAsync: JSON 読み込み + schemaVersion 検証</item>
///   <item>ComputePlanAsync: kernel + 全モジュールを走査し BundleUpdateItem を生成</item>
///   <item>RunPreflight: Process / resume_state / 書込権限 / REQUIRES_KERNEL</item>
///   <item>ApplyAsync: backup zip → 各 bundle を overlay → schema 警告抽出 → ログ書き出し</item>
/// </list>
/// </para>
/// </summary>
public class FabriqUpdateService : IFabriqUpdateService
{
    private const int   SupportedSchemaVersion = 1;
    private const string RulesRelativePath     = "dev/framework_overlay_rules.json";

    // ===================================================================
    // ルール読み込み
    // ===================================================================

    public async Task<OverlayRules> LoadRulesAsync(string templateRoot)
    {
        var rulesPath = Path.Combine(templateRoot, "dev", "framework_overlay_rules.json");
        if (!File.Exists(rulesPath))
            throw new FileNotFoundException(
                "template に dev/framework_overlay_rules.json が見つかりません。" +
                "このバージョンの fabriq template は本機能に対応していません（kernel 2.2.0 以上が必要）。",
                rulesPath);

        var json = await File.ReadAllTextAsync(rulesPath);
        var rules = JsonSerializer.Deserialize<OverlayRules>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("framework_overlay_rules.json のパースに失敗しました。");

        if (rules.SchemaVersion != SupportedSchemaVersion)
            throw new NotSupportedException(
                $"Unsupported framework_overlay_rules.json schemaVersion: {rules.SchemaVersion}. " +
                $"This version of fabriq_studio supports schemaVersion {SupportedSchemaVersion} only. " +
                $"Upgrade fabriq_studio to a newer version that understands this schema.");

        return rules;
    }

    // ===================================================================
    // Plan 計算
    // ===================================================================

    public async Task<FabriqUpdatePlan> ComputePlanAsync(string templateRoot, string targetRoot)
    {
        // target 側に Fabriq.exe が無ければ不完全な fabriq とみなしエラー
        if (!File.Exists(Path.Combine(targetRoot, "Fabriq.exe")))
            throw new InvalidOperationException(
                $"target に Fabriq.exe が見つかりません: {targetRoot}\n" +
                "正しい fabriq ルートフォルダを指定してください。");

        var rules = await LoadRulesAsync(templateRoot);

        var targetKernel   = SemVer.TryParseFile(Path.Combine(targetRoot,   rules.Bundles.Kernel.VersionFile));
        var templateKernel = SemVer.TryParseFile(Path.Combine(templateRoot, rules.Bundles.Kernel.VersionFile));

        var bundles = new List<BundleUpdateItem>();

        // ── kernel bundle ──
        var kernelAction = ClassifyAction(templateKernel, targetKernel);
        bundles.Add(new BundleUpdateItem
        {
            BundleKey       = "kernel",
            DisplayName     = "kernel",
            GroupName       = "kernel",
            TargetVersion   = targetKernel,
            TemplateVersion = templateKernel,
            Action          = kernelAction,
            IsSelected      = IsUpdatingAction(kernelAction),
        });

        // ── module bundles ──
        var effectiveKernelAfterUpdate = IsUpdatingAction(kernelAction) ? templateKernel : targetKernel;

        var moduleNames = EnumerateAllModuleNames(templateRoot, targetRoot, rules);
        foreach (var (type, name) in moduleNames.OrderBy(t => t.type).ThenBy(t => t.name))
        {
            var relModuleDir       = $"modules/{type}/{name}";
            var relVersionFile     = $"{relModuleDir}/VERSION";
            var relRequiresFile    = $"{relModuleDir}/REQUIRES_KERNEL";

            var templateExists = Directory.Exists(Path.Combine(templateRoot, "modules", type, name));
            var targetExists   = Directory.Exists(Path.Combine(targetRoot,   "modules", type, name));

            var templateVer = templateExists ? SemVer.TryParseFile(Path.Combine(templateRoot, relVersionFile)) : null;
            var targetVer   = targetExists   ? SemVer.TryParseFile(Path.Combine(targetRoot,   relVersionFile)) : null;
            var requiresKer = templateExists ? SemVer.TryParseFile(Path.Combine(templateRoot, relRequiresFile)) : null;

            UpdateAction act;
            if (!templateExists)
                act = UpdateAction.Preserve;                 // site-custom module
            else if (!targetExists)
                act = UpdateAction.New;                      // lazy seed
            else
                act = ClassifyAction(templateVer, targetVer);

            // REQUIRES_KERNEL 事前チェック（§ 9.5）
            string? warning = null;
            var blocked = false;
            if (IsUpdatingAction(act) && requiresKer.HasValue && effectiveKernelAfterUpdate.HasValue
                && requiresKer.Value > effectiveKernelAfterUpdate.Value)
            {
                blocked = true;
                warning = $"requires kernel {requiresKer}+, effective kernel would be {effectiveKernelAfterUpdate}. "
                        + "check the kernel bundle first.";
            }
            else if (act == UpdateAction.SkipTargetNewer)
            {
                warning = $"target newer ({targetVer} > {templateVer})";
            }

            bundles.Add(new BundleUpdateItem
            {
                BundleKey       = $"modules/{type}/{name}",
                DisplayName     = $"modules/{type}/{name}",
                GroupName       = $"modules/{type}",
                TargetVersion   = targetVer,
                TemplateVersion = templateVer,
                RequiresKernel  = requiresKer,
                Action          = act,
                WarningMessage  = warning,
                IsSelected      = IsUpdatingAction(act) && !blocked,
                IsBlocked       = blocked,
            });
        }

        return new FabriqUpdatePlan(templateRoot, targetRoot, rules, targetKernel, templateKernel, bundles);
    }

    /// <summary>template/target のモジュール名 union を列挙する。</summary>
    private static IEnumerable<(string type, string name)> EnumerateAllModuleNames(
        string templateRoot, string targetRoot, OverlayRules rules)
    {
        var set = new HashSet<(string, string)>();
        foreach (var type in rules.Bundles.Module.TypeValues)
        {
            foreach (var root in new[] { templateRoot, targetRoot })
            {
                var dir = Path.Combine(root, "modules", type);
                if (!Directory.Exists(dir)) continue;
                foreach (var sub in Directory.GetDirectories(dir))
                    set.Add((type, Path.GetFileName(sub)));
            }
        }
        return set;
    }

    /// <summary>§ 9.4 のマトリクスに従ってアクション判定。Preserve / New は呼び出し側で補う。</summary>
    private static UpdateAction ClassifyAction(SemVer? template, SemVer? target)
    {
        if (!template.HasValue && !target.HasValue) return UpdateAction.SkipSame;
        if (!template.HasValue)                     return UpdateAction.SkipNoTemplate;
        if (!target.HasValue)                       return UpdateAction.Update;   // lazy seed 内側用
        if (template.Value >  target.Value)         return UpdateAction.Update;
        if (template.Value == target.Value)         return UpdateAction.SkipSame;
        return UpdateAction.SkipTargetNewer;
    }

    private static bool IsUpdatingAction(UpdateAction a)
        => a is UpdateAction.Update or UpdateAction.New;

    // ===================================================================
    // Preflight
    // ===================================================================

    public PreflightResult RunPreflight(FabriqUpdatePlan plan, IReadOnlyList<BundleUpdateItem> selected)
    {
        var errors = new List<string>();
        var kernelBlocks = new List<string>();

        // 1. Fabriq.exe プロセス実行チェック
        try
        {
            var running = Process.GetProcessesByName("Fabriq");
            if (running.Length > 0)
                errors.Add($"Fabriq.exe が実行中です（{running.Length} プロセス）。終了してから再実行してください。");
        }
        catch { /* プロセス列挙失敗は無視 */ }

        // 2. resume_state.json 存在チェック
        var resumePath = Path.Combine(plan.TargetRoot, "kernel", "json", "resume_state.json");
        if (File.Exists(resumePath))
            errors.Add($"キッティング中断状態 (resume_state.json) が検出されました: {resumePath}");

        // 3. target フォルダ書込権限テスト
        try
        {
            var testPath = Path.Combine(plan.TargetRoot, ".fabriq_studio_write_test");
            File.WriteAllBytes(testPath, Array.Empty<byte>());
            File.Delete(testPath);
        }
        catch (Exception ex)
        {
            errors.Add($"target フォルダに書き込み権限がありません: {ex.Message}");
        }

        // 4. REQUIRES_KERNEL ブロックチェック（Plan 計算時に既に IsBlocked 付き）
        foreach (var b in selected)
        {
            if (b.IsBlocked)
                kernelBlocks.Add($"{b.DisplayName}: {b.WarningMessage}");
        }

        return new PreflightResult(errors, kernelBlocks);
    }

    // ===================================================================
    // Apply
    // ===================================================================

    public async Task<FabriqUpdateResult> ApplyAsync(FabriqUpdateRequest request, IProgress<string>? progress = null)
    {
        return await Task.Run(() => ApplyCore(request, progress));
    }

    private FabriqUpdateResult ApplyCore(FabriqUpdateRequest request, IProgress<string>? progress)
    {
        var logLines = new List<string>();

        // summary 用: UI 進捗にも通知
        void Log(string msg)
        {
            logLines.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            progress?.Report(msg);
        }

        // per-file 用: ログファイルにのみ記録（UI 通知しない）。
        // 数千ファイルの通知で UI スレッド上の文字列連結が O(n²) になるのを回避する。
        void FileLog(string msg) => logLines.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");

        var plan  = request.Plan;
        var rules = plan.Rules;

        Log($"=== fabriq update start (dryRun={request.DryRun}) ===");
        Log($"template : {plan.TemplateRoot}");
        Log($"target   : {plan.TargetRoot}");
        Log($"bundles  : {request.SelectedBundles.Count} selected");

        // ── バックアップ（dry-run ではスキップ: 目的は「書き込まずに計画を確認」なため） ──
        if (!request.DryRun)
        {
            try
            {
                Log($"Creating backup zip -> {request.BackupZipPath}");
                var parent = Path.GetDirectoryName(request.BackupZipPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                if (File.Exists(request.BackupZipPath)) File.Delete(request.BackupZipPath);
                ZipFile.CreateFromDirectory(plan.TargetRoot, request.BackupZipPath,
                    CompressionLevel.Optimal, includeBaseDirectory: false);
                Log($"Backup OK ({new FileInfo(request.BackupZipPath).Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Log($"Backup FAILED: {ex.Message}");
                WriteLog(request.LogFilePath, logLines);
                throw;
            }
        }
        else
        {
            Log("Dry-run: skipping backup zip creation");
        }

        // ── 各 bundle を overlay ──
        var bundleResults = new List<BundleUpdateResult>();
        foreach (var bundle in request.SelectedBundles)
        {
            if (bundle.IsBlocked)
            {
                Log($"SKIP (blocked): {bundle.DisplayName} — {bundle.WarningMessage}");
                bundleResults.Add(new BundleUpdateResult(
                    bundle.BundleKey, false, 0, 0,
                    new[] { bundle.WarningMessage ?? "blocked by REQUIRES_KERNEL" },
                    bundle.TargetVersion, bundle.TemplateVersion, bundle.Action));
                continue;
            }
            if (!IsUpdatingAction(bundle.Action))
            {
                Log($"SKIP: {bundle.DisplayName} (action={bundle.ActionText})");
                continue;
            }

            Log($"Updating {bundle.DisplayName}: {bundle.TargetVersionText} -> {bundle.TemplateVersionText}");
            try
            {
                BundleUpdateResult r = bundle.BundleKey == "kernel"
                    ? OverlayKernel(plan, rules, bundle, request.DryRun, FileLog)
                    : OverlayModule(plan, rules, bundle, request.DryRun, FileLog);
                Log($"  → {r.TouchedFileCount} touched, {r.SkippedCount} skipped, {r.Errors.Count} errors");
                bundleResults.Add(r);
            }
            catch (Exception ex)
            {
                Log($"FAILED: {bundle.DisplayName} — {ex.Message}");
                bundleResults.Add(new BundleUpdateResult(
                    bundle.BundleKey, false, 0, 0, new[] { ex.Message },
                    bundle.TargetVersion, bundle.TemplateVersion, bundle.Action));
            }
        }

        // ── CHANGELOG から schema 関連警告を抽出 ──
        var schemaWarnings = ExtractSchemaWarnings(plan.TemplateRoot);
        foreach (var w in schemaWarnings) Log($"SCHEMA WARNING: {w}");

        Log($"=== fabriq update complete (success={bundleResults.Count(r => r.Success)}, " +
            $"failed={bundleResults.Count(r => !r.Success)}) ===");
        WriteLog(request.LogFilePath, logLines);

        return new FabriqUpdateResult(bundleResults, request.BackupZipPath, request.LogFilePath,
            request.DryRun, schemaWarnings);
    }

    // ───────────────────────────────────────────────────────────
    // Overlay: kernel bundle
    // ───────────────────────────────────────────────────────────

    private BundleUpdateResult OverlayKernel(
        FabriqUpdatePlan plan, OverlayRules rules, BundleUpdateItem bundle, bool dryRun, Action<string> log)
    {
        var errors  = new List<string>();
        int touched = 0, skipped = 0;

        var excludeDirsTop   = new HashSet<string>(rules.ExcludeDirsTopLevel,   StringComparer.OrdinalIgnoreCase);
        var excludeDirsRec   = new HashSet<string>(rules.ExcludeDirsRecursive,  StringComparer.OrdinalIgnoreCase);
        var excludeFilesKern = new HashSet<string>(
            rules.ExcludeFilesKernelLevel.Select(p => p.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var incPath in rules.Bundles.Kernel.IncludePaths)
        {
            var normalized = incPath.TrimEnd('/', '\\').Replace('\\', '/');
            var srcPath    = Path.Combine(plan.TemplateRoot, normalized);

            if (File.Exists(srcPath))
            {
                // 単一ファイル include（Fabriq.exe / Deploy.bat / README.md 等）
                var rel = normalized;
                if (IsExcludedKernelFile(rel, excludeFilesKern))
                {
                    skipped++;
                    continue;
                }
                if (TryCopy(srcPath, Path.Combine(plan.TargetRoot, rel), dryRun, errors, log))
                    touched++;
            }
            else if (Directory.Exists(srcPath))
            {
                // ディレクトリ include
                CopyKernelTree(srcPath, plan.TargetRoot, normalized,
                    excludeDirsTop, excludeDirsRec, excludeFilesKern,
                    dryRun, log, ref touched, ref skipped, errors);
            }
            else
            {
                log($"  (skip: include path not found in template: {incPath})");
            }
        }

        return new BundleUpdateResult(
            bundle.BundleKey, errors.Count == 0, touched, skipped, errors,
            bundle.TargetVersion, bundle.TemplateVersion, bundle.Action);
    }

    private void CopyKernelTree(
        string srcDir, string targetRoot, string relativeBase,
        HashSet<string> excludeDirsTop, HashSet<string> excludeDirsRec, HashSet<string> excludeFilesKern,
        bool dryRun, Action<string> log, ref int touched, ref int skipped, List<string> errors)
    {
        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Path.Combine(srcDir, ".."), file)
                .Replace('\\', '/');
            // rel は "kernel/csv/hostlist.csv" 相当

            // excludeDirsTopLevel
            var topSeg = rel.Split('/')[0];
            if (excludeDirsTop.Contains(topSeg)) { skipped++; continue; }

            // excludeDirsRecursive（`profiles` など）
            if (rel.Split('/').Any(seg => excludeDirsRec.Contains(seg))) { skipped++; continue; }

            // excludeFilesKernelLevel（`kernel/csv/hostlist.csv` 等）
            if (IsExcludedKernelFile(rel, excludeFilesKern)) { skipped++; continue; }

            var dst = Path.Combine(targetRoot, rel);
            if (TryCopy(file, dst, dryRun, errors, log))
                touched++;
        }
    }

    private static bool IsExcludedKernelFile(string relPath, HashSet<string> excludeSet)
    {
        var normalized = relPath.Replace('\\', '/');
        return excludeSet.Contains(normalized);
    }

    // ───────────────────────────────────────────────────────────
    // Overlay: module bundle
    // ───────────────────────────────────────────────────────────

    private BundleUpdateResult OverlayModule(
        FabriqUpdatePlan plan, OverlayRules rules, BundleUpdateItem bundle, bool dryRun, Action<string> log)
    {
        var errors  = new List<string>();
        int touched = 0, skipped = 0;

        // bundle.BundleKey = "modules/standard/<name>" 形式
        var rel     = bundle.BundleKey.Replace('\\', '/');
        var srcDir  = Path.Combine(plan.TemplateRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        var dstDir  = Path.Combine(plan.TargetRoot,   rel.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(srcDir))
        {
            errors.Add($"template にモジュールフォルダが見つかりません: {srcDir}");
            return new BundleUpdateResult(bundle.BundleKey, false, 0, 0, errors,
                bundle.TargetVersion, bundle.TemplateVersion, bundle.Action);
        }

        var csvWhitelist = new HashSet<string>(rules.ModuleCsvWhitelist, StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var ext  = Path.GetExtension(file);

            // *.csv のうちホワイトリスト外は site-specific として保持
            if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                && !csvWhitelist.Contains(name))
            {
                skipped++;
                continue;
            }

            var relFromModule = Path.GetRelativePath(srcDir, file);
            var dst           = Path.Combine(dstDir, relFromModule);
            if (TryCopy(file, dst, dryRun, errors, log))
                touched++;
        }

        return new BundleUpdateResult(
            bundle.BundleKey, errors.Count == 0, touched, skipped, errors,
            bundle.TargetVersion, bundle.TemplateVersion, bundle.Action);
    }

    // ───────────────────────────────────────────────────────────
    // 共通ヘルパー
    // ───────────────────────────────────────────────────────────

    private static bool TryCopy(string src, string dst, bool dryRun, List<string> errors, Action<string> log)
    {
        try
        {
            var parent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(parent) && !dryRun) Directory.CreateDirectory(parent);

            if (dryRun)
            {
                log($"  [dry-run] would copy: {dst}");
                return true;
            }

            File.Copy(src, dst, overwrite: true);
            log($"  copied: {Path.GetRelativePath(Path.GetPathRoot(dst) ?? "", dst)}");
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"{src} -> {dst}: {ex.Message}");
            log($"  ERROR: {src}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// CHANGELOG.md の `[Unreleased]` および最新 `[X.Y.Z]` セクションから
    /// "schema" を含む行を抽出して警告リストに変換する。
    /// </summary>
    private static List<string> ExtractSchemaWarnings(string templateRoot)
    {
        var warnings = new List<string>();
        var changelog = Path.Combine(templateRoot, "CHANGELOG.md");
        if (!File.Exists(changelog)) return warnings;

        try
        {
            var lines = File.ReadAllLines(changelog);
            bool inSection = false;
            int sectionsRead = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (sectionsRead >= 2) break;
                    inSection = true;
                    sectionsRead++;
                    continue;
                }
                if (!inSection) continue;
                if (line.Contains("schema", StringComparison.OrdinalIgnoreCase))
                    warnings.Add(line.TrimStart('-', ' ', '*').Trim());
            }
        }
        catch { /* 読み取り失敗は警告抽出のみスキップ */ }

        return warnings;
    }

    private static void WriteLog(string logPath, IEnumerable<string> lines)
    {
        try
        {
            var parent = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(logPath, string.Join(Environment.NewLine, lines),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch { /* ログ書き込み失敗は致命的でないので無視 */ }
    }
}
