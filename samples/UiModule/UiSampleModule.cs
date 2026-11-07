using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Samples.UiModule;

/// <summary>
/// **带界面的最小样例**：收一个 <c>u32</c> 累加，显示计数与状态，并提供「复位」命令。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>视图工厂</b>：<see cref="Views.UiSampleViewFactory"/> 每个实例造一次视图；</item>
///   <item><b>命令</b>：按钮触发的命令名在 <c>OnRegistered</c> 声明，在 <see cref="OnStep"/> 里从收件箱取出；</item>
///   <item><b>显示源</b>：一个数值源（计数）与一个文本源（状态句），视图只按名字读取；</item>
///   <item><b>文本</b>：界面文案全部来自本模块的文本表，视图里只写键。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "sample",
    "ui-module",
    DisplayNameKey = UiSampleText.Title,
    DescriptionKey = UiSampleText.Description,
    Tags = ["sample", "display"],
    ParticipatesInDataFlow = true,
    WindowWidth = 340,
    WindowHeight = 168,
    MinWidth = 340,
    MinHeight = 168,
    ViewFactory = typeof(Views.UiSampleViewFactory))]
public sealed class UiSampleModule : IModule, IActivitySource
{
    /// <summary>接收口。</summary>
    public const string InPort = "in";

    /// <summary>角色标识。</summary>
    public const string RoleId = "count";

    /// <summary>本模块声明的数据类型标识。</summary>
    public const string TypeId = "sample/counter";

    /// <summary>显示源：累计值。</summary>
    public const string CountSource = "count";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>命令：把累计值清零。</summary>
    public const string ResetCommand = "reset";

    /// <summary>数据形状：一个 32 位无符号整数。</summary>
    public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.U32);

    private IModuleContext? _context;
    private ulong _count;

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));

        context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape)
        {
            TypeId = TypeId,
            Edge = PortEdge.Left,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Input(
            RoleId,
            TypeId,
            InPort,
            UiSampleText.Get(UiSampleText.RoleName)));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            CountSource,
            ViewDataSourceKind.Value,
            ContractPrimitive.U64));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StateSource, ViewDataSourceKind.Text));

        context.Declaration.DeclareCommand(ResetCommand, UiSampleText.Get(UiSampleText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnData);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is not { } context)
        {
            return;
        }

        PublishCount(context);
        context.Display.SetText(StateSource, UiSampleText.Get(UiSampleText.Ready));
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
                context.Display.SetText(StateSource, UiSampleText.Get(UiSampleText.ResetDone));
            }
        }

        while (context.Inbox.TryDequeueInput(out _, out _))
        {
            // 本模块没有输入源。
        }
    }

    /// <summary>收到一个单元：按 u32 累加。</summary>
    /// <param name="payload">数据单元（长度 = 契约尺寸）。</param>
    private void OnData(ReadOnlySpan<byte> payload)
    {
        if (_context is not { } context || payload.Length < sizeof(uint))
        {
            return;
        }

        _count += BinaryPrimitives.ReadUInt32LittleEndian(payload);
        PublishCount(context);
        context.Display.SetText(StateSource, UiSampleText.Get(UiSampleText.Ready));
    }

    /// <summary>把计数发布到显示源（小端 U64）。</summary>
    /// <param name="context">模块上下文。</param>
    private void PublishCount(IModuleContext context)
    {
        Span<byte> value = stackalloc byte[sizeof(ulong)];
        UiSampleText.Write(_count, value);
        context.Ports.Publish(CountSource, value);
    }
}
