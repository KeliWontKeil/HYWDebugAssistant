using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.Modules.TcpServer.Views;

/// <summary>**TCP 服务端视图的键**（XAML 只经 <c>{x:Static}</c> 引用）。</summary>
public static class TcpServerViewKeys
{
    public const string Link = TcpServerModule.LinkSource;
    public const string State = TcpServerModule.StateSource;
    public const string RxCount = TcpServerModule.RxCountSource;
    public const string TxCount = TcpServerModule.TxCountSource;
    public const string DropCount = TcpServerModule.DropCountSource;
    public const string ConnCount = TcpServerModule.ConnCountSource;
    public const string Open = TcpServerModule.OpenCommand;
    public const string Close = TcpServerModule.CloseCommand;
    public const string ClearCounters = TcpServerModule.ClearCountersCommand;
    public const string ClearCountersIcon = "icon.refresh";

    public const string LocalAddress = ExternalAccessFieldNames.LocalAddress;
    public const string LocalPort = ExternalAccessFieldNames.LocalPort;
    public const string MaxConnections = ExternalAccessFieldNames.MaxConnections;
    public const string NoDelay = ExternalAccessFieldNames.NoDelay;
}
