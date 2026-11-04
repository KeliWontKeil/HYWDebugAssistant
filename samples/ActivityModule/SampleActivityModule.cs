using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Samples.ActivityModule;

/// <summary>
/// **最小 step 样例**：每 100 ms 发布一次计数，演示 OnStep / NextDelay。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>没有端口、没有外部接入：只声明一个显示源；</item>
///   <item>不写循环、不建定时器：框架计时服务负责到期，模块只给相对 <c>NextDelay</c>；</item>
///   <item>没有视图工厂是合法形态：窗口只显示身份与状态。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "sample",
    "activity-module",
    DisplayName = "示例周期模块",
    Description = "最小自发活动样例：每 100 ms 发布一次计数，不拥有循环",
    Tags = ["sample"],
    ParticipatesInDataFlow = false,
    WindowWidth = 320,
    WindowHeight = 120,
    MinWidth = 320,
    MinHeight = 120)]
public sealed class SampleActivityModule : IModule, IActivitySource
{
    /// <summary>显示源：已触发次数。</summary>
    public const string TicksSource = "ticks";

    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private ulong _ticks;

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public TimeSpan? NextDelay => Period;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            TicksSource,
            ViewDataSourceKind.Value,
            Sdk.Contracts.ContractPrimitive.U64));
    }

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!reason.HasFlag(StepReason.Start) && !reason.HasFlag(StepReason.Due))
        {
            return;
        }

        _ticks++;
        context.Ports.Publish(TicksSource, BitConverter.GetBytes(_ticks));
    }
}
