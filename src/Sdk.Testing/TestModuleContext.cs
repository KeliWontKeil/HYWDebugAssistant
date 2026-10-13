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
    private readonly List<DataTypeDeclaration> _dataTypes = [];
    private readonly List<FlowDeclaration> _flows = [];
    private readonly List<RoleDeclaration> _roles = [];
    private readonly List<AppearanceReport> _appearances = [];
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
        Declaration = new DeclarationCollector(_ports, _dataTypes, _flows, _roles);
        Issues = new IssueCollector(ReportedIssues);
        Configuration = new InMemoryConfigurationAccess(_configuration);
        StopSignal = _stopSignal;
        Appearance = new AppearanceRecorder(_appearances);
    }

    /// <summary>
    /// 模块给元素设定的**外观**（颜色）——只记颜色，**不含语义**（框架不认识"红 = 失败"）。
    /// </summary>
    /// <remarks>模块作者可在自己的测试里断言"这一步确实把边框设成了某个颜色"。</remarks>
    public IReadOnlyList<AppearanceReport> AppearanceReports => _appearances;

    /// <inheritdoc />
    public IViewAppearanceAccess Appearance { get; }

    /// <summary>一次外观设定的**记录**（测试断言用）。</summary>
    /// <param name="SourceName">数据源名。</param>
    /// <param name="Outline">设的边框色；<see langword="null"/> = 清除。</param>
    /// <param name="Text">设的文字色；<see langword="null"/> = 未动这一键位。</param>
    /// <param name="TextSize">设的字号；<see langword="null"/> = 未动这一键位。</param>
    /// <param name="Reset">是不是一次**动作复原**要求（勾选框拨回）。</param>
    /// <param name="ReadOnly">设的运行期只读；<see langword="null"/> = 未动这一键位。</param>
    /// <param name="OutlineRole">设的边框**角色词**（相对色）；<see langword="null"/> = 未动这一键位。</param>
    /// <param name="TextRole">设的文字**角色词**（相对色）；<see langword="null"/> = 未动这一键位。</param>
    public readonly record struct AppearanceReport(
        string SourceName,
        uint? Outline,
        uint? Text,
        double? TextSize = null,
        bool Reset = false,
        bool? ReadOnly = null,
        string? OutlineRole = null,
        string? TextRole = null);

    /// <summary>把外观设定记下来的替身。</summary>
    private sealed class AppearanceRecorder : IViewAppearanceAccess
    {
        private readonly List<AppearanceReport> _log;

        public AppearanceRecorder(List<AppearanceReport> log) => _log = log;

        /// <inheritdoc />
        public void SetOutlineColor(string sourceName, uint? argb) =>
            _log.Add(new AppearanceReport(sourceName, argb, Text: null));

        /// <inheritdoc />
        public void SetOutlineRole(string sourceName, string? role) =>
            _log.Add(new AppearanceReport(sourceName, Outline: null, Text: null, OutlineRole: role));

        /// <inheritdoc />
        public void SetTextColor(string sourceName, uint? argb) =>
            _log.Add(new AppearanceReport(sourceName, Outline: null, argb));

        /// <inheritdoc />
        public void SetTextRole(string sourceName, string? role) =>
            _log.Add(new AppearanceReport(sourceName, Outline: null, Text: null, TextRole: role));

        /// <inheritdoc />
        public void SetTextSize(string sourceName, double? dips) =>
            _log.Add(new AppearanceReport(sourceName, Outline: null, Text: null, dips));

        /// <inheritdoc />
        public void SetReadOnly(string sourceName, bool isReadOnly) =>
            _log.Add(new AppearanceReport(sourceName, Outline: null, Text: null, ReadOnly: isReadOnly));

        /// <inheritdoc />
        public void ResetInteraction(string sourceName) =>
            _log.Add(new AppearanceReport(sourceName, Outline: null, Text: null, TextSize: null, Reset: true));
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

    /// <summary>
    /// **视图状态回读**（测试可预置）：模块读到的就是这里预置的值
    /// （框架里由 UI 写，测试里由用例写 —— 语义同一份）。
    /// </summary>
    public TestViewState ViewState { get; } = new();

    /// <inheritdoc />
    IViewStateReader IModuleContext.ViewState => ViewState;

    /// <summary>
    /// **显示内容的请求面**（测试可断言）：模块请求"清空某个显示源"时记在这里。
    /// </summary>
    /// <remarks>
    /// 框架里这条请求进 L4 的挂起表、由 UI 在刷新节拍里执行；测试里**直接落到 <see cref="ViewState"/>**
    /// —— 于是模块"追加/清空后再回读"（保存）这条路在测试里也走得通，与框架侧语义一致。
    /// </remarks>
    public List<string> ClearedDisplays { get; } = [];

    /// <summary>
    /// **整段替换过哪些显示源**（测试可断言）：模块调 <c>Display.SetText</c> 时记在这个名字下。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ClearedDisplays"/> 同一模型：测试里**直接落到 <see cref="ViewState"/>**。
    /// 与追加分开记的理由：替换是"只有当前值"的语义（消息 / 状态），断言"界面上现在显示的是哪一句"
    /// 应当查这一份，而不是去翻追加过的行。
    /// </remarks>
    public List<(string SourceName, string Text)> SetTexts { get; } = [];

    /// <inheritdoc />
    IViewDisplayAccess IModuleContext.Display => DisplayAccess;

    private DisplayRecorder? _display;

    private DisplayRecorder DisplayAccess => _display ??= new DisplayRecorder(this);

    /// <summary>
    /// **测试里的文件选择请求面**：请求只被记录，**不弹任何对话框**
    /// （与框架侧"请求进挂起表、由 UI 执行"同一个形状 —— 测试不碰真实系统对话）。
    /// </summary>
    /// <remarks>
    /// 想模拟"用户选定了一个路径"时，测试直接
    /// <c>EnqueueInput(request.SourceName, 路径)</c> —— 那正是框架在真实宿主里做的事
    /// （选定后按 <see cref="FilePickRequest.SourceName"/> 投回输入通道）。
    /// </remarks>
    public List<FilePickRequest> FilePickRequests { get; } = [];

    /// <inheritdoc />
    IFileAccess IModuleContext.Files => FileAccess;

    private FileAccessRecorder? _files;

    private FileAccessRecorder FileAccess => _files ??= new FileAccessRecorder(this);

    /// <summary>投递一个命令（测试驱动用；等价于用户在模块窗口里点按钮）。</summary>
    /// <param name="commandName">命令名。</param>
    public void EnqueueCommand(string commandName) => ((TestInbox)Inbox).Commands.Enqueue(commandName);

    /// <summary>投递一个输入值（测试驱动用；等价于用户在输入框里回车）。</summary>
    /// <param name="source">输入源。</param>
    /// <param name="text">文本。</param>
    /// <remarks>
    /// <b>同时写进视图状态</b>：真实链路里用户一改字，UI 先把值写进视图状态、再把这次改动投给模块
    /// （模块"所见即所发"读的就是那一份）。只入队不写状态，等于造了一个真实宿主不存在的形状 ——
    /// 模块会读不到用户在界面上拨到的位置。
    /// </remarks>
    public void EnqueueInput(string source, string text)
    {
        ViewState.SetInputText(source, text);
        ((TestInbox)Inbox).Inputs.Enqueue((source, text));
    }

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

    /// <summary>已声明的**数据类型**（按声明顺序；测试断言用）。</summary>
    public IReadOnlyList<DataTypeDeclaration> DeclaredDataTypes => _dataTypes;

    /// <summary>已声明的**内部处理关系**（按声明顺序；测试断言用）。</summary>
    public IReadOnlyList<FlowDeclaration> DeclaredFlows => _flows;

    /// <summary>已声明的**角色**（功能分块，按声明顺序；测试断言用）。</summary>
    public IReadOnlyList<RoleDeclaration> DeclaredRoles => _roles;

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
        List<DataTypeDeclaration> types,
        List<FlowDeclaration> flows,
        List<RoleDeclaration> roles) : IModuleDeclaration
    {
        public void DeclareDataType(DataTypeDeclaration type) => types.Add(type);

        public void DeclarePort(PortDeclaration port) => ports.Add(port);

        public void DeclareRole(RoleDeclaration role) => roles.Add(role);

        public void DeclareFlow(FlowDeclaration flow) => flows.Add(flow);
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
    /// <summary>
    /// 测试替身：把"显示请求"记下来，并**落到 <see cref="TestViewState"/>**（模块回读到的就是它）。
    /// </summary>
    /// <remarks>
    /// 与框架侧同一模型：**只入队、由"UI"落地** —— 测试里"UI"就是用例本身（它读 <see cref="TestViewState"/>）。
    /// 追加的拼装规则与框架侧逐字一致（不换行 = 直接接在后面；通道换行 = 起一条新的）。
    /// </remarks>
    private sealed class DisplayRecorder(TestModuleContext owner) : IViewDisplayAccess
    {
        public void Clear(string dataSourceName)
        {
            if (string.IsNullOrWhiteSpace(dataSourceName))
            {
                return;
            }

            owner.ClearedDisplays.Add(dataSourceName);
            owner.ViewState.ClearDisplay(dataSourceName);
        }

        public void SetText(string dataSourceName, string text)
        {
            if (string.IsNullOrWhiteSpace(dataSourceName))
            {
                return;
            }

            // 与框架侧同一语义：**替换**（不是追加）⇒ 记下"这一源被整段设过"并落到视图状态。
            owner.SetTexts.Add((dataSourceName, text));
            owner.ViewState.SetDisplay(dataSourceName, text);
        }

        public void AppendText(string dataSourceName, string text)
        {
            if (string.IsNullOrWhiteSpace(dataSourceName))
            {
                return;
            }

            owner.ViewState.AppendDisplay(dataSourceName, text, lineBreak: false);
        }

        public void AppendLine(string dataSourceName, string text)
        {
            if (string.IsNullOrWhiteSpace(dataSourceName))
            {
                return;
            }

            owner.ViewState.AppendDisplay(dataSourceName, text, lineBreak: true);
        }
    }

    /// <summary>
    /// 测试替身：把"文件选择请求"记下来，**不弹任何对话框**。
    /// </summary>
    /// <remarks>
    /// 与框架侧同一模型：模块只"请求"，执行是宿主的事。测试要模拟"用户选了某个路径"时，
    /// 直接 <c>EnqueueInput(request.SourceName, 路径)</c> —— 与真实宿主投回输入通道是同一件事。
    /// </remarks>
    private sealed class FileAccessRecorder(TestModuleContext owner) : IFileAccess
    {
        public void Request(FilePickRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.SourceName))
            {
                return;
            }

            owner.FilePickRequests.Add(request);
        }
    }
}
