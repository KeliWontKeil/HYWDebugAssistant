using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Samples.ActivityModule;

/// <summary>
/// **最小定时器样例**：每 100 ms 发布一次计数，演示 <c>IModuleTimer</c> 的创建 / 启动 / Tick。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>没有端口、没有外部接入：只声明一个显示源；</item>
///   <item>不写循环、不建系统定时器：在上下文里创建框架定时器，框架负责调度并把 Tick 投回实例线程；</item>
///   <item>没有视图工厂是合法形态：窗口只显示身份与状态。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "sample",
    "activity-module",
    DisplayName = "示例定时器模块",
    Description = "最小定时器样例：每 100 ms 发布一次计数，不拥有循环",
    Tags = ["sample"],
    ParticipatesInDataFlow = false,
    MinHeight = 120)]
public sealed class SampleActivityModule : IModule
{
    /// <summary>显示源：已触发次数。</summary>
    public const string TicksSource = "ticks";

    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private ulong _ticks;
    private IModuleTimer? _timer;
    private IModuleContext? _context;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            TicksSource,
            ViewDataSourceKind.Value,
            Sdk.Contracts.ContractPrimitive.U64));

        _context = context;
        _timer = context.Timers.Create();
        _timer.Interval = Period;
        _timer.Tick += OnTimerTick;
    }

    /// <inheritdoc />
    public void OnActivated() => _timer?.Start();

    /// <inheritdoc />
    public void OnRemoved()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_context is not { } context)
        {
            return;
        }

        _ticks++;
        context.Ports.Publish(TicksSource, BitConverter.GetBytes(_ticks));
    }
}
