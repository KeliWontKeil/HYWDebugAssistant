using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Timing;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Values;
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
///   <item><b>投递面</b>（发送/接收/订阅写入）：⏳ 本步占位（接收处理会记录，供 M1-5 投递调用）；</item>
///   <item><b>日志 / 业务状态</b>：⏳ 本步丢弃（**M1-6 接入 Issue 中心与 L3 日志汇集**，二者语义不同、不混用）；</item>
///   <item><b>配置读写</b>：✅ 内存实现 + **组合件快照/恢复**（M1-8：保存时按类型快照、加载时在 <c>OnActivated</c> 前恢复）。</item>
/// </list>
/// </remarks>
public sealed class ModuleContext : IModuleContext, IDisposable
{
    private readonly List<PortDeclaration> _ports = [];
    private readonly List<DataTypeDeclaration> _dataTypes = [];
    private readonly List<FlowDeclaration> _flows = [];
    private readonly List<RoleDeclaration> _roles = [];
    private readonly List<DisplaySourceDeclaration> _displaySources = [];
    private readonly List<string> _inputs = [];
    private readonly HashSet<string> _persistentInputs = new(StringComparer.Ordinal);
    private readonly List<ValueDeclaration> _values = [];
    private readonly Dictionary<string, ValueValidity> _valueValidities = new(StringComparer.Ordinal);
    private readonly Lock _valueValidityGate = new();
    private readonly List<(string Name, string Title)> _commands = [];
    private readonly List<ExternalAccessDeclaration> _externalAccesses = [];
    private readonly Dictionary<string, ReceiveHandler> _receiveHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _configuration = new(StringComparer.Ordinal);
    private readonly Lock _configurationGate = new();
    private long _configurationVersion;
    private readonly ModuleTimerSet _timers = new();
    private readonly string _objectPath;

    /// <summary>创建上下文。</summary>
    /// <param name="instanceId">实例 ID（上报归属用）。</param>
    /// <param name="moduleId">模块标识。</param>
    /// <param name="issues">框架侧上报口。</param>
    public ModuleContext(
        string instanceId,
        ModuleId moduleId,
        IIssueSink issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(issues);

        _objectPath = $"module:{instanceId}";
        InstanceId = instanceId;
        ModuleId = moduleId;

        Declaration = new DeclarationCollector(
            _ports, _dataTypes, _flows, _roles, _displaySources, _inputs, _persistentInputs, _values, _commands,
            _externalAccesses);
        Issues = new IssueReporter(issues, _objectPath);
        Log = new NullModuleLogger();
        State = new NullBusinessStateReporter();
        Ports = new PlaceholderPortAccess(_receiveHandlers);
        Configuration = new InMemoryConfigurationAccess(
            _configuration,
            _configurationGate,
            () => Interlocked.Increment(ref _configurationVersion));
        Inbox = new BoundedInbox();
        Values = new ModuleValueAccess(this);
        Appearance = AppearanceStore;
    }

    /// <summary>
    /// **元素外观的存放处** —— L4 经此读快照交给界面。
    /// </summary>
    /// <remarks>
    /// 它只存"什么颜色"，**不含任何语义**：模块给红给绿都行，框架不解释（见 <see cref="IViewAppearanceAccess"/>）。
    /// </remarks>
    public AppearanceStore AppearanceStore { get; } = new();

    /// <inheritdoc />
    public IViewAppearanceAccess Appearance { get; }

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
    public IModuleInbox Inbox { get; }

    /// <inheritdoc />
    public IModuleTimers Timers => _timers;

    /// <summary>
    /// **定时器集合的框架侧句柄**（承载者挂接、暂停/恢复与移除清理用；模块作者只使用 <see cref="Timers"/>）。
    /// </summary>
    public ModuleTimerSet TimerSet => _timers;

    /// <summary>释放本上下文持有的定时器集合（实例移除时由运行时调用；幂等）。</summary>
    public void Dispose() => _timers.Dispose();

    /// <summary>
    /// **视图状态回读**（只读快照）——由 L4 在创建实例时注入
    /// （按实例绑定的只读适配器）；未注入时回落到"全部未知"的空实现（**不抛异常**）。
    /// </summary>
    public IViewStateReader ViewState { get; private set; } = EmptyViewState.Instance;

    /// <summary>
    /// 挂上视图状态读取面（**由 L4 在创建实例时调用**；模块作者不使用本方法）。
    /// </summary>
    /// <param name="viewState">按实例绑定的只读读取面。</param>
    public void AttachViewState(IViewStateReader viewState)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ViewState = viewState;
    }

    /// <summary>
    /// **显示内容的请求面**（模块 → 框架的"请求"方向）：目前只有"清空某个显示源"
    /// 〔为 `hyw/data-monitor` 的「清空」而只增〕。未注入 ⇒ 请求被忽略（**不抛异常**）。
    /// </summary>
    public IViewDisplayAccess Display { get; private set; } = EmptyViewDisplay.Instance;

    /// <summary>挂上显示请求面（**由 L4 在创建实例时调用**；模块作者不使用本方法）。</summary>
    /// <param name="display">按实例绑定的请求面。</param>
    public void AttachDisplay(IViewDisplayAccess display)
    {
        ArgumentNullException.ThrowIfNull(display);
        Display = display;
    }

    /// <summary>
    /// **文件选择的请求面**（模块 → 框架的"请求"方向）：模块说"帮我弹一次系统选择器"，
    /// 选定的路径经**输入通道**投回。未注入 ⇒ 请求被忽略（**不抛异常**）。
    /// </summary>
    public IFileAccess Files { get; private set; } = EmptyFileAccess.Instance;

    /// <summary>挂上文件选择请求面（**由 L4/宿主在创建实例时调用**；模块作者不使用本方法）。</summary>
    /// <param name="files">按实例绑定的请求面。</param>
    public void AttachFiles(IFileAccess files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = files;
    }

    /// <summary>
    /// **模块自有资源访问面**（模块 → 框架的「只读」方向）：只读本模块目录下的 `resources/`。
    /// 未挂时回落到"读不到任何东西"（**不抛异常**）。
    /// </summary>
    public IResourceAccess Resources { get; private set; } = ResourceAccess.Empty;

    /// <summary>挂上模块自有资源访问面（**由 L4 在创建实例时调用**；模块作者不使用本方法）。</summary>
    /// <param name="resources">按模块包绑定的只读访问面。</param>
    public void AttachResources(IResourceAccess resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Resources = resources;
    }

    /// <summary>已声明的口（按声明顺序）。</summary>
    public IReadOnlyList<PortDeclaration> DeclaredPorts => _ports;

    /// <summary>
    /// **用 XAML 声明的端口替换 C# 声明**（Hosting 在 `OnRegistered` 之后、成员实例化之前调用）。
    /// </summary>
    /// <param name="ports">已按数据类型解析好的端口。</param>
    /// <remarks>
    /// 只在模块包带 `views/` 端口声明时调用；旧模块没有该调用，保持 `DeclarePort` 的收集结果。
    /// </remarks>
    internal void ReplaceDeclaredPorts(IReadOnlyList<PortDeclaration> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        _ports.Clear();
        _ports.AddRange(ports);
    }

    /// <summary>已声明的**数据类型**（按声明顺序）。</summary>
    public IReadOnlyList<DataTypeDeclaration> DeclaredDataTypes => _dataTypes;

    /// <summary>已声明的**内部处理关系**（按声明顺序）——合法性由工厂在成员实例化后统一校验。</summary>
    public IReadOnlyList<FlowDeclaration> DeclaredFlows => _flows;

    /// <summary>已声明的**角色**（按声明顺序）——合法性由工厂在成员实例化后统一校验。</summary>
    public IReadOnlyList<RoleDeclaration> DeclaredRoles => _roles;

    /// <summary>已声明的**显示源**（按声明顺序）——合法性由工厂统一校验（重名/空名）。</summary>
    public IReadOnlyList<DisplaySourceDeclaration> DeclaredDisplaySources => _displaySources;

    /// <summary>已声明的**输入源名**（按声明顺序）——提交闸门据此判定。</summary>
    public IReadOnlyList<string> DeclaredInputs => _inputs;

    /// <summary>声明为持久化的输入源名（随组合件/工作区保存）；顺序不保证。</summary>
    public IReadOnlyCollection<string> PersistentInputs => _persistentInputs;

    /// <summary>已声明的**值**（按声明顺序）。</summary>
    public IReadOnlyList<ValueDeclaration> DeclaredValues => _values;

    /// <inheritdoc />
    public IModuleValues Values { get; }

    /// <summary>读某一格当前的有效性结论（控件侧呈现用）。</summary>
    /// <param name="name">值名。</param>
    /// <param name="validity">结论；没有 ⇒ <see cref="ValueValidity.Valid"/>。</param>
    /// <returns>是否有该值的结论。</returns>
    public bool TryGetValueValidity(string name, out ValueValidity validity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            validity = ValueValidity.Valid;
            return false;
        }

        lock (_valueValidityGate)
        {
            return _valueValidities.TryGetValue(name, out validity);
        }
    }

    /// <summary>
    /// 按名字读配置字段的当前文本（配置字段也是值；模块不需要知道它属于哪个配置对象）。
    /// </summary>
    /// <param name="name">字段名。</param>
    /// <returns>值的文本形态；没有该字段 ⇒ <see langword="null"/>。</returns>
    private string? ReadConfigurationFieldText(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_configurationGate)
        {
            foreach (var configuration in _configuration.Values)
            {
                var property = configuration.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.Instance);

                if (property is null || !property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                var value = property.GetValue(configuration);

                return value switch
                {
                    null => string.Empty,
                    bool boolean => boolean ? "true" : "false",
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// **把参与还原的值恢复为声明默认值**（框架内部：模块「还原」时调用）。
    /// </summary>
    /// <remarks>配置字段由 <see cref="ResetConfiguration"/> 处理；本方法只清声明值。</remarks>
    public void ResetValues()
    {
        foreach (var value in _values)
        {
            if (!value.ResetWithModule)
            {
                continue;
            }

            ViewState.SetInputValue(value.Name, null);
        }

        lock (_valueValidityGate)
        {
            _valueValidities.Clear();
        }
    }

    /// <summary>模块是否声明过该值。</summary>
    /// <param name="name">值名。</param>
    public bool DeclaresValue(string name) =>
        _values.Exists(value => string.Equals(value.Name, name, StringComparison.Ordinal));

    /// <summary>按名字取声明（未声明 ⇒ <see langword="null"/>）。</summary>
    /// <param name="name">值名。</param>
    public ValueDeclaration? FindValueDeclaration(string name) =>
        _values.Find(value => string.Equals(value.Name, name, StringComparison.Ordinal));

    /// <summary>已声明的**命令**（名字 + 给人看的标题，按声明顺序）——触发闸门与诊断文案据此判定。</summary>
    public IReadOnlyList<(string Name, string Title)> DeclaredCommands => _commands;

    /// <summary>已声明的**外部接入**（按声明顺序；合法性由工厂统一校验）。</summary>
    public IReadOnlyList<ExternalAccessDeclaration> DeclaredExternalAccesses => _externalAccesses;

    /// <summary>
    /// **外部接入的存取面**（由 L4 在承载启动前挂上）；未挂时回落到"请求被忽略、读无数据"的空实现。
    /// </summary>
    public IExternalAccess Access { get; private set; } = ExternalAccessDefaults.Empty;

    /// <summary>挂上外部接入存取面（**由 L4 在承载启动前调用**；模块作者不使用本方法）。</summary>
    /// <param name="access">按实例绑定的存取面。</param>
    public void AttachAccess(IExternalAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        Access = access;
    }

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
    /// **配置变更号**（框架内部：Schema / 表单缓存的失效依据；每次配置对象被替换时递增）。
    /// </summary>
    /// <remarks>不序列化、不进入组合件；模块作者不使用。</remarks>
    public long ConfigurationVersion => Interlocked.Read(ref _configurationVersion);

    /// <summary>
    /// **按类型登记一份配置**（框架内部：外部接入的参数类型由标准目录的 descriptor 给出，
    /// 框架据此让配置表单与持久化自动复用该类型）。
    /// </summary>
    /// <param name="type">配置类型（公开、非抽象、有公共无参构造）。</param>
    /// <returns>登记后的对象；类型不合法或无法实例化时为 <see langword="null"/>（由调用方报 Issue）。</returns>
    public object? TryEnsureConfiguration(Type? type)
    {
        if (type is null || !type.IsClass || type.IsAbstract)
        {
            return null;
        }

        var key = type.FullName;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        lock (_configurationGate)
        {
            if (_configuration.TryGetValue(key, out var existing) && existing.GetType() == type)
            {
                return existing;
            }

            try
            {
                if (Activator.CreateInstance(type) is not { } created)
                {
                    return null;
                }

                _configuration[key] = created;
                _ = Interlocked.Increment(ref _configurationVersion);
                return created;
            }
            catch (Exception ex) when (ex is MissingMethodException
                                          or MemberAccessException
                                          or System.Reflection.TargetInvocationException
                                          or TypeInitializationException
                                          or NotSupportedException
                                          or ArgumentException)
            {
                return null;
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
            _ = Interlocked.Increment(ref _configurationVersion);
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

            _ = Interlocked.Increment(ref _configurationVersion);
            return true;
        }
    }

    /// <inheritdoc />
    public override string ToString() => _objectPath;

    // ---------------------------------------------------------------- 内部实现

    private sealed class DeclarationCollector(
        List<PortDeclaration> ports,
        List<DataTypeDeclaration> types,
        List<FlowDeclaration> flows,
        List<RoleDeclaration> roles,
        List<DisplaySourceDeclaration> displaySources,
        List<string> inputs,
        HashSet<string> persistentInputs,
        List<ValueDeclaration> values,
        List<(string Name, string Title)> commands,
        List<ExternalAccessDeclaration> externalAccesses) : IModuleDeclaration
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

        // **三条界面契约的收集**：只登记，重名/空名由工厂统一判（这里不抛、也不改名 ——
        // 模块的写法归模块，判定与上报归框架，两件事不混在一处）。
        public void DeclareDisplaySource(DisplaySourceDeclaration source)
        {
            ArgumentNullException.ThrowIfNull(source);
            displaySources.Add(source);
        }

        public void DeclareInput(string sourceName)
        {
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                if (!inputs.Contains(sourceName, StringComparer.Ordinal))
                {
                    inputs.Add(sourceName);
                }
            }
        }

        public void DeclareInput(InputDeclaration input)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                return;
            }

            DeclareInput(input.Name);

            if (input.Persistence == InputPersistence.Persistent)
            {
                _ = persistentInputs.Add(input.Name);
            }
        }

        public void DeclareValue(ValueDeclaration value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (string.IsNullOrWhiteSpace(value.Name))
            {
                return;
            }

            values.Add(value);

            // 值当前落在视图状态的后端；这属于框架内部实现，不进入模块/控件契约。
            DeclareInput(new InputDeclaration(value.Name)
            {
                Persistence = value.Persistent ? InputPersistence.Persistent : InputPersistence.Transient,
            });
        }

        public void DeclareCommand(string name, string title)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                commands.Add((name, string.IsNullOrWhiteSpace(title) ? name : title));
            }
        }

        public void DeclareExternalAccess(ExternalAccessDeclaration access)
        {
            ArgumentNullException.ThrowIfNull(access);
            externalAccesses.Add(access);
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

    /// <summary>模块侧的值面：读当前值、接受规范化值、报告这一格的结论。</summary>
    private sealed class ModuleValueAccess(ModuleContext owner) : IModuleValues
    {
        /// <inheritdoc />
        public string? ReadText(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var declaration = owner.FindValueDeclaration(name);
            if (declaration is not null)
            {
                return owner.ViewState.GetInputText(name);
            }

            // 配置字段也是值：按名字读取当前配置属性（模块不需要知道它存在哪份配置对象里）。
            return owner.ReadConfigurationFieldText(name);
        }

        /// <inheritdoc />
        public void AcceptText(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(name) || !owner.DeclaresValue(name))
            {
                return;
            }

            owner.ViewState.SetInputValue(name, value);
        }

        /// <inheritdoc />
        public ValueValidity ReadValidity(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ValueValidity.Valid;
            }

            lock (owner._valueValidityGate)
            {
                return owner._valueValidities.TryGetValue(name, out var validity)
                    ? validity
                    : ValueValidity.Valid;
            }
        }

        /// <inheritdoc />
        public void ReportValidity(string name, ValueValidity validity)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            lock (owner._valueValidityGate)
            {
                if (validity == ValueValidity.Valid)
                {
                    _ = owner._valueValidities.Remove(name);
                }
                else
                {
                    owner._valueValidities[name] = validity;
                }
            }
        }
    }

    /// <summary>
    /// **人机操作收件箱**实现：有界（<see cref="Capacity"/>），满时**丢最旧**并计数。
    /// </summary>
    /// <remarks>
    /// 并发：投递方是**管理线程**（变更提交），消费方是**模块线程**（step）→ 用锁保护；
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

        public bool HasPending
        {
            get
            {
                lock (_gate)
                {
                    return _commands.Count > 0 || _inputs.Count > 0;
                }
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
    private sealed class InMemoryConfigurationAccess(
        Dictionary<string, object> store,
        Lock gate,
        Action onChanged) : IConfigurationAccess
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
                onChanged();
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
                onChanged();
            }
        }
    }

    /// <summary>**"请求被忽略"的显示请求面**（未注入真实面时的回落 —— 不抛异常）。</summary>
    private sealed class EmptyViewDisplay : IViewDisplayAccess
    {
        public static readonly EmptyViewDisplay Instance = new();

        public void Clear(string dataSourceName)
        {
            // 无框架接线 ⇒ 忽略（模块不该被这个面绊倒）
        }

        public void SetText(string dataSourceName, string text)
        {
            // 同上：替换入口在未接线时同样是"忽略"，不抛
        }

        public void AppendText(string dataSourceName, string text)
        {
            // 同上：只增的追加入口在未接线时同样是"忽略"，不抛
        }

        public void AppendLine(string dataSourceName, string text)
        {
            // 同上
        }

        public bool IsFull(string dataSourceName) => false;

        public void SetSeriesWindow(string dataSourceName, Sdk.Views.SeriesWindowSnapshot snapshot)
        {
        }
    }

    /// <summary>**"请求被忽略"的文件选择面**（未注入真实面时的回落 —— 不抛异常）。</summary>
    private sealed class EmptyFileAccess : IFileAccess
    {
        public static readonly EmptyFileAccess Instance = new();

        public void Request(FilePickRequest request)
        {
            // 无宿主接线 ⇒ 忽略（模块不该被这个面绊倒；用户看不到对话框，模块也收不到路径）
        }
    }

    /// <summary>
    /// **"全部未知"的视图状态读取面**（未注入真实面时的回落）：一律 <see langword="null"/> / 空，
    /// **不抛异常**（"未声明 / 未渲染"与"模块不该被这个面绊倒"同一纪律）。
    /// </summary>
    private sealed class EmptyViewState : IViewStateReader
    {
        public static readonly EmptyViewState Instance = new();

        public int? GetSelectedPage(string stateKey) => null;

        public string? GetInputText(string sourceName) => null;

        public void SetInputValue(string sourceName, string? text)
        {
            _ = sourceName;
            _ = text;
        }

        public IReadOnlyList<string> GetDisplayLines(string sourceName) => [];
    }
}
