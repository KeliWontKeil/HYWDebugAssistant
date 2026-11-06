using System.Diagnostics;
using HYWDebugAssistant.Sdk.Tuning;

namespace HYWDebugAssistant.DataPlane.Queues;

/// <summary>
/// 队列**饱和压力**的唯一算法：进/出压力段的判据、迟滞、峰值与"本段内丢了多少"都在这里。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>无定时器、无后台线程、稳态零分配</b>：只在拥有者**持锁**的入队 / 出队 / 丢弃路径上被调用；
///     是否饱和是**查询时**用调用方给的单调时刻算出来的——读快照不会把队列拖成忙等；</item>
///   <item><b>进段</b>：深度到达提示阈值即记下单调时刻；<b>出段</b>：深度跌到阈值的一半以下才清。
///     这条**迟滞**是为了让深度在阈值上下抖动时不会把一次拥塞拆成很多段
///     （否则"饱和了多久"永远停留在几毫秒）；</item>
///   <item><b>阈值改了就重开一段</b>：改容量/阈值之后，旧的"已经堵了多久"不再有意义，
///     故清掉本段起点与段内丢弃，<b>峰值保留</b>（峰值是历史事实，不因改参数而消失）。</item>
/// </list>
/// </remarks>
internal sealed class QueuePressure
{
    private int _hintDepth;
    private TimeSpan _hintHold;
    private int _peakDepth;
    private long _aboveSinceTicks;
    private long _droppedWhileSaturated;

    /// <summary>深度提示的缺省阈值（单元数）。</summary>
    internal const int DefaultHintDepth = FlowDefaults.HintDepth;

    /// <summary>深度提示的缺省保持时长。</summary>
    internal static readonly TimeSpan DefaultHintHold = TimeSpan.FromMilliseconds(FlowDefaults.HintHoldMs);

    /// <summary>创建压力计（阈值与保持时长立即生效，压力段为空）。</summary>
    /// <param name="hintDepth">深度提示阈值（单元数；小于 1 按 1 处理）。</param>
    /// <param name="hintHold">持续多久算饱和（负值按 0 处理）。</param>
    internal QueuePressure(int hintDepth, TimeSpan hintHold) => UpdateHint(hintDepth, hintHold);

    /// <summary>深度提示阈值（单元数）。</summary>
    internal int HintDepth => _hintDepth;

    /// <summary>持续多久算饱和。</summary>
    internal TimeSpan HintHold => _hintHold;

    /// <summary>观察到的最大深度（不随压力段重置）。</summary>
    internal int PeakDepth => _peakDepth;

    /// <summary>本次压力段的起点时刻（<see cref="Stopwatch"/> 计时刻度；0 = 未饱和）。</summary>
    internal long AboveSinceTicks => _aboveSinceTicks;

    /// <summary>本次压力段内发生的丢弃数（离开压力段即归零）。</summary>
    internal long DroppedWhileSaturated => _droppedWhileSaturated;

    /// <summary>记一次入队后的深度（更新峰值；到阈值即开段，跌到半阈值以下即收段）。</summary>
    /// <param name="depth">入队后的深度。</param>
    internal void OnEnqueued(int depth) => Observe(depth);

    /// <summary>记一次出队后的深度（更新峰值；到阈值即开段，跌到半阈值以下即收段）。</summary>
    /// <param name="depth">出队后的深度。</param>
    internal void OnDequeued(int depth) => Observe(depth);

    /// <summary>记一次丢弃（只有**在压力段内**才计入段内丢弃）。</summary>
    internal void OnDropped()
    {
        if (_aboveSinceTicks != 0)
        {
            _droppedWhileSaturated++;
        }
    }

    /// <summary>给定时刻是否已饱和（处在压力段内且已持续够久）。</summary>
    /// <param name="nowTicks">观察时刻（<see cref="Stopwatch.GetTimestamp"/>）。</param>
    internal bool IsSaturated(long nowTicks) =>
        _aboveSinceTicks != 0 && Stopwatch.GetElapsedTime(_aboveSinceTicks, nowTicks) >= _hintHold;

    /// <summary>给定时刻已持续饱和多久（未处在压力段内为 <see cref="TimeSpan.Zero"/>）。</summary>
    /// <param name="nowTicks">观察时刻（<see cref="Stopwatch.GetTimestamp"/>）。</param>
    internal TimeSpan SaturatedFor(long nowTicks) =>
        _aboveSinceTicks == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_aboveSinceTicks, nowTicks);

    /// <summary>改阈值：重置本次压力段（起点与段内丢弃归零，**峰值保留**）。</summary>
    /// <param name="hintDepth">深度提示阈值（单元数；小于 1 按 1 处理）。</param>
    /// <param name="hintHold">持续多久算饱和（负值按 0 处理）。</param>
    internal void UpdateHint(int hintDepth, TimeSpan hintHold)
    {
        _hintDepth = Math.Max(1, hintDepth);
        _hintHold = hintHold < TimeSpan.Zero ? TimeSpan.Zero : hintHold;
        _aboveSinceTicks = 0;
        _droppedWhileSaturated = 0;
    }

    private void Observe(int depth)
    {
        var observed = depth < 0 ? 0 : depth;

        if (observed > _peakDepth)
        {
            _peakDepth = observed;
        }

        if (observed >= _hintDepth)
        {
            if (_aboveSinceTicks == 0)
            {
                _aboveSinceTicks = Stopwatch.GetTimestamp();
            }

            return;
        }

        // 迟滞：只有明确跌到"半阈值"以下才收段，避免深度在阈值附近抖动时反复拆段
        if (_aboveSinceTicks != 0 && observed < Math.Max(1, _hintDepth / 2))
        {
            _aboveSinceTicks = 0;
            _droppedWhileSaturated = 0;
        }
    }
}
