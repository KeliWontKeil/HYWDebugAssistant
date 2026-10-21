using HYWDebugAssistant.Access.Parameters;

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

    public const string LocalAddress = nameof(TcpServerConfig.LocalAddress);
    public const string LocalPort = nameof(TcpServerConfig.LocalPort);
    public const string MaxConnections = nameof(TcpServerConfig.MaxConnections);
    public const string NoDelay = nameof(TcpServerConfig.NoDelay);
}
