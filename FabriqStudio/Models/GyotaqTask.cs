using CommunityToolkit.Mvvm.ComponentModel;

namespace FabriqStudio.Models;

/// <summary>
/// Digital Gyotaq タスクリスト（task_list.csv）の 1 行を表すモデル。
/// CsvHelper の列名マッピング（PascalCase）と ObservableObject を兼用する。
/// Enabled は CSV 上で 0/1、C# 上で bool として扱う。
/// </summary>
public partial class GyotaqTask : ObservableObject
{
    /// <summary>タスク有効フラグ。CSV 上では 0/1 として保存。</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>タスクID。自動採番 T001, T002 ...</summary>
    [ObservableProperty] private string _taskId = "";

    /// <summary>タスクの短縮タイトル（最大40文字、スクリーンショットファイル名にも使用）。</summary>
    [ObservableProperty] private string _taskTitle = "";

    /// <summary>ユーザーへの作業指示テキスト（複数行可）。</summary>
    [ObservableProperty] private string _instruction = "";

    /// <summary>自動起動コマンド（URI / exe パス）。省略可。</summary>
    [ObservableProperty] private string _openCommand = "";

    /// <summary>OpenCommand の引数。省略可。</summary>
    [ObservableProperty] private string _openArgs = "";
}
