namespace HYWDebugAssistant.Sdk.AccessKit;

/// <summary>
/// **外部接入通道的声明面**：接入名 + 显示源名 + 命令名。
/// </summary>
/// <remarks>
/// <para>
/// 这里只有"名字"：接入名由模块交给框架，显示源与命令由模块在**自己的**注册钩子里登记，
/// 通道按这些名字读写。**不含**端口、数据类型与组帧 —— 那些属于模块间通信。
/// </para>
/// <para>
/// 名字只写一次：模块既用它声明，也用它构造本记录；两处各写一份时改名必然漏一处。
/// </para>
/// </remarks>
/// <param name="AccessName">接入名（模块内唯一，声明时登记）。</param>
/// <param name="LinkSource">显示源：连接状态（状态灯按它着色）。</param>
/// <param name="StateSource">显示源：一句话状态。</param>
/// <param name="RxCountSource">显示源：收字节数。</param>
/// <param name="TxCountSource">显示源：发字节数。</param>
/// <param name="DropCountSource">显示源：丢弃字节数。</param>
/// <param name="OpenCommand">命令：打开 / 连接 / 绑定 / 启动。</param>
/// <param name="CloseCommand">命令：关闭 / 断开 / 解绑 / 停止。</param>
/// <param name="ClearCountersCommand">命令：清空本接入的计数。</param>
/// <param name="ConnCountSource">显示源：当前连接数（多连接会话用；其余为 <see langword="null"/>）。</param>
public sealed record ExternalAccessChannelSpec(
    string AccessName,
    string LinkSource,
    string StateSource,
    string RxCountSource,
    string TxCountSource,
    string DropCountSource,
    string OpenCommand,
    string CloseCommand,
    string ClearCountersCommand,
    string? ConnCountSource = null);
