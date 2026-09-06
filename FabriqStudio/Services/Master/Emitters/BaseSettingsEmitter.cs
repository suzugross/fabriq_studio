using System.Text.RegularExpressions;
using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>2. ライセンス、3. パーティション、4. マスタ作成時の仮ホスト名、5. ネットワーク（IPv6 / NTP）。</summary>
public sealed class BaseSettingsEmitter : IMasterEmitter
{
    public string Name => "基盤設定";

    /// <summary>NetBIOS 名として妥当なコンピューター名（1〜15 文字、英数字とハイフン、先頭末尾はハイフン不可）。</summary>
    private static readonly Regex HostnameRegex = new("^(?!-)[A-Za-z0-9-]{1,15}(?<!-)$", RegexOptions.Compiled);

    public void Emit(MasterContext ctx)
    {
        EmitLicense(ctx);
        EmitPartition(ctx);
        EmitMasterHostname(ctx);
        EmitNetwork(ctx);
    }

    private static void EmitLicense(MasterContext ctx)
    {
        var key = ctx.Get("os_product_key").Trim();
        if (!string.IsNullOrEmpty(key))
        {
            ctx.AddCsvRow("windows_license_config", "license_key.csv", Row(
                ("Enabled", "1"),
                ("ProductKey", ctx.Secret(key)),
                ("Description", "Windows product key (master)")));
            ctx.AddProfile("windows_license_config", "windows_license_install.ps1", ProfileSlot.Base, 10, isolated: true, errorMode: "retry");
        }

        if (ctx.IsTrue("os_activate"))
            ctx.AddProfile("windows_license_config", "windows_license_auth.ps1", ProfileSlot.Base, 20, isolated: false, errorMode: "retry");
    }

    private static void EmitPartition(MasterContext ctx)
    {
        if (!ctx.IsTrue("part_enabled")) return;

        var cGb = ctx.GetInt("part_c_size_gb");
        if (cGb is null or <= 0)
        {
            ctx.Error("パーティション分割を行う場合は C: の容量（GB）を入力してください（3. ドライブ構成）。", "part_c_size_gb");
            return;
        }

        var letter = ctx.Get("part_new_letter").Trim().TrimEnd(':').ToUpperInvariant();
        if (letter.Length != 1 || letter[0] < 'D' || letter[0] > 'Z')
        {
            ctx.Error("新規パーティションのドライブレターは D〜Z の 1 文字で指定してください。", "part_new_letter");
            return;
        }

        var newGb = ctx.GetInt("part_new_size_gb") ?? 0;
        var label = ctx.Get("part_label").Trim();
        if (string.IsNullOrEmpty(label)) label = "Data";

        ctx.AddCsvRow("partition_config", "partition_list.csv", Row(
            ("Enabled", "1"),
            ("DiskNumber", "0"),
            ("SourceDriveLetter", "C"),
            ("SourceSizeMB", (cGb.Value * 1024).ToString()),
            ("NewDriveLetter", letter),
            ("NewSizeMB", (newGb * 1024).ToString()),
            ("FileSystem", "NTFS"),
            ("VolumeLabel", label),
            ("Description", $"Shrink C: to {cGb}GB and create {letter}: (master)")));

        ctx.AddProfile("partition_config", "partition_config.ps1", ProfileSlot.Base, 30, isolated: true);
    }

    /// <summary>
    /// マスタ作成時の仮ホスト名。hostname_config は設定 CSV を持たず hostlist.csv の選択ホスト（SELECTED_NEW_PCNAME）を
    /// 適用するため、hostlist.csv に AdminID=マスタ名 の行を書き、起動時にその行を選んでもらう。
    /// </summary>
    private static void EmitMasterHostname(MasterContext ctx)
    {
        var name = ctx.Get("master_hostname").Trim();
        if (string.IsNullOrEmpty(name)) return;

        if (!HostnameRegex.IsMatch(name) || name.All(char.IsDigit))
        {
            ctx.Error("仮コンピューター名は半角英数字とハイフンで 15 文字以内にしてください（先頭・末尾のハイフン、数字のみは不可）。", "master_hostname");
            return;
        }

        ctx.AddHostlistRow(Row(
            ("OldPCName", ""),
            ("NewPCName", name.ToUpperInvariant())));

        ctx.AddProfile("hostname_config", "hostname_config.ps1", ProfileSlot.Base, 5, isolated: false);
        ctx.Info($"仮コンピューター名 {name.ToUpperInvariant()} は hostlist.csv の管理番号「{ctx.MasterName}」の行に書きます。Fabriq 起動時のホスト選択でこの行を選んでください（IP 列は空のため ipaddress_config は対象外）。");
    }

    private static void EmitNetwork(MasterContext ctx)
    {
        // IPv6 無効化（有線・無線、日本語名と英語名の両方）
        if (ctx.Get("ipv6") == "0")
        {
            foreach (var pattern in new[] { "イーサネット*", "Ethernet*", "Wi-Fi*" })
                ctx.AddCsvRow("ipv6_config", "ipv6_list.csv", Row(
                    ("Enabled", "1"),
                    ("AdapterPattern", pattern),
                    ("IPv6State", "0"),
                    ("Description", $"Disable IPv6 ({pattern}) (master)")));
            ctx.AddProfile("ipv6_config", "ipv6_config.ps1", ProfileSlot.Base, 40, isolated: true);
        }

        // NTP
        if (ctx.IsTrue("ntp_enabled"))
        {
            var server = ctx.Get("ntp_server").Trim();
            if (string.IsNullOrEmpty(server))
            {
                ctx.Error("時刻同期を有効にする場合は NTP サーバーを入力してください（5. ネットワーク）。", "ntp_server");
            }
            else
            {
                ctx.AddCsvRow("time_sync_config", "time_sync_list.csv", Row(
                    ("Enabled", "1"),
                    ("NtpServer", server),
                    ("Description", "NTP server (master)")));
                ctx.AddProfile("time_sync_config", "time_sync_config.ps1", ProfileSlot.Base, 50, isolated: true, errorMode: "retry");
            }
        }
    }
}
