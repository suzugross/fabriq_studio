using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FabriqStudio.Tests;

/// <summary>
/// モジュール配下のパス（"modules/..."）を直接組み立てるコードが増えていないことを検査する。
/// プロファイル別データフォルダ（PDF）対応では、パスの組み立てを IModuleDataResolver に集約しないと
/// 「本体を編集したつもりが PDF が勝って効かない」乖離が黙って生える。
/// 新しく modules 配下を触るコードは IModuleDataResolver を経由すること。やむを得ず直接書く場合は
/// <see cref="Allowed"/> に理由付きで追加する（移行が進むほど一覧は短くなる）。
/// </summary>
public sealed class NoHardcodedModulePathsTest
{
    private static readonly Regex Pattern = new(@"""modules[\\/""]|@""modules", RegexOptions.Compiled);

    /// <summary>許可リスト（FabriqStudio/ からの相対パス → 理由）。</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Services/ModuleDataResolver.cs"]                   = "解決サービス本体（写像の唯一の実装）",
        ["Services/WorkspaceService.cs"]                     = "ワークスペース検証（modules フォルダの存在確認）",
        ["Services/ModuleService.cs"]                        = "モジュール一覧の列挙（フレームワーク資産 module.csv は PDF 対象外）",
        ["Services/LooperService.cs"]                        = "モジュール生成（フレームワーク資産の作成で PDF 対象外）",
        ["Services/FabriqBackupService.cs"]                  = "fabriq バックアップ（ツリー全体を扱う）",
        ["Services/FabriqUpdateService.cs"]                  = "fabriq オーバーレイ更新（コード配布で PDF 対象外）",
        ["Services/PianistTestRunService.cs"]                = "pianist.ps1 の起動（フレームワーク資産）",
        ["Services/Master/MasterProfileGeneratorService.cs"] = "本体ツリーの走査（modules/<tier>/ の列挙。データフォルダ側は IMasterTargetResolver 経由）",
    };

    [Fact]
    public void ModulePathsGoThroughResolver()
    {
        var src = Path.Combine(FindRepoRoot(), "FabriqStudio");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)) continue;
            if (Allowed.ContainsKey(rel)) continue;

            var lineNo = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNo++;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;   // コメントは対象外
                if (Pattern.IsMatch(line)) offenders.Add($"{rel}:{lineNo}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "modules 配下のパスを直接組み立てているコードがあります。IModuleDataResolver を経由してください。\n"
            + "（やむを得ない場合は NoHardcodedModulePathsTest.Allowed に理由付きで追加）\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void AllowedEntriesStillExist()
    {
        var src = Path.Combine(FindRepoRoot(), "FabriqStudio");
        var stale = Allowed.Keys.Where(rel => !File.Exists(Path.Combine(src, rel))).ToList();
        Assert.True(stale.Count == 0, "許可リストに存在しないファイルがあります（整理してください）:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void AllowedEntriesStillNeedTheirExemption()
    {
        // 直書きが無くなったファイルは許可リストから外す（一覧を短く保つ）
        var src = Path.Combine(FindRepoRoot(), "FabriqStudio");
        var unnecessary = new List<string>();
        foreach (var rel in Allowed.Keys)
        {
            var path = Path.Combine(src, rel);
            if (!File.Exists(path)) continue;
            var hit = File.ReadLines(path).Any(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && Pattern.IsMatch(l));
            if (!hit) unnecessary.Add(rel);
        }
        Assert.True(unnecessary.Count == 0, "直書きが無くなったので許可リストから外せます:\n  " + string.Join("\n  ", unnecessary));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FabriqStudio.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("FabriqStudio.sln が見つかりません（テストはリポジトリ内で実行してください）。");
    }
}
