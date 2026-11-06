using System.Diagnostics;

namespace HYWDebugAssistant.DataPlane.Queues;

/// <summary>
/// **状态行的小滞回**：把“当前深度 / 当前丢弃”收敛为稳定的“积压 / 丢弃”呈现。
/// </summary>
/// <remarks>
/// <para>
/// 它不是调度状态机：不拥有队列、不丢数据、不产生线程，只在查询时用调用方给的单调时刻计算。
/// 原始计数、深度与丢弃事实仍由队列/会话保存，状态只是**呈现层**。
/// </para>
/// <para>
/// 进入/退出使用不同的深度阈值与同一个保持时长，避免 10 ms 发送下 1 个在途单元造成闪烁；
/// 丢弃事实消失后额外保持一段时间，避免“丢弃 ↔ 正常”高频跳变。
/// </para>
/// </remarks>
internal sealed class StatusHysteresis
{
    private int _enterDepth;
    private int _exitDepth;
    private TimeSpan _hold;
    private TimeSpan _dropHold;
    private int _depth;
    private bool _backlogActive;
    private long _backlogCandidateTicks = -1;
    private bool _dropRaw;
    private bool _dropActive;
    private long _dropReleaseTicks;

    public StatusHysteresis(
        int enterDepth,
        int exitDepth,
        TimeSpan hold,
        TimeSpan dropHold)
    {
        UpdatePolicy(enterDepth, exitDepth, hold, dropHold);
    }

    /// <summary>当前积压显示是否激活。</summary>
    public bool IsBacklogActive(long nowTicks)
    {
        AdvanceBacklog(nowTicks);
        return _backlogActive;
    }

    /// <summary>当前丢弃显示是否激活。</summary>
    public bool IsDropActive(long nowTicks)
    {
        if (!_dropRaw && _dropActive && nowTicks >= _dropReleaseTicks)
        {
            _dropActive = false;
        }

        return _dropActive;
    }

    /// <summary>观察一次深度（入队 / 出队 / 清空后）。</summary>
    public void ObserveDepth(int depth, long nowTicks)
    {
        _depth = depth < 0 ? 0 : depth;
        AdvanceBacklog(nowTicks);
    }

    /// <summary>观察原始丢弃事实（当前是否仍处于丢弃 episode）。</summary>
    public void ObserveDrop(bool active, long nowTicks)
    {
        var wasRaw = _dropRaw;
        _dropRaw = active;

        if (active)
        {
            _dropActive = true;
            _dropReleaseTicks = nowTicks + ToTicks(_dropHold);
            return;
        }

        if (wasRaw && _dropActive)
        {
            // 从“仍有丢弃”变成“已无丢弃”的时刻起算保持时间；重复的 false 不延长。
            _dropReleaseTicks = nowTicks + ToTicks(_dropHold);
        }

        if (_dropActive && nowTicks >= _dropReleaseTicks)
        {
            _dropActive = false;
        }
    }

    /// <summary>更新阈值；保留当前激活状态，新阈值从下一次观察起生效。</summary>
    public void UpdatePolicy(int enterDepth, int exitDepth, TimeSpan hold, TimeSpan dropHold)
    {
        _enterDepth = Math.Max(1, enterDepth);
        _exitDepth = Math.Clamp(exitDepth, 0, _enterDepth - 1);
        _hold = hold < TimeSpan.Zero ? TimeSpan.Zero : hold;
        _dropHold = dropHold < TimeSpan.Zero ? TimeSpan.Zero : dropHold;
        _backlogCandidateTicks = -1;
    }

    /// <summary>重置到无压力（打开/关闭会话时）。</summary>
    public void Reset()
    {
        _depth = 0;
        _backlogActive = false;
        _backlogCandidateTicks = -1;
        _dropRaw = false;
        _dropActive = false;
        _dropReleaseTicks = 0;
    }

    private void AdvanceBacklog(long nowTicks)
    {
        if (_backlogActive)
        {
            if (_depth <= _exitDepth)
            {
                if (_backlogCandidateTicks < 0)
                {
                    _backlogCandidateTicks = nowTicks;
                }

                if (nowTicks - _backlogCandidateTicks >= ToTicks(_hold))
                {
                    _backlogActive = false;
                    _backlogCandidateTicks = -1;
                }
            }
            else
            {
                _backlogCandidateTicks = -1;
            }

            return;
        }

        if (_depth >= _enterDepth)
        {
            if (_backlogCandidateTicks < 0)
            {
                _backlogCandidateTicks = nowTicks;
            }

            if (nowTicks - _backlogCandidateTicks >= ToTicks(_hold))
            {
                _backlogActive = true;
                _backlogCandidateTicks = -1;
            }
        }
        else
        {
            _backlogCandidateTicks = -1;
        }
    }

    private static long ToTicks(TimeSpan value) =>
        value <= TimeSpan.Zero ? 0 : (long)(value.TotalSeconds * Stopwatch.Frequency);
}
