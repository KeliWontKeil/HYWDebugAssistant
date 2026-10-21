using HYWDebugAssistant.Access.Parameters;

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

    public const string RemoteHost = nameof(TcpClientConfig.RemoteHost);
    public const string RemotePort = nameof(TcpClientConfig.RemotePort);
    public const string ConnectTimeout = nameof(TcpClientConfig.ConnectTimeout);
    public const string NoDelay = nameof(TcpClientConfig.NoDelay);
}
