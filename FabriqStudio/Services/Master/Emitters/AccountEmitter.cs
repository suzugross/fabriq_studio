using static FabriqStudio.Services.Master.Emitters.EmitterHelpers;

namespace FabriqStudio.Services.Master.Emitters;

/// <summary>
/// 6. ユーザーアカウント: 組み込み Administrator（builtin_admin_config）と追加ローカルユーザー（local_user_config）。
/// マスタ作成は組み込み Administrator で行う前提のため、作業用アカウントや AutoLogon は作らない。
/// </summary>
public sealed class AccountEmitter : IMasterEmitter
{
    public string Name => "アカウント";

    public void Emit(MasterContext ctx)
    {
        EmitBuiltinAdmin(ctx);
        EmitLocalUsers(ctx);
    }

    private static void EmitBuiltinAdmin(MasterContext ctx)
    {
        if (!ctx.IsTrue("admin_enable")) return;

        var pass = ctx.Get("admin_password");
        if (string.IsNullOrEmpty(pass))
        {
            ctx.Error("Administrator のパスワードを入力してください（6. ユーザーアカウント）。", "admin_password");
            return;
        }

        ctx.AddCsvRow("builtin_admin_config", "builtin_admin.csv", Row(
            ("Enabled", "1"),
            ("Password", ctx.Secret(pass)),
            ("PasswordNeverExpires", ctx.IsTrue("admin_never_expires") ? "1" : "0"),
            ("Description", "Built-in Administrator (master)")));
        ctx.AddProfile("builtin_admin_config", "builtin_admin_config.ps1", ProfileSlot.Account, 10, isolated: true);
    }

    private static void EmitLocalUsers(MasterContext ctx)
    {
        var rows = ctx.Table("local_users");
        var any = false;
        foreach (var r in rows)
        {
            var name = r.Cell("UserName").Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var pass = r.Cell("Password");
            if (string.IsNullOrEmpty(pass))
                ctx.Warn($"ローカルユーザー {name} のパスワードが空です。", "local_users");

            var group = r.Cell("Group").Trim();
            if (string.IsNullOrEmpty(group)) group = "Administrators";
            if (group.Equals("administrators", StringComparison.OrdinalIgnoreCase)) group = "Administrators";

            ctx.AddCsvRow("local_user_config", "local_user_list.csv", Row(
                ("Enabled", "1"),
                ("UserName", name),
                ("Password", ctx.Secret(pass)),
                ("PasswordNeverExpires", r.Cell("PasswordNeverExpires").Trim() == "0" ? "0" : "1"),
                ("UserMayNotChangePassword", "0"),
                ("Group", group),
                ("Description", r.Cell("Description"))));
            any = true;
        }

        if (any)
            ctx.AddProfile("local_user_config", "local_user_config.ps1", ProfileSlot.Account, 20, isolated: true);
    }
}
