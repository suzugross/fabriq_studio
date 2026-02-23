using CommunityToolkit.Mvvm.Messaging.Messages;
using FabriqStudio.Models;

namespace FabriqStudio.Messages;

/// <summary>端末詳細画面への遷移要求</summary>
public class ShowHostDetailMessage : ValueChangedMessage<HostEntry>
{
    public ShowHostDetailMessage(HostEntry host) : base(host) { }
}

/// <summary>モジュール詳細画面への遷移要求</summary>
public class ShowModuleDetailMessage : ValueChangedMessage<ModuleMasterEntry>
{
    public ShowModuleDetailMessage(ModuleMasterEntry module) : base(module) { }
}

/// <summary>プロファイル詳細（編集）画面への遷移要求</summary>
public class ShowProfileDetailMessage : ValueChangedMessage<ProfileEntry>
{
    public ShowProfileDetailMessage(ProfileEntry profile) : base(profile) { }
}

/// <summary>一覧画面への戻り要求（targetPage: "HostList" / "ModuleEdit" / "BasicParams"）</summary>
public class NavigateBackMessage : ValueChangedMessage<string>
{
    public NavigateBackMessage(string targetPage) : base(targetPage) { }
}

/// <summary>
/// 詳細画面で保存が完了したことを通知するメッセージ。
/// BasicParamsViewModel がこのメッセージを受信してデータを自動リフレッシュする。
/// </summary>
public class WorkspaceDataUpdatedMessage : ValueChangedMessage<string>
{
    public WorkspaceDataUpdatedMessage(string source) : base(source) { }
}
