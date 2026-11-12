using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Template.MyModule;

/// <summary>
/// **模块模板**：一个接收口 + 一个发送口、一种数据类型、一个角色、一个配置字段、
/// 一个命令、一个定时器与一个最小视图。外部作者复制本目录后改三处身份即可开始。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>数据类型与端口由模块自己声明；对端要连接必须声明同一标识与形状；</item>
///   <item>配置只在 <see cref="OnRegistered"/> 读取一次即登记，配置变更时重新读取；</item>
///   <item>定时器由模块创建、框架调度，Tick 在实例线程触发；</item>
///   <item>视图只按声明的名字取数、提交；文案只写文本表键。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "template",
    "module",
    DisplayNameKey = TemplateText.Title,
    DescriptionKey = TemplateText.Description,
    Tags = ["template"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.TemplateViewFactory))]
public sealed class TemplateModule : IModule, IActivitySource
{
    /// <summary>接收口。</summary>
    public const string InPort = "in";

    /// <summary>发送口。</summary>
    public const string OutPort = "out";

    /// <summary>角色标识。</summary>
    public const string RoleId = "forward";

    /// <summary>类型标识（PascalCase 短名，不带前缀、不带版本）。</summary>
    public const string TypeId = "TemplateFrame";

    /// <summary>显示源：累计事件数。</summary>
    public const string CountSource = "count";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>命令：清零。</summary>
    public const string ResetCommand = "reset";

    /// <summary>配置字段名（与 <see cref="TemplateConfig.IntervalMs"/> 一致）。</summary>
    public const string IntervalField = nameof(TemplateConfig.IntervalMs);

    /// <summary>数据形状：<c>{ sequence: u64, value: f32 }</c>。</summary>
    public static readonly ContractShape Shape = ContractShape.OfStruct(
        ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    private IModuleContext? _context;
    private IModuleTimer? _timer;
    private TemplateConfig _config = new();
    private ulong _count;

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _config = context.Configuration.Read<TemplateConfig>();

        context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));

        context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape)
        {
            TypeId = TypeId,
            Edge = PortEdge.Left,
        });

        context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
        {
            TypeId = TypeId,
            Edge = PortEdge.Right,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Both(
            RoleId, TypeId, InPort, OutPort, TemplateText.Get(TemplateText.RoleName)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            CountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareCommand(ResetCommand, TemplateText.Get(TemplateText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnData);

        _timer = context.Timers.Create();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_config.IntervalMs, 10, 60_000));
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is not { } context)
        {
            return;
        }

        PublishCount(context);
        context.Display.SetText(StateSource, TemplateText.Get(TemplateText.Ready));
    }

    /// <inheritdoc />
    public void OnConfigurationChanged()
    {
        if (_context is not { } context || _timer is null)
        {
            return;
        }

        _config = context.Configuration.Read<TemplateConfig>();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_config.IntervalMs, 10, 60_000));
    }

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, ResetCommand, StringComparison.Ordinal))
            {
                _count = 0;
                PublishCount(context);
                context.Display.SetText(StateSource, TemplateText.Get(TemplateText.ResetDone));
            }
        }

        while (context.Inbox.TryDequeueInput(out _, out _))
        {
            // 模板没有输入源；需要输入时声明 DeclareInput 后在这里处理。
        }
    }

    /// <inheritdoc />
    public void OnRemoved() => _timer?.Dispose();

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_context is not { } context)
        {
            return;
        }

        _count++;
        PublishCount(context);
        context.Display.SetText(StateSource, TemplateText.Get(TemplateText.Tick));
    }

    private void OnData(ReadOnlySpan<byte> payload)
    {
        if (_context is not { } context || payload.Length < Shape.SizeInBytes)
        {
            return;
        }

        _count++;
        _ = context.Ports.TrySend(OutPort, payload);
        PublishCount(context);
        context.Display.SetText(StateSource, TemplateText.Get(TemplateText.Received));
    }

    private void PublishCount(IModuleContext context)
    {
        Span<byte> value = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(value, _count);
        context.Ports.Publish(CountSource, value);
    }
}
