using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Template.MyModule;

/// <summary>
/// **模块模板**：一个接收口 + 一个发送口、一种数据类型、一个角色、一个值、
/// 一个命令、一个定时器与一个最小视图。外部作者复制本目录后改三处身份即可开始。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>数据类型与端口由模块自己声明；对端要连接必须声明同一标识与形状；</item>
///   <item>可编辑的值用 <c>DeclareValue</c> 声明，模块用 <c>Values.ReadText</c> 读取；</item>
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

    /// <summary>值名：定时器间隔（毫秒）。</summary>
    public const string IntervalValue = "intervalMs";

    /// <summary>数据形状：<c>{ sequence: u64, value: f32 }</c>。</summary>
    public static readonly ContractShape Shape = ContractShape.OfStruct(
        ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    private IModuleContext? _context;
    private IModuleTimer? _timer;
    private int _intervalMs;
    private ulong _count;

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));

        // 端口声明在 Views/TemplateView.axaml 的 ModuleView.Ports 里（随包 views/ 源解析）。

        context.Declaration.DeclareRole(RoleDeclaration.Both(
            RoleId, TypeId, InPort, OutPort, TemplateText.Get(TemplateText.RoleName)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            CountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareValue(new ValueDeclaration(IntervalValue)
        {
            Editor = ValueEditor.Number,
            Persistent = true,
        });
        context.Declaration.DeclareCommand(ResetCommand, TemplateText.Get(TemplateText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnData);

        _timer = context.Timers.Create();
        ApplyInterval(context);
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

        ApplyInterval(context);
        PublishCount(context);
        context.Display.SetText(StateSource, TemplateText.Get(TemplateText.Ready));
    }

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);

        ApplyInterval(context);

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

    private void ApplyInterval(IModuleContext context)
    {
        if (!int.TryParse(
                context.Values.ReadText(IntervalValue),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            return;
        }

        value = Math.Clamp(value, 10, 60_000);

        if (value == _intervalMs)
        {
            return;
        }

        _intervalMs = value;

        if (_timer is not null)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(value);
        }
    }

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
