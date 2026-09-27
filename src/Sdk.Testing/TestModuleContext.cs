using System.Diagnostics.CodeAnalysis;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>
/// **模块级测试上下文**（T6 契约测试套件）：不拉起框架即可驱动一个模块。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>用途：模块作者在自己的测试项目里验证**声明**与**行为**（收到的数据、发出的数据、上报的问题）；</item>
///   <item>它**不是**框架实现：不含线程、队列、绑定与生命周期状态机——这些属框架，由组合级测试覆盖；</item>
///   <item>`Publish` 写入的记录可直接断言（<see cref="Published"/>）。</item>
/// </list>
/// </remarks>
public sealed class TestModuleContext : IModuleContext
{
    private readonly List<PortDeclaration> _ports = [];
    private readonly List<ViewDeclaration> _views = [];
    private readonly List<DataTypeDeclaration> _dataTypes = [];
    private readonly List<FlowDeclaration> _flows = [];
    private readonly List<RoleDeclaration> _roles = [];
    private readonly Dictionary<string, ReceiveHandler> _receiveHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _configuration = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<byte[]>> _published = new(StringComparer.Ordinal);
    private readonly Queue<(byte[] Payload, string PortName, Contracts.ContractLayout Layout)> _inbound = new();

    private readonly TestStopSignal _stopSignal = new();
    private TestPortAccess? _portAccess;

    /// <summary>创建测试上下文。</summary>
    /// <param name="instanceId">实例 ID（默认 <c>test-instance</c>）。</param>
    /// <param name="moduleId">模块标识（默认取自 <see cref="HYWDebugAssistant.Sdk.ModuleId"/> 的测试值）。</param>
    public TestModuleContext(string instanceId = "test-instance", HYWDebugAssistant.Sdk.ModuleId? moduleId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        InstanceId = instanceId;
        ModuleId = moduleId ?? HYWDebugAssistant.Sdk.ModuleId.Create("test", "under-test", "1.0.0");
        Declaration = new DeclarationCollector(_ports, _views, _dataTypes, _flows, _roles);
        Issues = new IssueCollector(ReportedIssues);
        Configuration = new InMemoryConfigurationAccess(_configuration);
        StopSignal = _stopSignal;
    }

    /// <summary>实例 ID。</summary>
    public string InstanceId { get; }

    /// <inheritdoc />
    public HYWDebugAssistant.Sdk.ModuleId ModuleId { get; }

    /// <inheritdoc />
    public IModuleDeclaration Declaration { get; }

    /// <inheritdoc />
    public IPortAccess Ports => _portAccess ??= new TestPortAccess(
        _receiveHandlers,
        _inbound,
        _published,
        Sent);

    /// <inheritdoc />
    public IConfigurationAccess Configuration { get; }

    /// <inheritdoc />
    public IIssueReporter Issues { get; }

    /// <summary>模块上报的问题（测试断言用）。</summary>
    public List<IssueReport> ReportedIssues { get; } = [];

    /// <summary>模块日志（测试断言用）。</summary>
    public List<(ModuleLogLevel Level, string Message)> Logs { get; } = [];

    /// <summary>业务状态上报（测试断言用）。</summary>
    public List<BusinessState> States { get; } = [];

    /// <inheritdoc />
    public IModuleLogger Log => new Logger(Logs);

    /// <inheritdoc />
    public IBusinessStateReporter State => new StateReporter(States);

    /// <inheritdoc />
    public IStopSignal StopSignal { get; }

    /// <inheritdoc />
    public IModuleInbox Inbox { get; } = new TestInbox();

    /// <summary>投递一个命令（测试驱动用；等价于用户在模块窗口里点按钮）。</summary>
    /// <param name="commandName">命令名。</param>
    public void EnqueueCommand(string commandName) => ((TestInbox)Inbox).Commands.Enqueue(commandName);

    /// <summary>投递一个输入值（测试驱动用；等价于用户在输入框里回车）。</summary>
    /// <param name="source">输入源。</param>
    /// <param name="text">文本。</param>
    public void EnqueueInput(string source, string text) => ((TestInbox)Inbox).Inputs.Enqueue((source, text));

    /// <summary>测试用收件箱：无界（测试里手动投递，不需要丢旧策略）。</summary>
    private sealed class TestInbox : IModuleInbox
    {
        public Queue<string> Commands { get; } = new();

        public Queue<(string Source, string Text)> Inputs { get; } = new();

        public bool TryDequeueCommand([NotNullWhen(true)] out string? commandName)
        {
            if (Commands.Count == 0)
            {
                commandName = null;
                return false;
            }

            commandName = Commands.Dequeue();
            return true;
        }

        public bool TryDequeueInput([NotNullWhen(true)] out string? source, [NotNullWhen(true)] out string? text)
        {
            if (Inputs.Count == 0)
            {
                source = null;
                text = null;
                return false;
            }

            var (s, t) = Inputs.Dequeue();
            source = s;
            text = t;
            return true;
        }
    }

    /// <summary>已声明的口（按声明顺序；测试断言用）。</summary>
    public IReadOnlyList<PortDeclaration> DeclaredPorts => _ports;

    /// <summary>已声明的**数据类型**（按声明顺序；测试断言用）【ADR-0046 D2】。</summary>
    public IReadOnlyList<DataTypeDeclaration> DeclaredDataTypes => _dataTypes;

    /// <summary>已声明的**内部处理关系**（按声明顺序；测试断言用）【ADR-0052】。</summary>
    public IReadOnlyList<FlowDeclaration> DeclaredFlows => _flows;

    /// <summary>已声明的**角色**（功能分块，按声明顺序；测试断言用）【ADR-0052】。</summary>
    public IReadOnlyList<RoleDeclaration> DeclaredRoles => _roles;

    /// <summary>已声明的视图（首个；测试断言用）。</summary>
    public ViewDeclaration? DeclaredView => _views.Count > 0 ? _views[0] : null;

    /// <summary>模块经发送口发出的数据单元（口名 → 字节序列）。</summary>
    public List<(string PortName, byte[] Payload)> Sent { get; } = [];

    /// <summary>模块写入订阅缓冲的样本（数据源名 → 样本序列）。</summary>
    public IReadOnlyDictionary<string, List<byte[]>> Published => _published;

    /// <summary>按当前配置写入一份配置（测试预置用）。</summary>
    public void SetConfiguration<T>(T configuration)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration[typeof(T).FullName!] = configuration;
    }

    /// <summary>模拟一次投递（被动接收模式下由框架调用；主动接收模式由模块自行取走）。</summary>
    /// <param name="portName">目标口名。</param>
    /// <param name="payload">数据单元字节。</param>
    /// <param name="layout">
    /// 数据单元的契约布局（省略时按"定长字节块"推断——**仅当单元 ≤ 256 字节**；更大的单元请显式传入）。
    /// </param>
    public void Deliver(string portName, ReadOnlySpan<byte> payload, Contracts.ContractLayout? layout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        var copy = payload.ToArray();
        var unitLayout = layout
            ?? Contracts.ContractLayout.FromShape(
                Contracts.ContractShape.OfBytes(Math.Clamp(copy.Length, 1, Contracts.BytesShape.MaxLength)));

        if (_receiveHandlers.TryGetValue(portName, out var handler))
        {
            handler(copy);
            return;
        }

        _inbound.Enqueue((copy, portName, unitLayout));
    }

    /// <summary>请求停止（模块的活动体应尽快返回）。</summary>
    public void RequestStop() => _stopSignal.Request();

    private sealed class DeclarationCollector(
        List<PortDeclaration> ports,
        List<ViewDeclaration> views,
        List<DataTypeDeclaration> types,
        List<FlowDeclaration> flows,
        List<RoleDeclaration> roles) : IModuleDeclaration
    {
        public void DeclareDataType(DataTypeDeclaration type) => types.Add(type);

        public void DeclarePort(PortDeclaration port) => ports.Add(port);

        public void DeclareRole(RoleDeclaration role) => roles.Add(role);

        public void DeclareFlow(FlowDeclaration flow) => flows.Add(flow);

        public void DeclareView(ViewDeclaration view) => views.Add(view);
    }

    private sealed class IssueCollector(List<IssueReport> sink) : IIssueReporter
    {
        public void Report(IssueReport issue) => sink.Add(issue);
    }

    private sealed class Logger(List<(ModuleLogLevel Level, string Message)> sink) : IModuleLogger
    {
        public bool IsEnabled(ModuleLogLevel level) => true;

        public void Log(ModuleLogLevel level, string message) => sink.Add((level, message));
    }

    private sealed class StateReporter(List<BusinessState> sink) : IBusinessStateReporter
    {
        public void Update(BusinessState state) => sink.Add(state);
    }

    private sealed class TestStopSignal : IStopSignal
    {
        private volatile bool _requested;

        public bool IsStopRequested => _requested;

        public bool Wait(TimeSpan timeout)
        {
            if (_requested)
            {
                return true;
            }

            Thread.Sleep(timeout);
            return _requested;
        }

        public void Request() => _requested = true;
    }

    private sealed class InMemoryConfigurationAccess(Dictionary<string, object> store) : IConfigurationAccess
    {
        public T Read<T>()
            where T : class, new() =>
            store.TryGetValue(typeof(T).FullName!, out var value) ? (T)value : new T();

        public void Write<T>(T configuration)
            where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(configuration);
            store[typeof(T).FullName!] = configuration;
        }
    }

    private sealed class TestPortAccess(
        Dictionary<string, ReceiveHandler> handlers,
        Queue<(byte[] Payload, string PortName, Contracts.ContractLayout Layout)> inbound,
        Dictionary<string, List<byte[]>> published,
        List<(string PortName, byte[] Payload)> sent) : IPortAccess
    {
        public bool TrySend(string portName, ReadOnlySpan<byte> payload)
        {
            sent.Add((portName, payload.ToArray()));
            return true;
        }

        public void SetReceiveHandler(string portName, ReceiveHandler handler) => handlers[portName] = handler;

        public bool TryReceive(
            [NotNullWhen(true)] out PayloadBuffer? buffer,
            [NotNullWhen(true)] out string? sourcePort)
        {
            if (inbound.Count > 0)
            {
                var (payload, portName, layout) = inbound.Dequeue();
                buffer = new TestPayloadBuffer(payload, layout);
                sourcePort = portName;
                return true;
            }

            buffer = null;
            sourcePort = null;
            return false;
        }

        public void ReleaseBuffer(PayloadBuffer buffer)
        {
            // 测试缓冲无池可归还
        }

        public void Publish(string dataSourceName, ReadOnlySpan<byte> value)
        {
            if (!published.TryGetValue(dataSourceName, out var list))
            {
                list = [];
                published[dataSourceName] = list;
            }

            list.Add(value.ToArray());
        }
    }

    /// <summary>测试用数据单元缓冲（自带布局）。</summary>
    private sealed class TestPayloadBuffer : PayloadBuffer
    {
        private readonly byte[] _payload;

        public TestPayloadBuffer(byte[] payload, Contracts.ContractLayout layout)
        {
            _payload = payload;
            Layout = layout;
        }

        public override int Length => _payload.Length;

        public override Contracts.ContractLayout Layout { get; }

        public override ReadOnlySpan<byte> Span => _payload;
    }
}
