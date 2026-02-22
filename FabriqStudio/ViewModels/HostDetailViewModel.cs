using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FabriqStudio.Messages;
using FabriqStudio.Models;

namespace FabriqStudio.ViewModels;

/// <summary>
/// 端末詳細表示 — HostListView でダブルクリックされた HostEntry を表示する。
/// 将来的に IsReadOnly="False" にするだけで各 TextBox が編集可能になるレイアウトを提供する。
/// </summary>
public partial class HostDetailViewModel : ObservableObject
{
    [ObservableProperty] private HostEntry?                      _host;
    [ObservableProperty] private ObservableCollection<PrinterInfo> _printers = [];

    /// <summary>選択された端末を読み込み、プリンターリストを構築する。</summary>
    public void Load(HostEntry host)
    {
        Host     = host;
        Printers = new ObservableCollection<PrinterInfo>(BuildPrinters(host));
    }

    private static IEnumerable<PrinterInfo> BuildPrinters(HostEntry h) =>
    [
        new() { Number = 1,  Name = h.Printer1Name,  Driver = h.Printer1Driver,  Port = h.Printer1Port  },
        new() { Number = 2,  Name = h.Printer2Name,  Driver = h.Printer2Driver,  Port = h.Printer2Port  },
        new() { Number = 3,  Name = h.Printer3Name,  Driver = h.Printer3Driver,  Port = h.Printer3Port  },
        new() { Number = 4,  Name = h.Printer4Name,  Driver = h.Printer4Driver,  Port = h.Printer4Port  },
        new() { Number = 5,  Name = h.Printer5Name,  Driver = h.Printer5Driver,  Port = h.Printer5Port  },
        new() { Number = 6,  Name = h.Printer6Name,  Driver = h.Printer6Driver,  Port = h.Printer6Port  },
        new() { Number = 7,  Name = h.Printer7Name,  Driver = h.Printer7Driver,  Port = h.Printer7Port  },
        new() { Number = 8,  Name = h.Printer8Name,  Driver = h.Printer8Driver,  Port = h.Printer8Port  },
        new() { Number = 9,  Name = h.Printer9Name,  Driver = h.Printer9Driver,  Port = h.Printer9Port  },
        new() { Number = 10, Name = h.Printer10Name, Driver = h.Printer10Driver, Port = h.Printer10Port },
    ];

    [RelayCommand]
    private void NavigateBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackMessage("HostList"));
}
