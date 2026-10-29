using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Modules.Udp.Views;

/// <summary>**UDP 视图的键**（XAML 只经 <c>{x:Static}</c> 引用）。</summary>
public static class UdpViewKeys
{
    public const string Link = UdpModule.LinkSource;
    public const string State = UdpModule.StateSource;
    public const string RxCount = UdpModule.RxCountSource;
    public const string TxCount = UdpModule.TxCountSource;
    public const string DropCount = UdpModule.DropCountSource;
    public const string Open = UdpModule.OpenCommand;
    public const string Close = UdpModule.CloseCommand;

    public const string LocalAddress = ExternalAccessFieldNames.LocalAddress;
    public const string LocalPort = ExternalAccessFieldNames.LocalPort;
    public const string RemoteAddress = ExternalAccessFieldNames.RemoteAddress;
    public const string RemotePort = ExternalAccessFieldNames.RemotePort;
    public const string Broadcast = ExternalAccessFieldNames.Broadcast;
}
