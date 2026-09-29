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
///   <item><b>口径 = 整秒桶</b>【**2026-09 用户裁定**】："**算一秒的积累量**（短时间窗口推整秒在断续数据下不准）"
///     ⇒ 速率 = **上一个 ≥1 秒窗口内的增量**（除以该窗口的真实秒数）。窗口**锚定在第一次观察**，
///     与 UI 刷新节拍（30 帧 ≈ 0.5 s）**解耦**：同一份数据在 30 fps / 60 fps 节拍下读数一致。
///     <para>
///     **为什么不再用 0.1 秒的"重复观察即复用"**：那个补丁是为了让"同一趟刷新里的多个消费者"读到同一个数
///     （否则第一遍之后增量恒为 0 ⇒ 角色速率恒显示占位符）。整秒桶**天然**满足这条 —— 桶没满就返回上一次算出的值。
///     </para>
///   </item>
///   <item><b>断续数据的表现</b>：手动点一次发送 ⇒ 那一秒的窗口里就是 1 个单元的量（≈1.4 KB/s），
///     **下一秒如实回到 0**（界面显示 `0.00 B/s`，不再是"闪一下"的虚高峰值）；</item>
///   <item><b>线程</b>：**由观察方单线程使用**（实践中 = UI 刷新线程）；不做内部同步，避免每次观察都取锁。</item>
/// </list>
/// </remarks>
public sealed class RateMeter
{
    /// <summary>
    /// **整秒桶的窗口长度（秒）**：窗口未满 ⇒ 返回上一次算出的速率（不重算、不换基准）。
    /// </summary>
    /// <remarks>取值依据：用户裁定"算一秒的积累量"—— 1 秒既能让断续数据读数平滑、又不至于滞后太久。</remarks>
    public const double WindowSeconds = 1.0;

    private long _windowStartTotal = -1;
    private long _windowStartTimestamp;
    private double _rate;

    /// <summary>最近一次算出的速率（每秒；尚未算出时为 0）。</summary>
    public double Rate => _rate;

    /// <summary>观察一次累计值。</summary>
    /// <param name="total">当前累计值（单调不减）。</param>
    /// <param name="timestamp">当前时刻（<see cref="Stopwatch.GetTimestamp"/>）。</param>
    /// <returns>上一个整秒窗口内的每秒增量；首个窗口未满时返回上次值（首观察为 0）。</returns>
    public double Observe(long total, long timestamp) => Observe(total, timestamp, Stopwatch.Frequency);

    /// <summary>观察一次累计值（显式给出时基，便于测试与跨平台核对）。</summary>
    /// <param name="total">当前累计值（单调不减）。</param>
    /// <param name="timestamp">当前时刻（时基单位）。</param>
    /// <param name="frequency">时基频率（每秒的计数，如 <see cref="Stopwatch.Frequency"/>）。</param>
    public double Observe(long total, long timestamp, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frequency, 1);

        if (_windowStartTotal < 0)
        {
            // 首个观察只建立基准：**不猜**历史速率（差分估算的错就在猜）
            _windowStartTotal = total;
            _windowStartTimestamp = timestamp;
            return 0;
        }

        var elapsed = (timestamp - _windowStartTimestamp) / (double)frequency;

        // **窗口未满**（含"同一趟刷新里的重复观察"）：返回上一次的整秒量，不重算、不换基准。
        // 这条同时兜住"同一次刷新里多个消费者必须读到同一个数"（旧实现靠 0.1 s 的补丁，现在由桶天然保证）。
        if (elapsed < WindowSeconds)
        {
            return _rate;
        }

        var delta = total - _windowStartTotal;
        _windowStartTotal = total;
        _windowStartTimestamp = timestamp;

        // 计数回退（绑定重建/重置）⇒ 本次视为 0，不产生负速率。
        // 除以**真实经过时间**：窗口可能因节拍相位略超 1 秒（例如 1.2 s），不归一化就会把那一秒算大。
        _rate = delta <= 0 ? 0 : delta / elapsed;
        return _rate;
    }

    /// <summary>重置（绑定消失或计数器被替换时调用）。</summary>
    public void Reset()
    {
        _windowStartTotal = -1;
        _windowStartTimestamp = 0;
        _rate = 0;
    }
}
