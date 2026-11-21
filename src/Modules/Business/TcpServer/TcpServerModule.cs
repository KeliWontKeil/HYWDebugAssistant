using System.Globalization;
using System.Net;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.TcpServer;

/// <summary>
/// **TCP 服务端**：监听一个端口，把每条进来的连接接成一路数据 —— 各连接来的字节从 <c>rx</c> 出去，
/// 发往各连接的字节从 <c>tx</c> 进来。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>监听 = 一个会话，连接 = 它的多路</b>：对数据面而言仍是一进一出两个口；</item>
///   <item><b>单条客户端断开只清该连接</b>，不把监听打成故障；监听本身失败才变红；</item>
///   <item><b>不自动重试</b>：停止/失败后用户再次〔启动〕；</item>
///   <item><b>两条边界</b>：外部接入面只搬原始字节（<see cref="ExternalAccessChannel"/>）；
///     数据单元的类型与组帧由本模块自己声明与实现（<see cref="PortType"/> / <see cref="RawBytesCodec"/>）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "tcp-server",
    DisplayNameKey = TcpServerText.Title,
    DescriptionKey = TcpServerText.Description,
    Tags = ["port", "tcp", "server", "io"],
    Capabilities = ModuleCapability.Network,
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.TcpServerViewFactory))]
public sealed class TcpServerModule : IModule, IActivitySource
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "tcp-server";

    /// <summary>**发送口**（本模块是源）：客户端 → 框架。</summary>
    public const string RxPort = "rx";

    /// <summary>**接收口**（本模块是宿）：框架 → 客户端。</summary>
    public const string TxPort = "tx";

    /// <summary>数据角色 Id。</summary>
    public const string RoleId = "tcp-server";

    /// <summary>显示源：监听状态（状态灯按它的外观角色着色）。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>显示源：当前连接数。</summary>
    public const string ConnCountSource = "connCount";

    /// <summary>命令：启动监听。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：停止监听。</summary>
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
        ClearCountersCommand,
        ConnCountSource);

    private readonly byte[] _frame = new byte[RawBytesCodec.FrameSize];
    private readonly InputValidity _validity = new();
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);
    private IModuleContext? _context;
    private ExternalAccessChannel? _channel;

    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集）。</summary>
    private static string Tr(string key) => TextCatalog.Get(typeof(TcpServerModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(TcpServerModule).Assembly, key, args);

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        // ① 数据类型（模块间约定，本模块自己声明）→ ② 口 → ③ 角色 → ④ 界面契约 → ⑤ 接入
        context.Declaration.DeclareDataType(PortType);

        // 端口声明在 Views/TcpServerView.axaml 的 ModuleView.Ports 里。

        context.Declaration.DeclareRole(RoleDeclaration.Both(
            RoleId,
            PortType.TypeId,
            TxPort,
            RxPort,
            Tr(TcpServerText.RoleListen)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(ConnCountSource, ViewDataSourceKind.Value, ContractPrimitive.U32));

        context.Declaration.DeclareCommand(OpenCommand, Tr(TcpServerText.Open));
        context.Declaration.DeclareCommand(CloseCommand, Tr(TcpServerText.Close));
        context.Declaration.DeclareCommand(ClearCountersCommand, Tr(TcpServerText.ViewClearCounters));

        context.Declaration.DeclareInput(ExternalAccessFieldNames.LocalAddress);
        context.Declaration.DeclareInput(ExternalAccessFieldNames.LocalPort);
        context.Declaration.DeclareInput(ExternalAccessFieldNames.MaxConnections);

        // 模块只声明"接入名 + 标准标识"；适配器与参数类型都由框架按标准创建/登记。
        context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(AccessName, ExternalAccessStandardIds.TcpServer)
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

        // **先输入后命令**：刚改完字段就点“启动”时，命令必须看到最新校验结论。
        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            OnInput(source, text);
        }

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            OnCommand(command);
        }

        if ((reason & StepReason.Start) != 0)
        {
            ValidateConfiguration();
        }

        OnWork();
    }

    /// <summary>执行一条命令（step 与直接测试共用）。</summary>
    public void OnCommand(string commandName)
    {
        if (_context is not null)
        {
            ValidateConfiguration();
        }

        _ = _channel?.HandleCommand(commandName);
    }

    /// <summary>输入回调：只由文本框/选择变化事件触发。</summary>
    public void OnInput(string sourceName, string text)
    {
        if (IsConfigurationField(sourceName))
        {
            _drafts[sourceName] = text;
            ValidateConfiguration();
        }
    }

    /// <summary>配置被框架写回（还原/导入/字段提交）：按新配置重算，已结清的草稿自然丢弃。</summary>
    public void OnConfigurationChanged()
    {
        if (_context is not null)
        {
            ValidateConfiguration();
            OnWork();
        }
    }

    /// <summary>发布状态（step 与直接测试共用）。</summary>
    public void OnWork() => _channel?.PublishStatus();

    private static bool IsConfigurationField(string source) =>
        source is ExternalAccessFieldNames.LocalAddress
            or ExternalAccessFieldNames.LocalPort
            or ExternalAccessFieldNames.MaxConnections;

    /// <summary>字段当前文本：编辑草稿优先，没有草稿才看已提交配置。</summary>
    private string? ReadField(string name)
    {
        if (!_drafts.TryGetValue(name, out var draft))
        {
            return _context?.Values.ReadText(name);
        }

        var committed = _context?.Values.ReadText(name);
        if (string.Equals(draft, committed, StringComparison.Ordinal))
        {
            _ = _drafts.Remove(name);
            return committed;
        }

        return draft;
    }

    /// <summary>配置校验：只由输入/动作/Start/配置变更调用。</summary>
    private void ValidateConfiguration()
    {
        if (_context is not { } context)
        {
            return;
        }

        var addressText = ReadField(ExternalAccessFieldNames.LocalAddress);
        var address = (addressText ?? string.Empty).Trim();
        var addressEmpty = address.Length == 0;
        var addressOk = !addressEmpty && IPAddress.TryParse(address, out _);

        var portText = ReadField(ExternalAccessFieldNames.LocalPort);
        var portDigits = (portText ?? string.Empty).Trim();
        var portOk = int.TryParse(portDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            && port is >= 0 and <= 65535;
        var portEmpty = portDigits.Length == 0;

        var maxText = ReadField(ExternalAccessFieldNames.MaxConnections);
        var maxDigits = (maxText ?? string.Empty).Trim();
        var maxOk = int.TryParse(maxDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)
            && max is >= 1 and <= 256;
        var maxEmpty = maxDigits.Length == 0;

        // 本地地址是下拉：空只表示“还没选”，不把“本机没有可用项”画成用户格式错误。
        _validity.Set(
            context,
            ExternalAccessFieldNames.LocalAddress,
            !addressEmpty && !addressOk,
            addressOk ? null : Tr(TcpServerText.StatusFailedAddressInvalid, address),
            ValueValidityOrigin.Format);
        _validity.Set(
            context,
            ExternalAccessFieldNames.LocalPort,
            portText is not null && !portOk,
            portOk ? null : Tr(TcpServerText.StatusFailedPortOutOfRange, portDigits),
            portEmpty ? ValueValidityOrigin.Module : ValueValidityOrigin.Format);
        _validity.Set(
            context,
            ExternalAccessFieldNames.MaxConnections,
            maxText is not null && !maxOk,
            maxOk ? null : Tr(TcpServerText.StatusFailedMaxConnectionsOutOfRange, maxDigits),
            maxEmpty ? ValueValidityOrigin.Module : ValueValidityOrigin.Format);

        // 未就绪时状态句只说“参数未就绪”，具体原因留在字段悬停里；纯格式错误不写状态。
        var blockedByEmpty = addressEmpty || portEmpty || maxEmpty;
        _channel?.SetOpenReadiness(
            addressOk && portOk && maxOk,
            blockedByEmpty ? Tr(TcpServerText.StatusConfigNotReady) : null);
    }

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

    /// <summary>状态句：监听句 + 本模块自己的 <c>tx</c> 口压力（口侧事实，通道不读）。</summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    private ExternalAccessStatus Describe(ExternalAccessSnapshot snapshot)
    {
        if (snapshot.State == ExternalAccessState.Open
            && _context is not null
            && _context.Ports.TryGetFlowState(TxPort, out var flow))
        {
            // 当前丢弃 episode 由队列维护：发生丢弃置位，成功出队清除。
            // 因此“丢弃 → 积压 → 正常”由当前事实自然给出，不再靠 10 秒历史窗口。
            if (flow.DropStatusActive)
            {
                return new ExternalAccessStatus(Tr(TcpServerText.StatusSendLost), ViewInkRoles.Danger);
            }

            if (flow.BacklogStatusActive)
            {
                return new ExternalAccessStatus(
                    Tr(TcpServerText.StatusTxSaturated, flow.Depth, flow.DroppedWhileSaturatedUnits),
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
                ? Tr(TcpServerText.StatusListening, snapshot.ActiveConnections)
                : Tr(TcpServerText.StatusListeningDetail, endpoint, snapshot.ActiveConnections),
            ExternalAccessState.Opening => endpoint.Length == 0
                ? Tr(TcpServerText.StatusStarting)
                : Tr(TcpServerText.StatusStartingDetail, endpoint),
            ExternalAccessState.Closing => Tr(TcpServerText.StatusStopping),
            ExternalAccessState.Faulted => FailureText(snapshot),
            _ => snapshot.Suspended
                ? Tr(TcpServerText.StatusSuspended)
                : snapshot.RejectedSinceOpen > 0
                    ? Tr(TcpServerText.StatusRejected, snapshot.RejectedSinceOpen)
                    : Tr(TcpServerText.StatusIdle),
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
            ExternalAccessFailureKind.FrameworkInternal => Tr(TcpServerText.StatusFailedFrameworkInternal),
            ExternalAccessFailureKind.ParametersNotRegistered => Tr(TcpServerText.StatusFailedParametersNotRegistered),
            ExternalAccessFailureKind.AddressInvalid => Tr(TcpServerText.StatusFailedAddressInvalid, value),
            ExternalAccessFailureKind.PortOutOfRange => Tr(TcpServerText.StatusFailedPortOutOfRange, value),
            ExternalAccessFailureKind.PortInUse => Tr(TcpServerText.StatusFailedPortInUse, endpoint),
            ExternalAccessFailureKind.ListenFailed => Tr(TcpServerText.StatusFailedListenFailed),
            ExternalAccessFailureKind.AcceptFailed => Tr(TcpServerText.StatusFailedAcceptFailed),
            ExternalAccessFailureKind.OpenFailed => Tr(TcpServerText.StatusFailedOpenFailed),
            ExternalAccessFailureKind.ReadFailed => Tr(TcpServerText.StatusFailedReadFailed),
            ExternalAccessFailureKind.WriteFailed => Tr(TcpServerText.StatusFailedWriteFailed),
            _ => Tr(TcpServerText.StatusFailedUnknown),
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
        ExternalAccessPressure.ReceiveLost => Tr(TcpServerText.StatusReceiveLost, snapshot.ReceiveDroppedSinceOpen),
        ExternalAccessPressure.SendLost => Tr(TcpServerText.StatusSendLost),
        ExternalAccessPressure.SendBacklogged => Tr(TcpServerText.StatusTxBacklog, snapshot.SendBacklogDepth),
        ExternalAccessPressure.ReceiveSaturated => Tr(TcpServerText.StatusRxSaturated, snapshot.ReceiveQueueDepth),
        _ => null,
    };
}
