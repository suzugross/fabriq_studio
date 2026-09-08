using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using FabriqStudio.Models;
using FabriqStudio.Models.Master;
using FabriqStudio.Services.Gpo;
using FabriqStudio.Services.Master.Emitters;

namespace FabriqStudio.Services.Master;

public sealed class MasterProfileGeneratorService : IMasterProfileGeneratorService
{
    private static readonly Regex TagRegex = new(@"\[master:([A-Za-z0-9_-]+)\]", RegexOptions.Compiled);

    private static readonly string[] DefaultRegistryHeaders =
        ["Enabled", "AdminID", "SettingTitle", "KeyPath", "KeyName", "Type", "Value", "Segment"];

    private readonly IWorkspaceService          _workspace;
    private readonly IModuleService             _moduleService;
    private readonly IFileService               _fileService;
    private readonly ICsvService                _csvService;
    private readonly IRegistryCollectionService _registry;
    private readonly ICryptoService             _crypto;
    private readonly IMasterTargetResolver      _resolver;

    /// <summary>実行順は固定（レジストリ辞書 → GPO → アカウント → 基盤 → システム → デスクトップ → アプリ → 仕上げ → 配備 → 手動）。</summary>
    private readonly IMasterEmitter[] _emitters;

    public MasterProfileGeneratorService(
        IWorkspaceService          workspace,
        IModuleService             moduleService,
        IFileService               fileService,
        ICsvService                csvService,
        IRegistryCollectionService registry,
        ICryptoService             crypto,
        IMasterTargetResolver      resolver,
        IGpoCatalogService         gpoCatalog)
    {
        _workspace     = workspace;
        _moduleService = moduleService;
        _fileService   = fileService;
        _csvService    = csvService;
        _registry      = registry;
        _crypto        = crypto;
        _resolver      = resolver;
        _emitters =
        [
            new RegistryTemplateEmitter(),
            new RegistryAdditionEmitter(),
            new GpoEmitter(gpoCatalog),
            new AccountEmitter(),
            new BaseSettingsEmitter(),
            new SystemEmitter(),
            new DesktopEmitter(),
            new PrinterEmitter(),
            new AppsEmitter(),
            new FinalizeEmitter(),
            new SysprepEmitter(),
            new ManualEmitter(),
        ];
    }

    // ═══════════════════════════════════════════════════════════════
    //  Snapshot
    // ═══════════════════════════════════════════════════════════════

    public async Task<MasterWorkspaceSnapshot> LoadSnapshotAsync()
    {
        var root     = _resolver.RootPath;
        var snapshot = new MasterWorkspaceSnapshot { RootPath = root };

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "module.csv", ModulePresetService.PresetFileName,
        };

        // module.csv のメタ（Script → MenuName）
        var menuNames = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in await _moduleService.GetAllModulesAsync())
            {
                if (!menuNames.TryGetValue(entry.ModuleDir, out var map))
                    menuNames[entry.ModuleDir] = map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                map[entry.Script] = entry.MenuName;
            }
        }
        catch
        {
            // メニュー名は Description の初期値に使うだけなので、読めなくても生成は続けられる
        }

        foreach (var tier in new[] { "standard", "extended" })
        {
            var tierDir = Path.Combine(root, "modules", tier);
            if (!Directory.Exists(tierDir)) continue;

            foreach (var moduleDir in Directory.GetDirectories(tierDir))
            {
                var name = Path.GetFileName(moduleDir);
                if (snapshot.Modules.ContainsKey(name)) continue;   // standard 優先

                var info = new MasterModuleInfo { Dir = name, Kind = tier, AbsPath = moduleDir };
                if (menuNames.TryGetValue(name, out var map))
                    foreach (var (k, v) in map) info.ScriptMenuNames[k] = v;

                foreach (var csvPath in Directory.GetFiles(moduleDir, "*.csv", SearchOption.TopDirectoryOnly))
                {
                    var csvName = Path.GetFileName(csvPath);
                    if (excluded.Contains(csvName)) continue;
                    info.Csvs[csvName] = await ReadCsvInfoAsync(csvPath);
                }

                foreach (var sub in Directory.GetDirectories(moduleDir))
                {
                    var subName = Path.GetFileName(sub);
                    info.SubDirs.Add(subName);
                    var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (var e in Directory.EnumerateFileSystemEntries(sub))
                            entries.Add(Path.GetFileName(e));
                    }
                    catch { /* アクセス不可のフォルダは空扱い */ }
                    info.SubDirFiles[subName] = entries;

                    // 2 階層目（例: assets\M365 の中に Office\ があるか）も記録する
                    try
                    {
                        foreach (var child in Directory.GetDirectories(sub))
                        {
                            var childEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var e in Directory.EnumerateFileSystemEntries(child))
                                childEntries.Add(Path.GetFileName(e));
                            info.SubDirFiles[$"{subName}\\{Path.GetFileName(child)}"] = childEntries;
                        }
                    }
                    catch { /* アクセス不可は無視 */ }
                }

                snapshot.Modules[name] = info;
            }
        }

        var profilesDir = _resolver.ProfilesDir;
        if (Directory.Exists(profilesDir))
            foreach (var f in Directory.GetFiles(profilesDir, "*.csv", SearchOption.TopDirectoryOnly))
                snapshot.ProfileNames.Add(Path.GetFileNameWithoutExtension(f));

        var hostlistPath = Path.Combine(root, "kernel", "csv", "hostlist.csv");
        if (File.Exists(hostlistPath))
            snapshot.Hostlist = await ReadCsvInfoAsync(hostlistPath);

        return snapshot;
    }

    private async Task<MasterCsvInfo> ReadCsvInfoAsync(string csvPath)
    {
        var info = new MasterCsvInfo { Name = Path.GetFileName(csvPath), AbsPath = csvPath };
        try
        {
            var table = await _fileService.ReadCsvAsDataTableAsync(csvPath);
            foreach (DataColumn c in table.Columns) info.Headers.Add(c.ColumnName);
            info.RowCount = table.Rows.Count;

            var hasSeg  = table.Columns.Contains("Segment");
            var hasDesc = table.Columns.Contains("Description");
            var hasId   = table.Columns.Contains("AdminID");
            foreach (DataRow row in table.Rows)
            {
                if (hasSeg)
                {
                    var seg = row["Segment"]?.ToString()?.Trim() ?? "";
                    if (seg.Length > 0)
                        info.SegmentCounts[seg] = info.SegmentCounts.GetValueOrDefault(seg) + 1;
                }
                if (hasDesc)
                {
                    var desc = row["Description"]?.ToString() ?? "";
                    foreach (Match m in TagRegex.Matches(desc))
                        info.TagCounts[m.Value] = info.TagCounts.GetValueOrDefault(m.Value) + 1;
                }
                if (hasId)
                {
                    var id = row["AdminID"]?.ToString()?.Trim() ?? "";
                    if (id.Length > 0)
                    {
                        info.AdminIdCounts[id] = info.AdminIdCounts.GetValueOrDefault(id) + 1;
                        if (!info.RowsByAdminId.ContainsKey(id))
                        {
                            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (DataColumn c in table.Columns) cells[c.ColumnName] = row[c]?.ToString() ?? "";
                            info.RowsByAdminId[id] = cells;
                        }
                    }
                }
            }
        }
        catch
        {
            // 壊れた CSV: ヘッダー無しとして扱う（AddCsvRow 側で「列が無い」警告になる）
        }
        return info;
    }

    // ═══════════════════════════════════════════════════════════════
    //  BuildPlan
    // ═══════════════════════════════════════════════════════════════

    public MasterPlan BuildPlan(MasterTemplate template, MasterAnswers answers, MasterWorkspaceSnapshot snapshot)
    {
        Func<string, string>? encrypt = _crypto.HasPassphrase && _crypto.MasterPassphrase is { } pp
            ? v => _crypto.Encrypt(v, pp)
            : null;

        var ctx = new MasterContext(template, answers, snapshot, _registry.Entries, _resolver, encrypt);

        if (!MasterAnswers.IsValidMasterName(answers.MasterName))
        {
            ctx.Error("マスタ名を半角英数字・アンダースコア・ハイフンで入力してください。");
            return ctx.Plan;
        }

        foreach (var emitter in _emitters)
        {
            try { emitter.Emit(ctx); }
            catch (Exception ex)
            {
                ctx.Error($"{emitter.Name} の計算中にエラー: {ex.Message}");
            }
        }

        CheckGpoConflicts(ctx);
        AssembleRegistryFiles(ctx);
        AssembleProfiles(ctx);
        AddStaleRowCleanups(ctx);
        CheckFirstGenerationCollisions(ctx);
        BuildFileSummaries(ctx.Plan);

        return ctx.Plan;
    }

    /// <summary>
    /// GPO（Registry.pol）とレジストリ辞書（reg_hklm / reg_hkcu）が同じ値を書く二重管理を検出して警告する。
    /// GPO は定期的（既定 90 分）に再適用されるため、reg 側の意図と衝突して設定が行き来する。
    /// </summary>
    private static void CheckGpoConflicts(MasterContext ctx)
    {
        var gpo = ctx.Plan.CsvOps.FirstOrDefault(o =>
            o.ModuleDir.Equals(GpoEmitter.ModuleDir, StringComparison.OrdinalIgnoreCase) &&
            o.CsvName.Equals(GpoEmitter.CsvName, StringComparison.OrdinalIgnoreCase));
        if (gpo is null || ctx.RegistryRequests.Count == 0) return;

        foreach (var row in gpo.Rows)
        {
            var valueName = row.GetValueOrDefault("ValueName", "") ?? "";
            if (valueName.Length == 0) continue;
            var scope = row.GetValueOrDefault("Scope", "") ?? "";
            var hive  = scope.Equals("User", StringComparison.OrdinalIgnoreCase) ? "HKCU" : "HKLM";
            var key   = StripHive(row.GetValueOrDefault("KeyPath", "") ?? "");

            foreach (var r in ctx.RegistryRequests)
            {
                if (!r.Entry.Hive.Equals(hive, StringComparison.OrdinalIgnoreCase)) continue;
                if (!r.Entry.KeyName.Equals(valueName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!StripHive(r.Entry.KeyPath).Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                ctx.Warn($"GPO「{row.GetValueOrDefault("SettingTitle", "")}」とレジストリ設定「{r.SettingTitle}」が同じ値（{hive}\\{key}\\{valueName}）を書きます。GPO は定期的に再適用されるため二重管理になります。どちらか一方にしてください。", GpoEmitter.ItemId);
            }
        }
    }

    private static string StripHive(string keyPath)
    {
        var k = keyPath.Trim().Trim('\\');
        foreach (var prefix in new[] { "HKEY_LOCAL_MACHINE\\", "HKEY_CURRENT_USER\\", "HKLM\\", "HKCU\\" })
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return k[prefix.Length..].Trim('\\');
        return k;
    }

    /// <summary>
    /// 以前の生成でこのマスタの行（Segment=マスタ名 / [master:名] タグ）を書いた CSV のうち、
    /// 今回の計画で行を出さないものは「行 0 件の置換」として計画に載せ、旧行を取り除く。
    /// （章を無効にしたときに古い設定行が残らないようにする）
    /// </summary>
    private void AddStaleRowCleanups(MasterContext ctx)
    {
        var plan   = ctx.Plan;
        var suffix = $"_list_{ctx.MasterName}.csv";

        foreach (var module in ctx.Snapshot.Modules.Values)
        {
            foreach (var csv in module.Csvs.Values)
            {
                // 案件別レジストリファイルは RegistryOps / Deletes で扱う
                if (csv.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                var segCount = csv.HasSegment ? MasterContext.CountOwnedRows(csv, ctx.MasterName) : 0;
                var tagCount = !csv.HasSegment && csv.HasColumn("Description") ? csv.TagCounts.GetValueOrDefault(ctx.Tag) : 0;
                if (segCount == 0 && tagCount == 0) continue;

                if (plan.CsvOps.Any(o => o.AbsPath.Equals(csv.AbsPath, StringComparison.OrdinalIgnoreCase))) continue;

                plan.CsvOps.Add(new PlanCsvRows
                {
                    ModuleDir = module.Dir,
                    CsvName   = csv.Name,
                    AbsPath   = csv.AbsPath,
                    RelPath   = _resolver.ToRelative(csv.AbsPath),
                    Isolation = csv.HasSegment ? PlanIsolation.Segment : PlanIsolation.DescriptionTag,
                    Tag       = ctx.Tag,
                    ExistingIsolatedRows = csv.HasSegment ? segCount : tagCount,
                });
            }
        }

        // hostlist.csv の仮ホスト名行（管理番号 = 回答の master_admin_id。旧版は AdminID = マスタ名）も、今回出さないなら取り除く
        var host = ctx.Snapshot.Hostlist;
        if (host is not null
            && !plan.CsvOps.Any(o => o.AbsPath.Equals(host.AbsPath, StringComparison.OrdinalIgnoreCase)))
        {
            var key   = BaseSettingsEmitter.StoredAdminId(ctx);
            var owned = host.AdminIdCounts.GetValueOrDefault(ctx.MasterName);
            if (key is not null && host.RowsByAdminId.TryGetValue(key, out var keyRow) && !BaseSettingsEmitter.LooksLikeDeviceRow(keyRow))
                owned += host.AdminIdCounts.GetValueOrDefault(key);
            else
                key = null;   // 端末の行は触らない

            if (owned > 0) plan.CsvOps.Add(new PlanCsvRows
            {
                ModuleDir = "kernel/csv",
                CsvName   = host.Name,
                AbsPath   = host.AbsPath,
                RelPath   = _resolver.ToRelative(host.AbsPath),
                Isolation = PlanIsolation.AdminId,
                Tag       = ctx.Tag,
                AdminIdKey = key,
                ExistingIsolatedRows = owned,
            });
        }
    }

    private void AssembleRegistryFiles(MasterContext ctx)
    {
        var plan = ctx.Plan;
        foreach (var (hive, moduleDir, script, order) in new[]
                 {
                     ("HKLM", "reg_hklm_config", "reg_hklm_config.ps1", 10),
                     ("HKCU", "reg_hkcu_config", "reg_hkcu_config.ps1", 20),
                 })
        {
            var rows = ctx.RegistryRequests
                .Where(r => r.Entry.Hive.Equals(hive, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.SubSegment is null ? 0 : 1)   // 通常行 → 副セグメント（一時ポリシー）行
                .ToList();

            var fileName = $"reg_{hive.ToLowerInvariant()}_list_{ctx.MasterName}.csv";
            var module   = ctx.Snapshot.GetModule(moduleDir);

            if (rows.Count == 0)
            {
                // 以前の生成物が残っていれば削除対象にする
                if (module is not null && module.Csvs.TryGetValue(fileName, out var stale))
                    plan.Deletes.Add(new PlanDelete
                    {
                        AbsPath = stale.AbsPath,
                        RelPath = _resolver.ToRelative(stale.AbsPath),
                        Reason  = $"{hive} のレジストリ設定が無くなったため",
                    });
                continue;
            }

            if (module is null)
            {
                ctx.Error($"モジュール {moduleDir} がワークスペースに無いため、{hive} のレジストリ設定（{rows.Count} 件）を書けません。");
                continue;
            }

            var abs = Path.Combine(module.AbsPath, fileName);
            var op  = new PlanRegistryFile
            {
                Hive      = hive,
                ModuleDir = moduleDir,
                AbsPath   = abs,
                RelPath   = _resolver.ToRelative(abs),
                Exists    = module.Csvs.ContainsKey(fileName),
            };
            foreach (var r in rows)
                op.Rows.Add(new PlanRegistryRow
                {
                    SettingTitle = r.SettingTitle,
                    ItemId       = r.ItemId ?? "",
                    KeyPath      = r.Entry.KeyPath,
                    KeyName      = r.Entry.KeyName,
                    Type         = r.Entry.Type,
                    Value        = r.Value,
                    Segment      = ctx.SegmentFor(r.SubSegment),
                });
            plan.RegistryOps.Add(op);

            if (rows.Any(r => r.SubSegment is null))
                ctx.AddProfile(moduleDir, script, ProfileSlot.Registry, order, isolated: true);

            // 副セグメント（マスタ作成中だけの一時ポリシー）はマスタ プロファイルの先頭で設定する。
            // 解除は Sysprep プロファイル側の reg_*_delete 行（SysprepEmitter）が同じ Segment で行う。
            foreach (var sub in rows.Where(r => r.SubSegment is not null).Select(r => r.SubSegment!).Distinct())
                ctx.AddProfile(moduleDir, script, ProfileSlot.Base, 1, isolated: true,
                    subSegment: sub, description: $"{ctx.MenuName(moduleDir, script)} - 一時ポリシー");
        }
    }

    private void AssembleProfiles(MasterContext ctx)
    {
        var plan = ctx.Plan;

        // ── マスタ本体 ──────────────────────────────────────────────
        var master = ctx.ProfileRequests.Where(p => p.Kind == ProfileKind.Master)
            .OrderBy(p => p.Slot).ThenBy(p => p.Order).ThenBy(p => p.Sequence).ToList();

        var masterProfile = NewProfile(ctx, ctx.MasterName, ProfileKind.Master);
        var rows = masterProfile.Rows;

        var wait = ctx.GetInt("autopilot_wait") ?? 3;
        rows.Add(Marker("__AUTOPILOT__", $"WaitSec={Math.Max(0, wait)}"));

        // 一時ポリシー（副セグメント temp）だけでは再起動しない
        var hasEarlyRows   = master.Any(p => p.Slot <= ProfileSlot.Account && p.SubSegment != SysprepEmitter.TempSubSegment);
        var hkcuRows       = ctx.RegistryRequests.Any(r => r.Entry.Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase));
        var restartDone    = false;
        var gateDone       = false;

        foreach (var group in master.GroupBy(p => p.Slot).OrderBy(g => g.Key))
        {
            // Base / Account を抜けるところで再起動（ホスト側の状態を確定させてから続きを無人実行）
            if (!restartDone && hasEarlyRows && group.Key > ProfileSlot.Account)
            {
                rows.Add(Marker("__RESTART__", "Restart"));
                restartDone = true;
            }

            // 仕上げ（Sysprep 等）の前に前進バリア: Error / 検証失敗が残っていれば先へ進ませない
            if (!gateDone && group.Key >= ProfileSlot.Finalize)
            {
                rows.Add(Marker("__GATE__", "Forward barrier (since kernel 3.6.0)"));
                gateDone = true;
            }

            foreach (var req in group.OrderBy(p => p.Order).ThenBy(p => p.Sequence))
                rows.Add(ToEntry(ctx, req));

            // Registry スロットの後で Explorer 再起動（HKCU の即時反映）
            if (group.Key == ProfileSlot.Registry && hkcuRows)
                rows.Add(Marker("__REEXPLORER__", "Restart Explorer"));
        }

        // Base / Account しか無い場合は末尾で再起動
        if (!restartDone && hasEarlyRows)
        {
            rows.Add(Marker("__RESTART__", "Restart"));
            restartDone = true;
        }

        if (restartDone)
            ctx.Info("__RESTART__ の後は組み込み Administrator でサインインし直すと、Fabriq が RunOnce から自動で再開します。");

        Renumber(rows);
        plan.Profiles.Add(masterProfile);

        if (master.Count == 0)
            ctx.Warn("生成されるモジュール行がありません。各章の設定を入力してください。");

        // ── Sysprep プロファイル（マスタ作成後に Administrator で実行。順序は Order のみ、ゲート無し）──
        var sysprepName = ctx.MasterName + "_sysprep";
        var sysprep = ctx.ProfileRequests.Where(p => p.Kind == ProfileKind.Sysprep)
            .OrderBy(p => p.Order).ThenBy(p => p.Sequence).ToList();

        if (SysprepEmitter.Enabled(ctx) && sysprep.Count > 0)
        {
            var sp = NewProfile(ctx, sysprepName, ProfileKind.Sysprep);
            sp.Rows.Add(Marker("__AUTOPILOT__", $"WaitSec={Math.Max(0, wait)}"));
            foreach (var req in sysprep) sp.Rows.Add(ToEntry(ctx, req));
            Renumber(sp.Rows);
            plan.Profiles.Add(sp);
        }
        else if (ctx.Snapshot.ProfileNames.Contains(sysprepName))
        {
            var abs = _resolver.GetProfilePath(sysprepName);
            plan.Deletes.Add(new PlanDelete
            {
                AbsPath = abs,
                RelPath = _resolver.ToRelative(abs),
                Reason  = "Sysprep プロファイルを生成しない設定のため",
            });
        }

        // ── 配備プロファイル（廃止）: 以前の生成で作った <名>_deploy.csv が残っていれば取り除く ──
        var deployName = ctx.MasterName + "_deploy";
        if (ctx.Snapshot.ProfileNames.Contains(deployName))
        {
            var abs = _resolver.GetProfilePath(deployName);
            plan.Deletes.Add(new PlanDelete
            {
                AbsPath = abs,
                RelPath = _resolver.ToRelative(abs),
                Reason  = "配備プロファイルは廃止されたため（マスタ設計では生成しません）",
            });
        }
    }

    private PlanProfile NewProfile(MasterContext ctx, string name, ProfileKind kind)
    {
        var abs = _resolver.GetProfilePath(name);
        return new PlanProfile
        {
            Name    = name,
            AbsPath = abs,
            RelPath = _resolver.ToRelative(abs),
            Exists  = ctx.Snapshot.ProfileNames.Contains(name),
            Kind    = kind,
        };
    }

    private static ProfileScriptEntry Marker(string marker, string description) => new()
    {
        ScriptPath  = marker,
        Enabled     = "1",
        Description = description,
    };

    private static ProfileScriptEntry ToEntry(MasterContext ctx, ProfileRequest req)
    {
        var module = ctx.Snapshot.GetModule(req.Module)!;
        var menu   = module.ScriptMenuNames.GetValueOrDefault(req.Script)
                     ?? Path.GetFileNameWithoutExtension(req.Script);
        return new ProfileScriptEntry
        {
            ScriptPath  = $"{module.Kind}/{module.Dir}/{req.Script}",
            Enabled     = "1",
            Description = req.Description ?? menu,
            Segment     = req.SubSegment is not null ? ctx.SegmentFor(req.SubSegment)
                        : req.Isolated             ? ctx.MasterName
                        : "",
            ErrorMode   = req.ErrorMode,
            Group       = req.Kind switch
            {
                ProfileKind.Sysprep => "Sysprep",
                _                   => req.Slot.ToString(),
            },
        };
    }

    private static void Renumber(List<ProfileScriptEntry> rows)
    {
        for (var i = 0; i < rows.Count; i++) rows[i].Order = (i + 1) * 10;
    }

    /// <summary>
    /// 初回生成（LastGenerated が無い）で、同じマスタ名の行・ファイルが既にある場合は
    /// 他人の生成物／手書き行を上書きする恐れがあるため、生成をブロックする。
    /// </summary>
    private static void CheckFirstGenerationCollisions(MasterContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.Answers.LastGenerated)) return;

        foreach (var op in ctx.Plan.CsvOps.Where(o => o.ExistingIsolatedRows > 0))
            ctx.Error($"{op.RelPath} に Segment / タグ「{ctx.MasterName}」の行が既に {op.ExistingIsolatedRows} 件あります（このマスタは初回生成です）。別のマスタ名にするか、既存行を確認してください。");

        foreach (var op in ctx.Plan.RegistryOps.Where(o => o.Exists))
            ctx.Error($"{op.RelPath} が既に存在します（このマスタは初回生成です）。別のマスタ名にするか、ファイルを確認してください。");

        foreach (var p in ctx.Plan.Profiles.Where(p => p.Exists))
            ctx.Error($"プロファイル {p.RelPath} が既に存在します（このマスタは初回生成です）。別のマスタ名にしてください。");
    }

    private static void BuildFileSummaries(MasterPlan plan)
    {
        var list = plan.FileSummaries;
        foreach (var p in plan.Profiles)
            list.Add(new PlanFileSummary
            {
                RelPath = p.RelPath,
                Action  = p.Exists ? "置換" : "新規",
                Detail  = $"{p.Rows.Count} 行",
            });

        foreach (var r in plan.RegistryOps)
            list.Add(new PlanFileSummary
            {
                RelPath = r.RelPath,
                Action  = r.Exists ? "置換" : "新規",
                Detail  = $"{r.Rows.Count} 行",
            });

        foreach (var c in plan.CsvOps)
        {
            string action, detail;
            if (c.Rows.Count == 0)
            {
                action = "削除";
                detail = $"以前の {c.ExistingIsolatedRows} 行を削除";
            }
            else if (c.ExistingIsolatedRows > 0)
            {
                action = "置換";
                detail = $"+{c.Rows.Count} 行（以前の {c.ExistingIsolatedRows} 行を置換）";
            }
            else
            {
                action = "追加";
                detail = $"+{c.Rows.Count} 行";
            }
            list.Add(new PlanFileSummary { RelPath = c.RelPath, Action = action, Detail = detail });
        }

        foreach (var t in plan.TextFiles)
            list.Add(new PlanFileSummary
            {
                RelPath = t.RelPath,
                Action  = t.Exists ? "置換" : "新規",
                Detail  = t.Label,
            });

        foreach (var d in plan.Deletes)
            list.Add(new PlanFileSummary { RelPath = d.RelPath, Action = "削除", Detail = d.Reason });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Apply
    // ═══════════════════════════════════════════════════════════════

    public async Task<MasterApplyResult> ApplyAsync(MasterPlan plan, IProgress<string>? progress = null)
    {
        var result = new MasterApplyResult();

        if (plan.HasErrors)
        {
            result.Error = "計画にエラーがあるため書き込みません。";
            return result;
        }

        foreach (var op in plan.CsvOps)
        {
            progress?.Report(op.RelPath);
            try
            {
                await WriteCsvRowsAsync(op, plan.MasterName);
                result.Written.Add(op.RelPath);
            }
            catch (Exception ex)
            {
                result.Failed.Add($"{op.RelPath}: {ex.Message}");
            }
        }

        foreach (var op in plan.RegistryOps)
        {
            progress?.Report(op.RelPath);
            try
            {
                await WriteRegistryFileAsync(op, plan.MasterName);
                result.Written.Add(op.RelPath);
            }
            catch (Exception ex)
            {
                result.Failed.Add($"{op.RelPath}: {ex.Message}");
            }
        }

        foreach (var t in plan.TextFiles)
        {
            progress?.Report(t.RelPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(t.AbsPath)!);
                Backup(t.AbsPath);
                await File.WriteAllTextAsync(t.AbsPath, t.Content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                result.Written.Add(t.RelPath);
            }
            catch (Exception ex)
            {
                result.Failed.Add($"{t.RelPath}: {ex.Message}");
            }
        }

        foreach (var p in plan.Profiles)
        {
            progress?.Report(p.RelPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p.AbsPath)!);
                Backup(p.AbsPath);
                await _csvService.WriteAsync(Path.GetRelativePath(_resolver.RootPath, p.AbsPath), p.Rows);
                result.Written.Add(p.RelPath);
            }
            catch (Exception ex)
            {
                result.Failed.Add($"{p.RelPath}: {ex.Message}");
            }
        }

        foreach (var d in plan.Deletes)
        {
            progress?.Report($"削除 {d.RelPath}");
            try
            {
                if (File.Exists(d.AbsPath))
                {
                    Backup(d.AbsPath);
                    File.Delete(d.AbsPath);
                }
                result.Written.Add($"{d.RelPath}（削除）");
            }
            catch (Exception ex)
            {
                result.Failed.Add($"{d.RelPath}: {ex.Message}");
            }
        }

        return result;
    }

    private async Task WriteCsvRowsAsync(PlanCsvRows op, string masterName)
    {
        var table = await _fileService.ReadCsvAsDataTableAsync(op.AbsPath);
        if (table.Columns.Count == 0)
            throw new InvalidDataException("CSV のヘッダーを読めませんでした。");

        // 以前の生成行を取り除く（隔離キーで厳密一致）
        var toRemove = new List<DataRow>();
        foreach (DataRow row in table.Rows)
        {
            switch (op.Isolation)
            {
                case PlanIsolation.Segment:
                    // マスタ名そのものと、副セグメント（マスタ名:xxx）の行を取り除く
                    if (MasterContext.OwnsSegment(masterName, row["Segment"]?.ToString()))
                        toRemove.Add(row);
                    break;
                case PlanIsolation.DescriptionTag:
                    if ((row["Description"]?.ToString() ?? "").Contains(op.Tag, StringComparison.Ordinal))
                        toRemove.Add(row);
                    break;
                case PlanIsolation.AdminId:
                    // 旧版の AdminID = マスタ名 の行と、このマスタの管理番号（数字）の行
                    var adminId = row["AdminID"]?.ToString()?.Trim() ?? "";
                    if (adminId == masterName || (op.AdminIdKey is not null && adminId == op.AdminIdKey))
                        toRemove.Add(row);
                    break;
            }
        }
        foreach (var row in toRemove) table.Rows.Remove(row);

        foreach (var src in op.Rows)
        {
            var row = table.NewRow();
            foreach (DataColumn col in table.Columns)
                row[col] = src.GetValueOrDefault(col.ColumnName, "") ?? "";
            table.Rows.Add(row);
        }

        Backup(op.AbsPath);
        await _fileService.WriteCsvFromDataTableAsync(op.AbsPath, table);
    }

    private async Task WriteRegistryFileAsync(PlanRegistryFile op, string masterName)
    {
        // ヘッダーはモジュール同梱の基本 CSV に合わせる（無ければ標準 8 列）
        var headers = DefaultRegistryHeaders.ToList();
        var basePath = Path.Combine(Path.GetDirectoryName(op.AbsPath)!,
            op.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? "reg_hklm_list.csv" : "reg_hkcu_list.csv");
        if (File.Exists(basePath))
        {
            try
            {
                var baseTable = await _fileService.ReadCsvAsDataTableAsync(basePath);
                if (baseTable.Columns.Count > 0)
                    headers = baseTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            }
            catch { /* 標準ヘッダーで続行 */ }
        }

        var table = new DataTable();
        foreach (var h in headers) table.Columns.Add(h);

        var id = 1;
        foreach (var r in op.Rows)
        {
            var row = table.NewRow();
            Set(row, "Enabled", "1");
            Set(row, "AdminID", (id++).ToString());
            Set(row, "SettingTitle", r.SettingTitle);
            Set(row, "KeyPath", r.KeyPath);
            Set(row, "KeyName", r.KeyName);
            Set(row, "Type", r.Type);
            Set(row, "Value", r.Value);
            Set(row, "Segment", string.IsNullOrEmpty(r.Segment) ? masterName : r.Segment);
            table.Rows.Add(row);
        }

        Backup(op.AbsPath);
        await _fileService.WriteCsvFromDataTableAsync(op.AbsPath, table);

        static void Set(DataRow row, string col, string value)
        {
            if (row.Table.Columns.Contains(col)) row[col] = value;
        }
    }

    /// <summary>上書き前に 1 世代だけ .bak を残す（生成は冪等なので復旧用途のみ）。</summary>
    private static void Backup(string absPath)
    {
        if (!File.Exists(absPath)) return;
        File.Copy(absPath, absPath + ".bak", overwrite: true);
    }
}
