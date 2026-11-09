using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Samples.ExternalModule;

/// <summary>
/// **独立编译示例**：只声明一个 <c>serial</c> 标准接入，适配器与参数类型都由宿主按标准解析。
/// </summary>
/// <remarks>
/// <para>
/// 本模块不提供视图；它证明一个外部作者只靠三个 SDK NuGet 包即可声明接入并被打包、加载。
/// </para>
/// <para>
/// <b>两条边界在示例里各占一半</b>：外部接入面只搬原始字节（<see cref="ExternalAccessChannel"/>），
/// 数据单元的类型与组帧由本模块自己声明与实现（<see cref="PortType"/> / <see cref="RawBytesCodec"/>）。
/// </para>
/// </remarks>
[ModuleDefinition(
    "sample",
    "serial-access",
    DisplayName = "示例串口接入",
    Description = "只声明 serial 标准接入的最小外部模块（独立编译示例）",
    Tags = ["sample", "serial"],
    Capabilities = ModuleCapability.SerialPort,
    ParticipatesInDataFlow = true,
    MinHeight = 190)]
public sealed class SampleSerialModule : IModule, IActivitySource
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "sample-serial";

    /// <summary>发送口（设备 → 框架）。</summary>
    public const string RxPort = "rx";

    /// <summary>接收口（框架 → 设备）。</summary>
    public const string TxPort = "tx";

    /// <summary>角色标识。</summary>
    public const string RoleId = "sample-serial";

    /// <summary>显示源：连接状态。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>命令：打开。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：关闭。</summary>
    public const string CloseCommand = "close";

    /// <summary>命令：清空计数。</summary>
    public const string ClearCountersCommand = "clear-counters";

    /// <summary>本模块声明的数据类型标识（模块间约定，模块自己携带）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>本模块声明的数据类型形状：<c>{ len: u16, data: bytes[1472] }</c>。</summary>
    public static readonly ContractShape RawBytesShape = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(RawBytesCodec.Capacity)));

    /// <summary>本模块声明的数据类型（口与角色都引用它）。</summary>
    public static readonly DataTypeDeclaration PortType = new(RawBytesTypeId, RawBytesShape)
    {
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

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        // ① 数据类型（模块间约定，自己声明）→ ② 口 → ③ 角色 → ④ 界面契约 → ⑤ 接入
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
            "示例串口"));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        context.Declaration.DeclareCommand(OpenCommand, "打开");
        context.Declaration.DeclareCommand(CloseCommand, "关闭");
        context.Declaration.DeclareCommand(ClearCountersCommand, "清空计数");

        // 模块只声明"接入名 + 标准标识"；适配器与参数类型都由框架按标准创建/登记。
        context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(AccessName, ExternalAccessStandardIds.Serial)
        {
            MaxUnitBytes = RawBytesCodec.Capacity,
        });

        context.Ports.SetReceiveHandler(TxPort, OnPortData);

        _channel = new ExternalAccessChannel(context, ChannelSpec, Describe);
    }

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnActivated() => _channel?.Attach(OnAccessData);

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

    /// <summary>状态句：连接状态 + 本模块自己的 <c>tx</c> 口压力。</summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    private ExternalAccessStatus Describe(ExternalAccessSnapshot snapshot)
    {
        if (snapshot.State == ExternalAccessState.Open
            && _context is not null
            && _context.Ports.TryGetFlowState(TxPort, out var flow)
            && flow.Saturated)
        {
            return new ExternalAccessStatus(
                $"发送跟不上（积压 {flow.Depth}）",
                ViewInkRoles.Warning);
        }

        return new ExternalAccessStatus(ConnectionText(snapshot));
    }

    private static string ConnectionText(ExternalAccessSnapshot snapshot) => snapshot.State switch
    {
        ExternalAccessState.Open => snapshot.Endpoint is { } endpoint
            ? $"已打开（{endpoint.Address}）"
            : "已打开",
        ExternalAccessState.Opening => "正在打开…",
        ExternalAccessState.Closing => "正在关闭…",
        ExternalAccessState.Faulted => snapshot.Failure is { } failure
            ? $"打开失败（{failure.Kind}）"
            : "打开失败",
        _ => snapshot.Suspended ? "实例已停用" : "未打开",
    };
}
