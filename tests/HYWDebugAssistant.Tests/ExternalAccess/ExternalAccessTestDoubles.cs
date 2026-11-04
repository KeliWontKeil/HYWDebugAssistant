using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;

namespace HYWDebugAssistant.Tests.ExternalAccess;

/// <summary>框架用例的假参数类型（要能进配置快照、能按字段写回）。</summary>
public sealed class FakeAccessParameters
{
    /// <summary>一个可写字段。</summary>
    [ConfigurationField("值")]
    public int Value { get; set; } = 1;
}

/// <summary>
/// 脚本化适配件：立刻/延迟就绪、推数据、写失败、打开失败、探活计数。
/// </summary>
public sealed class FakeExternalAccessAdapter : IExternalAccessAdapter, IExternalAccessLiveness, IExternalAccessWriteTimeout
{
    private readonly Queue<byte[]> _pending = new();
    private readonly Lock _gate = new();
    private IExternalAccessReady? _ready;

    /// <inheritdoc />
    public Type ParametersType => typeof(FakeAccessParameters);

    /// <summary>历次下发的写超时（打开前一次、运行期改设置各一次）。</summary>
    public List<TimeSpan> WriteTimeouts { get; } = [];

    /// <inheritdoc />
    public void SetWriteTimeout(TimeSpan timeout) => WriteTimeouts.Add(timeout);

    /// <summary>打开次数。</summary>
    public int OpenCount { get; private set; }

    /// <summary>关闭次数。</summary>
    public int CloseCount { get; private set; }

    /// <summary>探活调用次数。</summary>
    public int LivenessCalls { get; private set; }

    /// <summary>最近一次打开拿到的参数对象。</summary>
    public FakeAccessParameters? OpenedWith { get; private set; }

    /// <summary>写出的数据单元。</summary>
    public List<byte[]> Written { get; } = [];

    /// <summary>打开时是否抛异常。</summary>
    public bool ThrowOnOpen { get; set; }

    /// <summary>打开后是否立刻报告就绪。</summary>
    public bool AutoOpen { get; set; } = true;

    /// <summary>写是否成功。</summary>
    public bool WriteOk { get; set; } = true;

    /// <summary>
    /// 声称交付的长度（<see langword="null"/> ⇒ 如实上报）。
    /// </summary>
    /// <remarks>
    /// 用来复现"适配件声称的长度超过声明上限"：真实适配件不该这样，但框架必须防住它
    /// （否则超长单元会带着假长度进入队列）。
    /// </remarks>
    public int? LengthOverride { get; set; }

    /// <inheritdoc />
    public void Open(object parameters, IExternalAccessReady ready)
    {
        OpenCount++;
        OpenedWith = (FakeAccessParameters)parameters;
        _ready = ready;

        if (ThrowOnOpen)
        {
            throw new InvalidOperationException("假适配件：打开失败");
        }

        if (AutoOpen)
        {
            ready.SetOpen(new ExternalAccessEndpoint("127.0.0.1", 1));
        }
    }

    /// <inheritdoc />
    public bool TryRead(Span<byte> destination, out int length)
    {
        length = 0;

        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return false;
            }

            var payload = _pending.Dequeue();
            if (payload.Length > destination.Length)
            {
                return false;
            }

            payload.CopyTo(destination);
            length = LengthOverride ?? payload.Length;
            return true;
        }
    }

    /// <inheritdoc />
    public bool Write(ReadOnlySpan<byte> payload)
    {
        Written.Add(payload.ToArray());
        return WriteOk;
    }

    /// <inheritdoc />
    public void Close(TimeSpan timeout)
    {
        CloseCount++;
        _ready = null;

        lock (_gate)
        {
            _pending.Clear();
        }
    }

    /// <summary>模拟"外部来了数据"。</summary>
    public void Publish(ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            _pending.Enqueue(payload.ToArray());
        }

        _ready?.NotifyReady();
    }

    /// <summary>模拟"连接断开 / 读取失败"。</summary>
    public void Fault(ExternalAccessFailure failure) => _ready?.SetFaulted(failure);

    /// <inheritdoc />
    public void CheckLiveness() => LivenessCalls++;
}

/// <summary>
/// 脚本化外部标准目录：标准标识 → <see cref="FakeExternalAccessAdapter"/>（适配器由 catalog 提供）。
/// </summary>
/// <remarks>
/// 测试直接持有 <see cref="Adapter"/> 做断言；工厂每次返回同一个实例，正好对应"一个接入一个会话"。
/// </remarks>
public sealed class FakeExternalAccessCatalog : IExternalAccessCatalog
{
    private readonly ExternalAccessDescriptor[] _standards;

    /// <summary>创建假目录（只含一个标准）。</summary>
    public FakeExternalAccessCatalog()
    {
        Adapter = new FakeExternalAccessAdapter();
        _standards =
        [
            new ExternalAccessDescriptor(
                FakeAccessModule.StandardId,
                typeof(FakeAccessParameters),
                () => Adapter),
        ];
    }

    /// <summary>该标准创建出的适配件（测试断言用）。</summary>
    public FakeExternalAccessAdapter Adapter { get; }

    /// <inheritdoc />
    public IReadOnlyList<ExternalAccessDescriptor> Standards => _standards;

    /// <inheritdoc />
    public bool TryResolve(string standardId, out ExternalAccessDescriptor descriptor)
    {
        descriptor = null!;

        foreach (var candidate in _standards)
        {
            if (string.Equals(candidate.StandardId, standardId, StringComparison.Ordinal))
            {
                descriptor = candidate;
                return true;
            }
        }

        return false;
    }
}

/// <summary>无活动体的测试模块：声明一条接入，经交互回调接收命令/输入/工作提示。</summary>
public sealed class FakeAccessModule : IModule, IActivitySource
{
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
    /// <summary>接入名。</summary>
    public const string AccessName = "fake";

    /// <summary>外部标准标识（由假 catalog 提供）。</summary>
    public const string StandardId = "fake";

    private readonly Lock _gate = new();

    /// <summary>收到的命令。</summary>
    public List<string> Commands { get; } = [];

    /// <summary>收到的输入。</summary>
    public List<(string Source, string Text)> Inputs { get; } = [];

    /// <summary>收到的外部数据。</summary>
    public List<byte[]> Received { get; } = [];

    /// <summary><c>OnWork</c> 次数。</summary>
    public int WorkCount { get; private set; }

    /// <summary>注册期保存的上下文（用于发请求）。</summary>
    public IModuleContext? Context { get; private set; }

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        Context = context;
        context.Declaration.DeclareCommand("open", "打开");
        context.Declaration.DeclareCommand("close", "关闭");
        context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(AccessName, StandardId));
    }

    /// <inheritdoc />
    public void OnActivated() => Context?.Access.SetReceiveHandler(AccessName, OnAccessData);

    /// <inheritdoc />
    public void OnCommand(string commandName)
    {
        lock (_gate)
        {
            Commands.Add(commandName);
        }

        if (Context is null)
        {
            return;
        }

        if (string.Equals(commandName, "open", StringComparison.Ordinal))
        {
            _ = Context.Access.RequestOpen(AccessName);
        }
        else if (string.Equals(commandName, "close", StringComparison.Ordinal))
        {
            _ = Context.Access.RequestClose(AccessName);
        }
    }

    /// <inheritdoc />
    public void OnInput(string sourceName, string text)
    {
        lock (_gate)
        {
            Inputs.Add((sourceName, text));
        }
    }

    /// <inheritdoc />
    public void OnWork()
    {
        lock (_gate)
        {
            WorkCount++;
        }
    }

    private void OnAccessData(ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            Received.Add(payload.ToArray());
        }
    }
}

/// <summary>构造测试用模块定义。</summary>
internal static class FakeModuleDefinition
{
    public static ModuleDefinition Create(Type entryType) => new(
        ModuleId.Create("test", "access"),
        "测试接入模块",
        "框架用例用的无活动体模块",
        [],
        ModuleCapability.None,
        [],
        participatesInDataFlow: false,
        entryType: entryType,
        packageDirectory: string.Empty,
        assemblyPath: string.Empty);
}
