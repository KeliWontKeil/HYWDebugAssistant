using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// 侧栏底部的轻量性能监视：CPU / 工作集 / 线程数 / GC 计数。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不拥有定时器</b>：由 <see cref="ShellViewModel.Refresh"/> 每拍调用 <see cref="Tick"/>，
///     内部按真实时间限流到约 1 Hz ⇒ 不新增 UI 定时器，也不影响模块/动画刷新节拍；</item>
///   <item><b>只读</b>：不触发 GC、不改运行时、不参与任何机制判定；关闭后不再采样；</item>
///   <item><b>失败不崩</b>：进程计数器取不到时显示 <c>--</c>，下一次继续尝试。</item>
/// </list>
/// </remarks>
public sealed partial class PerformanceMonitorViewModel : ViewModelBase
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    private Process? _process;
    private TimeSpan _lastCpu;
    private long _lastSampleTimestamp;

    /// <summary>是否启用监视（由应用设置驱动；关闭时不采样）。</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>CPU 占用文本（相对整机）。</summary>
    [ObservableProperty]
    public partial string CpuText { get; set; } = string.Empty;

    /// <summary>工作集文本。</summary>
    [ObservableProperty]
    public partial string MemoryText { get; set; } = string.Empty;

    /// <summary>线程数文本。</summary>
    [ObservableProperty]
    public partial string ThreadText { get; set; } = string.Empty;

    /// <summary>GC 计数文本（gen0/gen1/gen2）。</summary>
    [ObservableProperty]
    public partial string GcText { get; set; } = string.Empty;

    /// <summary>悬停时显示完整口径（工作集 / 专用字节 / 托管堆 / 线程 / GC）。</summary>
    [ObservableProperty]
    public partial string Tooltip { get; set; } = string.Empty;

    /// <summary>创建监视器；默认关闭，等设置载入后再开始采样。</summary>
    public PerformanceMonitorViewModel()
    {
        SetUnavailable();
    }

    /// <summary>由外壳每拍调用；只有启用且距上次采样满 1 秒时才真正读取进程。</summary>
    public void Tick()
    {
        if (!IsEnabled)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastSampleTimestamp != 0
            && Stopwatch.GetElapsedTime(_lastSampleTimestamp, now) < SampleInterval)
        {
            return;
        }

        Sample(now);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!value)
        {
            _process?.Dispose();
            _process = null;
            _lastSampleTimestamp = 0;
            _lastCpu = default;
            return;
        }

        // 重新启用时立即给出一帧，并重新建立 CPU 的差分基线。
        _lastSampleTimestamp = 0;
        Sample(Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// CPU 百分比（纯函数）：CPU 时间差值 / 实际经过时间 / 核数，夹到 0–100。
    /// </summary>
    /// <param name="cpuDelta">两次采样的进程 CPU 时间差。</param>
    /// <param name="elapsed">两次采样的墙钟时间差。</param>
    /// <param name="processorCount">逻辑处理器数。</param>
    /// <returns>0–100 的百分比。</returns>
    public static double CalculateCpuPercent(TimeSpan cpuDelta, TimeSpan elapsed, int processorCount)
    {
        if (elapsed <= TimeSpan.Zero || cpuDelta <= TimeSpan.Zero)
        {
            return 0;
        }

        var cores = Math.Max(1, processorCount);
        var percent = cpuDelta.TotalSeconds / elapsed.TotalSeconds / cores * 100;
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>把字节数格式化成工具栏能放下的短文本（M/G）。</summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>如 <c>48M</c> / <c>1.2G</c>。</returns>
    public static string FormatMemory(long bytes)
    {
        var value = Math.Max(0, bytes) / 1024d / 1024d;
        return value >= 1024
            ? (value / 1024).ToString("0.0", CultureInfo.InvariantCulture) + "G"
            : value.ToString("0", CultureInfo.InvariantCulture) + "M";
    }

    private void Sample(long now)
    {
        try
        {
            _process ??= Process.GetCurrentProcess();
            _process.Refresh();

            var cpu = _process.TotalProcessorTime;
            var elapsed = _lastSampleTimestamp == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(_lastSampleTimestamp, now);
            var cpuPercent = _lastSampleTimestamp == 0
                ? 0
                : CalculateCpuPercent(cpu - _lastCpu, elapsed, Environment.ProcessorCount);

            _lastCpu = cpu;
            _lastSampleTimestamp = now;

            var workingSet = _process.WorkingSet64;
            var privateBytes = _process.PrivateMemorySize64;
            var managedBytes = GC.GetTotalMemory(forceFullCollection: false);
            var threads = _process.Threads.Count;
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);

            CpuText = Line("shell.performance.cpu", cpuPercent.ToString("0", CultureInfo.InvariantCulture) + "%");
            MemoryText = Line("shell.performance.memory", FormatMemory(workingSet));
            ThreadText = Line("shell.performance.threads", threads.ToString(CultureInfo.InvariantCulture));
            GcText = Line("shell.performance.gc", $"{gen0}/{gen1}/{gen2}");
            Tooltip = Loc(
                "shell.performance.tip",
                cpuPercent.ToString("0", CultureInfo.InvariantCulture),
                FormatMemory(workingSet),
                FormatMemory(privateBytes),
                FormatMemory(managedBytes),
                threads,
                gen0,
                gen1,
                gen2);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or NotSupportedException
                                      or System.ComponentModel.Win32Exception)
        {
            SetUnavailable();
        }
    }

    private void SetUnavailable()
    {
        CpuText = Line("shell.performance.cpu", "--");
        MemoryText = Line("shell.performance.memory", "--");
        ThreadText = Line("shell.performance.threads", "--");
        GcText = Line("shell.performance.gc", "--");
        Tooltip = Loc("shell.performance.tip", "--", "--", "--", "--", "--", "--", "--", "--");
    }

    private static string Line(string key, string value) =>
        Loc(key) + " " + value;

    private static string Loc(string key) =>
        TextCatalog.Get(typeof(PerformanceMonitorViewModel).Assembly, key);

    private static string Loc(string key, params object?[] args) =>
        TextCatalog.Get(typeof(PerformanceMonitorViewModel).Assembly, key, args);
}
