using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.Udp;

/// <summary>
/// **UDP**：绑定一个本地端口收发数据报 —— 收到的数据报从 <c>rx</c> 出去，
/// 要发出去的从 <c>tx</c> 进来（发给参数里的默认目标）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>绑定 = 一个会话</b>：UDP 无连接 ⇒ 会话里没有"连接状态"，只有"绑定没绑定"；</item>
///   <item>一次接收 = 一个数据报；超过单元容量的部分被截断并计数；</item>
///   <item><b>不自动重试</b>：绑定失败/解绑后用户再次〔绑定〕；</item>
///   <item><b>两条边界</b>：外部接入面只搬原始字节（<see cref="ExternalAccessChannel"/>）；
///     数据单元的类型与组帧由本模块自己声明与实现（<see cref="PortType"/> / <see cref="RawBytesCodec"/>）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "udp",
    DisplayNameKey = UdpText.Title,
    DescriptionKey = UdpText.Description,
    Tags = ["udp", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 170,
    ViewFactory = typeof(Views.UdpViewFactory))]
public sealed class UdpModule : IModule, IActivitySource
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "udp";

    /// <summary>**发送口**（本模块是源）：收到的数据报。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：要发出去的数据报。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id。</summary>
    public const string RoleId = "udp";

    /// <summary>显示源：绑定状态（状态灯按它的外观角色着色）。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>命令：绑定。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：解绑。</summary>
    public const string CloseCommand = "close";

    /// <summary>命令：清空本接入的计数（队列数据、连接数与故障态都不动）。</summary>
    public const string ClearCountersCommand = "clearCounters";

    /// <summary>本模块声明的数据类型标识（模块间约定，模块自己携带）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>本模块声明的数据类型形状：<c>{ len: u16, data: bytes[1472] }</c>。</summary>
    public static readonly ContractShape RawBytesShape = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(RawBytesCodec.Capacity)));

    /// <summary>本模块声明的数据类型（口与角色都引用它）。</summary>
    public static readonly DataTypeDeclaration PortType = new(RawBytesTypeId, RawBytesShape)
    {
        // 「速率只要有效数据」：`len` 就是有效字节数 ⇒ 声明它，框架逐单元读它累计有效字节。
        Metering = new MeteringBlock { PayloadLengthField = "len" },
    };

    private static readonly ExternalAccessChannelSpec ChannelSpec = new(
        AccessName,
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        CloseCommand,
        ClearCountersCommand);

    private readonly byte[] _frame = new byte[RawBytesCodec.FrameSize];
    private IModuleContext? _context;
    private ExternalAccessChannel? _channel;

    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集）。</summary>
    private static string Tr(string key) => TextCatalog.Get(typeof(UdpModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(UdpModule).Assembly, key, args);

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        // ① 数据类型（模块间约定，本模块自己声明）→ ② 口 → ③ 角色 → ④ 界面契约 → ⑤ 接入
        context.Declaration.DeclareDataType(PortType);

        context.Declaration.DeclarePort(new PortDeclaration(RxPort, PortDirection.Send, PortType.Shape)
        {
            TypeId = PortType.TypeId,
            Edge = PortEdge.Bottom,
        });

        context.Declaration.DeclarePort(new PortDeclaration(TxPort, PortDirection.Receive, PortType.Shape)
        {
            TypeId = PortType.TypeId,
            Edge = PortEdge.Top,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Both(
            RoleId,
            PortType.TypeId,
            TxPort,
            RxPort,
            Tr(UdpText.RoleDatagram)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        context.Declaration.DeclareCommand(OpenCommand, Tr(UdpText.Open));
        context.Declaration.DeclareCommand(CloseCommand, Tr(UdpText.Close));
        context.Declaration.DeclareCommand(ClearCountersCommand, Tr(UdpText.ViewClearCounters));

        // 模块只声明"接入名 + 标准标识"；适配器与参数类型都由框架按标准创建/登记。
        context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(AccessName, ExternalAccessStandardIds.Udp)
        {
            MaxUnitBytes = RawBytesCodec.Capacity,
        });

        context.Ports.SetReceiveHandler(TxPort, OnPortData);

        _channel = new ExternalAccessChannel(context, ChannelSpec, Describe, DescribePressure);
    }

    /// <inheritdoc />
    public void OnActivated() => _channel?.Attach(OnAccessData);

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            OnCommand(command);
        }

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            OnInput(source, text);
        }

        OnWork();
    }

    /// <summary>执行一条命令（step 与直接测试共用）。</summary>
    public void OnCommand(string commandName) => _ = _channel?.HandleCommand(commandName);

    /// <summary>输入回调（本模块没有输入语义）。</summary>
    public static void OnInput(string sourceName, string text)
    {
        _ = sourceName;
        _ = text;
    }

    /// <summary>发布状态（step 与直接测试共用）。</summary>
    public void OnWork() => _channel?.PublishStatus();

    /// <summary>外部原始字节到达（实例线程）：组帧并从 <c>rx</c> 口发出。</summary>
    /// <param name="payload">原始字节块（长度由框架的单元上限决定）。</param>
    private void OnAccessData(ReadOnlySpan<byte> payload)
    {
        if (_context is null || !RawBytesCodec.TryWriteFrame(payload, _frame))
        {
            return;
        }

        _ = _context.Ports.TrySend(RxPort, _frame);
    }

    /// <summary>口数据到达（实例线程）：按 <c>len</c> 取有效字节并写回外部。</summary>
    /// <param name="payload">定长载荷。</param>
    private void OnPortData(ReadOnlySpan<byte> payload)
    {
        if (_context is null || _channel is null)
        {
            return;
        }

        if (!RawBytesCodec.TryReadContent(payload, out var content))
        {
            _context.Ports.ReportLoss(TxPort, DropReason.ModuleInternal, 1, payload.Length);
            return;
        }

        if (content.Length == 0)
        {
            return;
        }

        _ = _channel.Write(content);
    }

    /// <summary>状态句：绑定句 + 本模块自己的 <c>tx</c> 口压力（口侧事实，通道不读）。</summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    private ExternalAccessStatus Describe(ExternalAccessSnapshot snapshot)
    {
        if (snapshot.State == ExternalAccessState.Open
            && _context is not null
            && _context.Ports.TryGetFlowState(TxPort, out var flow))
        {
            if (flow.DroppedUnits > 0
                && flow.SinceLastDrop is { } since
                && since <= ExternalAccessChannel.LossHintWindow)
            {
                return new ExternalAccessStatus(Tr(UdpText.StatusSendLost), ViewInkRoles.Danger);
            }

            if (flow.Saturated)
            {
                return new ExternalAccessStatus(
                    Tr(UdpText.StatusTxSaturated, flow.Depth, flow.DroppedWhileSaturatedUnits),
                    ViewInkRoles.Warning);
            }
        }

        return new ExternalAccessStatus(ConnectionText(snapshot));
    }

    private static string ConnectionText(ExternalAccessSnapshot snapshot)
    {
        var endpoint = EndpointText(snapshot.Endpoint);

        return snapshot.State switch
        {
            ExternalAccessState.Open => endpoint.Length == 0
                ? Tr(UdpText.StatusBound)
                : Tr(UdpText.StatusBoundDetail, endpoint),
            ExternalAccessState.Opening => endpoint.Length == 0
                ? Tr(UdpText.StatusBinding)
                : Tr(UdpText.StatusBindingDetail, endpoint),
            ExternalAccessState.Closing => Tr(UdpText.StatusUnbinding),
            ExternalAccessState.Faulted => FailureText(snapshot),
            _ => snapshot.Suspended
                ? Tr(UdpText.StatusSuspended)
                : snapshot.RejectedSinceOpen > 0
                    ? Tr(UdpText.StatusRejected, snapshot.RejectedSinceOpen)
                    : Tr(UdpText.StatusIdle),
        };
    }

    /// <summary>按失败种类从本模块表取句子（未覆盖的种类走未知回退）。</summary>
    private static string FailureText(ExternalAccessSnapshot snapshot)
    {
        var failure = snapshot.Failure;
        var endpoint = EndpointText(snapshot.Endpoint);
        var value = failure?.Value ?? string.Empty;

        return failure?.Kind switch
        {
            ExternalAccessFailureKind.FrameworkInternal => Tr(UdpText.StatusFailedFrameworkInternal),
            ExternalAccessFailureKind.ParametersNotRegistered => Tr(UdpText.StatusFailedParametersNotRegistered),
            ExternalAccessFailureKind.AddressInvalid => Tr(UdpText.StatusFailedAddressInvalid, value),
            ExternalAccessFailureKind.WildcardAddressNotAllowed => Tr(UdpText.StatusFailedWildcardAddress, value),
            ExternalAccessFailureKind.PortOutOfRange => Tr(UdpText.StatusFailedPortOutOfRange, value),
            ExternalAccessFailureKind.PortInUse => Tr(UdpText.StatusFailedPortInUse, endpoint),
            ExternalAccessFailureKind.BindFailed => Tr(UdpText.StatusFailedBindFailed),
            ExternalAccessFailureKind.OpenFailed => Tr(UdpText.StatusFailedOpenFailed),
            ExternalAccessFailureKind.ReadFailed => Tr(UdpText.StatusFailedReadFailed),
            ExternalAccessFailureKind.WriteFailed => Tr(UdpText.StatusFailedWriteFailed),
            _ => Tr(UdpText.StatusFailedUnknown),
        };
    }

    /// <summary>把结构化端点拼成 <c>address[:port]</c>（只搬运事实，不含界面词）。</summary>
    private static string EndpointText(ExternalAccessEndpoint? endpoint) =>
        endpoint is { } value
            ? value.Port is { } port
                ? $"{value.Address}:{port}"
                : value.Address
            : string.Empty;

    /// <summary>会话侧压力句（措辞归本模块）。</summary>
    private static string? DescribePressure(ExternalAccessPressure kind, ExternalAccessSnapshot snapshot) => kind switch
    {
        ExternalAccessPressure.ReceiveLost => Tr(UdpText.StatusReceiveLost, snapshot.ReceiveDroppedSinceOpen),
        ExternalAccessPressure.SendLost => Tr(UdpText.StatusSendLost),
        ExternalAccessPressure.SendBacklogged => Tr(UdpText.StatusTxBacklog, snapshot.SendBacklogDepth),
        ExternalAccessPressure.ReceiveSaturated => Tr(UdpText.StatusRxSaturated, snapshot.ReceiveQueueDepth),
        _ => null,
    };
}
