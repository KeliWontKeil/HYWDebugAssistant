using System.Diagnostics;

namespace HYWDebugAssistant.Core.Diagnostics;

/// <summary>
/// **速率计**：把"累计计数 + 时间戳"变成"每秒增量"——**框架侧唯一**的速率口径来源。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么要有它</b>【ADR-0046 D8】：此前"速率"是 **UI 自己差分估算**的（拿两次刷新的计数相减）
///     ⇒ 同一事实有多种口径、且 UI 慢一点数字就漂。现在统一由框架算，UI 只显示；</item>
///   <item><b>无时间戳依赖</b>：只用**单调计数**（<see cref="Stopwatch.GetTimestamp"/>），与墙钟/时区无关；</item>
///   <item><b>用法</b>：每次观察传**当前累计值**与当前时刻；首次观察只建立基准（返回 0），此后返回区间速率；</item>
///   <item><b>可重复读</b>【ADR-0053 实测补】<b>同一次刷新里的多次读取必须给出同一个数</b>：一次刷新会对**每个模块**
///     各读一遍诊断（端口标记、角色信息、角色连线…），若每次都重算"自上次观察以来"的增量，那么第一遍之后
///     增量恒为 0 ⇒ **除第一个消费者外全都读到 0**（现场表现：角色的速率**恒显示占位符**）。
///     ⇒ 小于 <see cref="MinIntervalSeconds"/> 的重复观察**直接返回上次速率**，不重算、不换基准；</item>
///   <item><b>线程</b>：**由观察方单线程使用**（实践中 = UI 刷新线程）；不做内部同步，避免每次观察都取锁。</item>
/// </list>
/// </remarks>
public sealed class RateMeter
{
    /// <summary>
    /// **最小重算间隔（秒）**：小于它的重复观察**返回上次速率**。
    /// </summary>
    /// <remarks>
    /// 取值依据：UI 的一次刷新（对每个模块各读一遍诊断）在**毫秒级**完成，而刷新的节拍是 **0.5 秒**
    /// ⇒ 0.1 秒足够把"同一次刷新内的所有读取"归并到同一份数值上，又不会拖慢任何真实更新。
    /// </remarks>
    public const double MinIntervalSeconds = 0.1;

    private long _lastTotal = -1;
    private long _lastTimestamp;
    private double _rate;

    /// <summary>最近一次算出的速率（每秒；尚未算出时为 0）。</summary>
    public double Rate => _rate;

    /// <summary>观察一次累计值。</summary>
    /// <param name="total">当前累计值（单调不减）。</param>
    /// <param name="timestamp">当前时刻（<see cref="Stopwatch.GetTimestamp"/>）。</param>
    /// <returns>自上次观察以来的速率（每秒）；首次观察、时间未推进或间隔过短时返回上次值。</returns>
    public double Observe(long total, long timestamp) => Observe(total, timestamp, Stopwatch.Frequency);

    /// <summary>观察一次累计值（显式给出时基，便于测试与跨平台核对）。</summary>
    /// <param name="total">当前累计值（单调不减）。</param>
    /// <param name="timestamp">当前时刻（时基单位）。</param>
    /// <param name="frequency">时基频率（每秒的计数，如 <see cref="Stopwatch.Frequency"/>）。</param>
    public double Observe(long total, long timestamp, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frequency, 1);

        if (_lastTotal < 0)
        {
            // 首个观察只建立基准：**不猜**历史速率（差分估算的错就在猜）
            _lastTotal = total;
            _lastTimestamp = timestamp;
            return 0;
        }

        var elapsed = (timestamp - _lastTimestamp) / (double)frequency;
        if (elapsed <= 0)
        {
            return _rate;
        }

        // **同一次刷新里的重复读取**：不重算、不换基准（否则后读到的消费者恒为 0）
        if (elapsed < MinIntervalSeconds)
        {
            return _rate;
        }

        var delta = total - _lastTotal;
        _lastTotal = total;
        _lastTimestamp = timestamp;

        // 计数回退（绑定重建/重置）⇒ 本次视为 0，不产生负速率
        _rate = delta <= 0 ? 0 : delta / elapsed;
        return _rate;
    }

    /// <summary>重置（绑定消失或计数器被替换时调用）。</summary>
    public void Reset()
    {
        _lastTotal = -1;
        _lastTimestamp = 0;
        _rate = 0;
    }
}
