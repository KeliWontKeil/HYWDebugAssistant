using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Modules.TcpClient.Views;

/// <summary>**TCP 客户端视图的键**（XAML 只经 <c>{x:Static}</c> 引用）。</summary>
public static class TcpClientViewKeys
{
    public const string Link = TcpClientModule.LinkSource;
    public const string State = TcpClientModule.StateSource;
    public const string RxCount = TcpClientModule.RxCountSource;
    public const string TxCount = TcpClientModule.TxCountSource;
    public const string DropCount = TcpClientModule.DropCountSource;
    public const string Open = TcpClientModule.OpenCommand;
    public const string Close = TcpClientModule.CloseCommand;
    public const string ClearCounters = TcpClientModule.ClearCountersCommand;
    public const string ClearCountersIcon = "icon.refresh";

    public const string RemoteHost = ExternalAccessFieldNames.RemoteHost;
    public const string RemotePort = ExternalAccessFieldNames.RemotePort;
    public const string ConnectTimeout = ExternalAccessFieldNames.ConnectTimeout;
    public const string NoDelay = ExternalAccessFieldNames.NoDelay;
}
