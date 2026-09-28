using System.Diagnostics.CodeAnalysis;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Hosting.Context;

/// <summary>
/// 模块上下文实现：模块**唯一**的框架访问面（M1-4 最小可用版）。
/// </summary>
/// <remarks>
/// 各服务面的成熟度（随 M1 步骤推进，逐项替换为真实实现）：
/// <list type="bullet">
///   <item><b>声明</b>（口/视图）：✅ 本步完整实现；</item>
///   <item><b>Issue 上报</b>：✅ 转发到 <see cref="IIssueSink"/> 并归属本实例；</item>
///   <item><b>停止信号</b>：✅ 简单标志（M1-5 由数据面的停止协议驱动）；</item>
///   <item><b>投递面</b>（发送/接收/订阅写入）：⏳ 本步占位（接收处理会记录，供 M1-5 投递调用）；</item>
///   <item><b>日志 / 业务状态</b>：⏳ 本步丢弃（**M1-6 接入 Issue 中心与 L3 日志汇集**，二者语义不同、不混用）；</item>
///   <item><b>配置读写</b>：✅ 内存实现 + **组合件快照/恢复**（M1-8：保存时按类型快照、加载时在 <c>OnActivated</c> 前恢复）。</item>
/// </list>
/// </remarks>
public sealed class ModuleContext : IModuleContext
{
    private readonly List<PortDeclaration> _ports = [];
    private readonly List<ViewDeclaration> _views = [];
    private readonly List<DataTypeDeclaration> _dataTypes = [];
    private readonly List<FlowDeclaration> _flows = [];
    private readonly List<RoleDeclaration> _roles = [];
    private readonly Dictionary<string, ReceiveHandler> _receiveHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _configuration = new(StringComparer.Ordinal);
    private readonly Lock _configurationGate = new();
    private readonly string _objectPath;

    /// <summary>创建上下文。</summary>
    /// <param name="instanceId">实例 ID（上报归属用）。</param>
    /// <param name="moduleId">模块标识。</param>
    /// <param name="issues">框架侧上报口。</param>
    public ModuleContext(string instanceId, ModuleId moduleId, IIssueSink issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(issues);

        _objectPath = $"module:{instanceId}";
        InstanceId = instanceId;
        ModuleId = moduleId;

        Declaration = new DeclarationCollector(_ports, _views, _dataTypes, _flows, _roles);
        Issues = new IssueReporter(issues, _objectPath);
        Log = new NullModuleLogger();
        State = new NullBusinessStateReporter();
        StopSignal = new SimpleStopSignal();
        Ports = new PlaceholderPortAccess(_receiveHandlers);
        Configuration = new InMemoryConfigurationAccess(_configuration, _configurationGate);
        Inbox = new BoundedInbox();
        InputFeedback = InputFeedbackStore;
    }

    /// <summary>
    /// **输入处理结果的存放处**（G3）——L4 经此读快照、并在**投递入队时**清除旧结果（"下一次提交即刷新"）。
    /// </summary>
    public InputFeedbackStore InputFeedbackStore { get; } = new();

    /// <inheritdoc />
    public IInputFeedbackReporter InputFeedback { get; }

    /// <summary>实例 ID。</summary>
    public string InstanceId { get; }

    /// <inheritdoc />
    public ModuleId ModuleId { get; }

    /// <inheritdoc />
    public IModuleDeclaration Declaration { get; }

    /// <inheritdoc />
    public IPortAccess Ports { get; private set; }

    /// <inheritdoc />
    public IConfigurationAccess Configuration { get; }

    /// <inheritdoc />
    public IIssueReporter Issues { get; }

    /// <inheritdoc />
    public IModuleLogger Log { get; }

    /// <inheritdoc />
    public IBusinessStateReporter State { get; }

    /// <inheritdoc />
    public IStopSignal StopSignal { get; }

    /// <inheritdoc />
    public IModuleInbox Inbox { get; }

    /// <summary>已声明的口（按声明顺序）。</summary>
    public IReadOnlyList<PortDeclaration> DeclaredPorts => _ports;

    /// <summary>已声明的**数据类型**（按声明顺序）【ADR-0046 D2】。</summary>
    public IReadOnlyList<DataTypeDeclaration> DeclaredDataTypes => _dataTypes;

    /// <summary>已声明的**内部处理关系**（按声明顺序）【ADR-0052】——合法性由工厂在成员实例化后统一校验。</summary>
    public IReadOnlyList<FlowDeclaration> DeclaredFlows => _flows;

    /// <summary>已声明的**角色**（按声明顺序）【ADR-0052】——合法性由工厂在成员实例化后统一校验。</summary>
    public IReadOnlyList<RoleDeclaration> DeclaredRoles => _roles;

    /// <summary>已声明的视图（取首个）。</summary>
    public ViewDeclaration? DeclaredView => _views.Count > 0 ? _views[0] : null;

    /// <summary>
    /// 替换投递面实现（**由 L4 运行时在承载启动前调用**；模块作者不使用本方法）。
    /// </summary>
    /// <remarks>
    /// M1-4 的占位投递面（<c>PlaceholderPortAccess</c>）在这里被真实实现替换：
    /// 发送走绑定投递、接收从承载队列取、订阅写入订阅缓冲。
    /// </remarks>
    public void AttachDataPlane(IPortAccess portAccess)
    {
        ArgumentNullException.ThrowIfNull(portAccess);
        Ports = portAccess;
    }

    /// <summary>
    /// 注册接收处理（**框架内部**：L4 运行时的投递面把模块的注册转到这里）。
    /// </summary>
    /// <param name="portName">接收口名。</param>
    /// <param name="handler">处理委托。</param>
    public void RegisterReceiveHandler(string portName, ReceiveHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(handler);

        _receiveHandlers[portName] = handler;
    }

    /// <summary>已注册的接收处理（供数据面在投递时调用——M1-5）。</summary>
    public IReadOnlyDictionary<string, ReceiveHandler> ReceiveHandlers => _receiveHandlers;

    /// <summary>
    /// 配置快照（**框架内部**：组合件保存时按类型快照写入文件；模块作者不使用）。
    /// </summary>
    /// <remarks>
    /// 键 = 配置类型的完整名；值为当前配置对象。加锁复制一份：该字典可能被**模块线程**
    /// （<c>Read</c> 首次登记默认配置）与**管理线程**（保存/恢复）同时访问。
    /// </remarks>
    public IReadOnlyDictionary<string, object> ConfigurationSnapshot
    {
        get
        {
            lock (_configurationGate)
            {
                return new Dictionary<string, object>(_configuration, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// 恢复一条配置（**框架内部**：组合件加载时在 <c>OnActivated</c> **之前**写入；模块作者不使用）。
    /// </summary>
    /// <param name="typeFullName">配置类型完整名。</param>
    /// <param name="configuration">已反序列化的配置对象。</param>
    public void RestoreConfiguration(string typeFullName, object configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeFullName);
        ArgumentNullException.ThrowIfNull(configuration);

        lock (_configurationGate)
        {
            _configuration[typeFullName] = configuration;
        }
    }

    /// <summary>
    /// **把该实例已保存的配置重置为默认值**（框架内部：模块「还原」时调用）。
    /// </summary>
    /// <remarks>
    /// 实现要点：**不是清空**，而是把每条配置替换为**同类型的新实例**（默认值）。
    /// 清空会让"配置 Schema 表单"失去字段来源（表单按登记对象生成）——用户会看到空表单；
    /// 替换新实例则同时满足：① 模块下次读取即拿到默认值；② 表单仍可生成并显示默认值。
    /// 反射只作用于**模块自有配置类型**（沿用 `ConfigurationJson` 的既有取舍：组合件信封零反射、模块配置反射式）。
    /// </remarks>
    /// <returns>是否确实重置了内容（false = 原本没有配置）。</returns>
    public bool ResetConfiguration()
    {
        lock (_configurationGate)
        {
            if (_configuration.Count == 0)
            {
                return false;
            }

            foreach (var key in _configuration.Keys.ToList())
            {
                if (Activator.CreateInstance(_configuration[key].GetType()) is { } defaults)
                {
                    _configuration[key] = defaults;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 请求停止本实例的模块活动（由数据面的停止协议调用，M1-5）。
    /// </summary>
    /// <remarks>模块侧通过 <see cref="StopSignal"/> 只读查询，不能自行置位。</remarks>
    public void RequestStop()
    {
        if (StopSignal is SimpleStopSignal signal)
        {
            signal.Request();
        }
    }

    /// <inheritdoc />
    public override string ToString() => _objectPath;

    // ---------------------------------------------------------------- 内部实现

    private sealed class DeclarationCollector(
        List<PortDeclaration> ports,
        List<ViewDeclaration> views,
        List<DataTypeDeclaration> types,
        List<FlowDeclaration> flows,
        List<RoleDeclaration> roles) : IModuleDeclaration
    {
        public void DeclareDataType(DataTypeDeclaration type)
        {
            ArgumentNullException.ThrowIfNull(type);
            types.Add(type);
        }

        public void DeclarePort(PortDeclaration port)
        {
            ArgumentNullException.ThrowIfNull(port);
            ports.Add(port);
        }

        public void DeclareRole(RoleDeclaration role)
        {
            ArgumentNullException.ThrowIfNull(role);
            roles.Add(role);
        }

        public void DeclareFlow(FlowDeclaration flow)
        {
            ArgumentNullException.ThrowIfNull(flow);
            flows.Add(flow);
        }

        public void DeclareView(ViewDeclaration view)
        {
            ArgumentNullException.ThrowIfNull(view);
            views.Add(view);
        }
    }

    private sealed class IssueReporter(IIssueSink sink, string objectPath) : IIssueReporter
    {
        // 实现不抛出（上报失败不应让模块崩）
        public void Report(IssueReport issue)
        {
            if (issue is null || string.IsNullOrWhiteSpace(issue.Message))
            {
                return;
            }

            // 来源由**框架**在模块上报路径上补齐（模块不需要、也不应该自己声明来源）
            var report = issue.Source == IssueSource.Module ? issue : issue with { Source = IssueSource.Module };

            sink.Report(report.ObjectPath is null ? objectPath : $"{objectPath}/{report.ObjectPath}", report);
        }
    }

    /// <summary>日志面占位：M1-6 接入 L3 日志汇集（**不并入 Issue 通道**——§8.5）。</summary>
    private sealed class NullModuleLogger : IModuleLogger
    {
        public bool IsEnabled(ModuleLogLevel level) => false;

        public void Log(ModuleLogLevel level, string message)
        {
            // M1-6 接入日志汇集
        }
    }

    /// <summary>业务状态面占位：M1-6 接入状态通道（**业务状态不入 Issue**——§8.5）。</summary>
    private sealed class NullBusinessStateReporter : IBusinessStateReporter
    {
        public void Update(BusinessState state)
        {
            // M1-6 接入状态汇集
        }
    }

    /// <summary>
    /// **人机操作收件箱**实现【M1-16 / ADR-0040】：有界（<see cref="Capacity"/>），满时**丢最旧**并计数。
    /// </summary>
    /// <remarks>
    /// 并发：投递方是**管理线程**（变更提交），消费方是**模块线程**（活动体）→ 用锁保护；
    /// 容量有限保证"用户连点"不会把内存撑爆，也不会阻塞 UI。
    /// </remarks>
    private sealed class BoundedInbox : IModuleInbox
    {
        private readonly Queue<string> _commands = new();
        private readonly Queue<(string Source, string Text)> _inputs = new();
        private readonly Lock _gate = new();

        public int DroppedCount { get; private set; }

        public void EnqueueCommand(string commandName)
        {
            lock (_gate)
            {
                if (_commands.Count >= InboxCapacity)
                {
                    _ = _commands.Dequeue();
                    DroppedCount++;
                }

                _commands.Enqueue(commandName);
            }
        }

        public void EnqueueInput(string source, string text)
        {
            lock (_gate)
            {
                if (_inputs.Count >= InboxCapacity)
                {
                    _ = _inputs.Dequeue();
                    DroppedCount++;
                }

                _inputs.Enqueue((source, text));
            }
        }

        public bool TryDequeueCommand([NotNullWhen(true)] out string? commandName)
        {
            lock (_gate)
            {
                if (_commands.Count == 0)
                {
                    commandName = null;
                    return false;
                }

                commandName = _commands.Dequeue();
                return true;
            }
        }

        public bool TryDequeueInput([NotNullWhen(true)] out string? source, [NotNullWhen(true)] out string? text)
        {
            lock (_gate)
            {
                if (_inputs.Count == 0)
                {
                    source = null;
                    text = null;
                    return false;
                }

                var (s, t) = _inputs.Dequeue();
                source = s;
                text = t;
                return true;
            }
        }
    }

    /// <summary>收件箱容量（命令与输入各自计数）。</summary>
    public const int InboxCapacity = 64;

    /// <summary>投递一个命令（**框架内部**：变更提交时调用；模块作者不使用）。</summary>
    /// <param name="commandName">命令名。</param>
    public void EnqueueCommand(string commandName) => ((BoundedInbox)Inbox).EnqueueCommand(commandName);

    /// <summary>投递一个输入值（**框架内部**）。</summary>
    /// <param name="source">输入元素绑定的数据源名。</param>
    /// <param name="text">用户输入文本。</param>
    public void EnqueueInput(string source, string text) => ((BoundedInbox)Inbox).EnqueueInput(source, text);

    private sealed class SimpleStopSignal : IStopSignal    {
        private volatile bool _stopRequested;

        public bool IsStopRequested => _stopRequested;

        public bool Wait(TimeSpan timeout)
        {
            if (_stopRequested)
            {
                return true;
            }

            Thread.Sleep(timeout);
            return _stopRequested;
        }

        /// <summary>请求停止（由数据面的停止协议调用——M1-5）。</summary>
        public void Request() => _stopRequested = true;
    }

    /// <summary>投递面占位：M1-5 由数据面替换为真实队列与绑定投递。</summary>
    private sealed class PlaceholderPortAccess(Dictionary<string, ReceiveHandler> handlers) : IPortAccess
    {
        public bool TrySend(string portName, ReadOnlySpan<byte> payload) => false;

        public void SetReceiveHandler(string portName, ReceiveHandler handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(portName);
            ArgumentNullException.ThrowIfNull(handler);
            handlers[portName] = handler;
        }

        public bool TryReceive(
            [NotNullWhen(true)] out Sdk.Data.PayloadBuffer? buffer,
            [NotNullWhen(true)] out string? sourcePort)
        {
            buffer = null;
            sourcePort = null;
            return false;
        }

        public void ReleaseBuffer(Sdk.Data.PayloadBuffer buffer)
        {
            // 占位实现：无池可归还（M1-5 数据面接管）
        }

        public void Publish(string dataSourceName, ReadOnlySpan<byte> value)
        {
            // M1-9 接入订阅缓冲
        }
    }

    /// <summary>
    /// 配置面内存实现：读时**登记默认配置**（框架据此持久化并生成表单），写在锁内进行。
    /// </summary>
    /// <remarks>
    /// "读即登记"是必要的：模块通常只读不改，若不登记，保存组合件时该实例的配置会**整块丢失**、
    /// 配置表单也无字段可编辑（配置是"随组合件保存的实例参数"——主文档 §9.2）。
    /// </remarks>
    private sealed class InMemoryConfigurationAccess(Dictionary<string, object> store, Lock gate) : IConfigurationAccess
    {
        public T Read<T>()
            where T : class, new()
        {
            var key = typeof(T).FullName!;

            lock (gate)
            {
                if (store.TryGetValue(key, out var value) && value is T existing)
                {
                    return existing;
                }

                var created = new T();
                store[key] = created;
                return created;
            }
        }

        public void Write<T>(T configuration)
            where T : class, new()
        {
            ArgumentNullException.ThrowIfNull(configuration);

            lock (gate)
            {
                store[typeof(T).FullName!] = configuration;
            }
        }
    }
}
