namespace HYWDebugAssistant.Access;

/// <summary>
/// 一个**外部接入模块**的共用骨架参数：接入名、两个口、显示源、命令、容量。
/// </summary>
/// <remarks>
/// 四个模块的形态相同（一个会话 + 一进一出 + 状态/计数），差异只在接入名、口名、角色名、按钮文案与状态文案。
/// 把差异收在这份声明里，模块类只提供 <see cref="AccessModuleSpec"/> 与文案，不再各写一遍接线。
/// </remarks>
/// <param name="AccessName">接入名（模块内唯一）。</param>
/// <param name="RxPort">发送口名（设备/外部 → 框架）。</param>
/// <param name="TxPort">接收口名（框架 → 设备/外部）。</param>
/// <param name="RoleId">角色标识。</param>
/// <param name="RoleDisplayName">角色呈现名。</param>
/// <param name="LinkSource">显示源：连接状态（状态灯按外观角色着色）。</param>
/// <param name="StateSource">显示源：一句话状态。</param>
/// <param name="RxCountSource">显示源：收字节数。</param>
/// <param name="TxCountSource">显示源：发字节数。</param>
/// <param name="DropCountSource">显示源：丢弃字节数。</param>
/// <param name="OpenCommand">命令：打开 / 连接 / 启动 / 绑定。</param>
/// <param name="OpenTitle">命令按钮文案（给人看的）。</param>
/// <param name="CloseCommand">命令：关闭 / 断开 / 停止 / 解绑。</param>
/// <param name="CloseTitle">命令按钮文案。</param>
/// <param name="ConnCountSource">显示源：当前连接数（TCP 服务端用；其余为 <see langword="null"/>）。</param>
/// <param name="Capacity">单单元字节区容量（默认与 RawBytes 一致）。</param>
public sealed record AccessModuleSpec(
    string AccessName,
    string RxPort,
    string TxPort,
    string RoleId,
    string RoleDisplayName,
    string LinkSource,
    string StateSource,
    string RxCountSource,
    string TxCountSource,
    string DropCountSource,
    string OpenCommand,
    string OpenTitle,
    string CloseCommand,
    string CloseTitle,
    string? ConnCountSource = null,
    int Capacity = RawBytesFraming.Capacity);
