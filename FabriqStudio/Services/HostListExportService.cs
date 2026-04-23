using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CsvHelper;
using FabriqStudio.Helpers;
using FabriqStudio.Models;

namespace FabriqStudio.Services;

/// <summary>
/// 端末一覧エクスポートの実装。
/// <para>
/// UI 側の <see cref="HostEntry"/> インスタンスを直接変更しないよう、<see cref="HostEntry.Clone"/>
/// でディープコピーした上で復号・書き出しを行う。
/// </para>
/// </summary>
public class HostListExportService : IHostListExportService
{
    private readonly ICryptoService _crypto;

    public HostListExportService(ICryptoService crypto)
    {
        _crypto = crypto;
    }

    public Task<HostListExportResult> ExportAsync(HostListExportRequest request)
        => Task.Run(() => ExportSync(request));

    private HostListExportResult ExportSync(HostListExportRequest request)
    {
        var now          = DateTime.Now;
        var folderName   = $"hostlist_export_{now:yyyyMMdd_HHmmss}";
        var exportFolder = Path.Combine(request.ParentFolder, folderName);
        Directory.CreateDirectory(exportFolder);

        var errors = new List<string>();
        var clones = request.Hosts.Select(h => h.Clone()).ToList();

        int decrypted = 0;
        if (request.Decrypt)
            decrypted = DecryptClones(clones, errors);

        var remainingEnc = CountRemainingEnc(clones);

        // ── hostlist.csv（BOM 付き UTF-8 / fabriq 本体と同じ書き方） ──
        var csvPath = Path.Combine(exportFolder, "hostlist.csv");
        using (var writer = new StreamWriter(csvPath, append: false,
                   encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(clones);
        }

        // ── README.txt（BOM なし UTF-8 / テキストエディタ汎用互換） ──
        var readmePath = Path.Combine(exportFolder, "README.txt");
        File.WriteAllText(readmePath,
            BuildReadme(now, clones.Count, request.Decrypt, decrypted, remainingEnc, request.Memo),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new HostListExportResult(exportFolder, clones.Count, decrypted, remainingEnc, errors);
    }

    /// <summary>HostEntry の暗号化可能な string プロパティ（列挙・反射のコスト低減のため静的キャッシュ）。</summary>
    private static readonly Lazy<IReadOnlyList<PropertyInfo>> EncryptableProps = new(() =>
        typeof(HostEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanWrite
                     && CryptoHelper.IsEncryptableColumn(p.Name))
            .ToList());

    private int DecryptClones(List<HostEntry> clones, List<string> errors)
    {
        if (!_crypto.HasPassphrase)
        {
            errors.Add("パスフレーズが未設定のため復号をスキップしました。");
            return 0;
        }

        int decrypted = 0;
        foreach (var host in clones)
        {
            foreach (var prop in EncryptableProps.Value)
            {
                var value = prop.GetValue(host)?.ToString() ?? "";
                if (!value.StartsWith("ENC:", StringComparison.Ordinal)) continue;
                try
                {
                    prop.SetValue(host, _crypto.Decrypt(value, _crypto.MasterPassphrase!));
                    decrypted++;
                }
                catch (Exception ex)
                {
                    errors.Add($"AdminID={host.AdminID}/{prop.Name}: {ex.Message}");
                }
            }
        }
        return decrypted;
    }

    private static int CountRemainingEnc(List<HostEntry> clones)
    {
        int count = 0;
        foreach (var host in clones)
            foreach (var prop in EncryptableProps.Value)
            {
                var value = prop.GetValue(host)?.ToString() ?? "";
                if (value.StartsWith("ENC:", StringComparison.Ordinal))
                    count++;
            }
        return count;
    }

    private static string BuildReadme(
        DateTime now, int hostCount, bool decryptRequested,
        int decrypted, int remainingEnc, string memo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("fabriq studio — hostlist export");
        sb.AppendLine("================================");
        sb.AppendLine($"Exported at         : {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Host count          : {hostCount}");
        sb.AppendLine($"Decrypt requested   : {(decryptRequested ? "yes" : "no")}");
        if (decryptRequested)
            sb.AppendLine($"Decrypted cells     : {decrypted}");
        sb.AppendLine($"Remaining ENC cells : {remainingEnc}");

        if (!string.IsNullOrWhiteSpace(memo))
        {
            sb.AppendLine();
            sb.AppendLine("User memo");
            sb.AppendLine("---------");
            sb.AppendLine(memo.TrimEnd());
        }

        return sb.ToString();
    }
}
