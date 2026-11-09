namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>一个通道组的运行期状态：配置、动态历史、速率与完整性。</summary>
internal sealed class DisplayGroupRuntime
{
    /// <summary>组 ID。</summary>
    public required uint GroupId { get; init; }

    /// <summary>本组的显示配置（名称/颜色/显隐）。</summary>
    public required DisplayGroupConfig Config { get; set; }

    /// <summary>当前通道数。</summary>
    public int ChannelCount { get; private set; }

    /// <summary>每通道动态历史。</summary>
    public ChannelHistory[] Histories { get; private set; } = [];

    /// <summary>模块自己的数据编号（每收到一帧 +1；可清零）。</summary>
    public long DisplayIndex { get; private set; }

    /// <summary>当前 X 范围起点（样本索引；无范围时 &lt;0）。</summary>
    public long RangeStart { get; private set; } = -1;

    /// <summary>当前 X 范围终点（样本索引；无范围时 &lt;0）。</summary>
    public long RangeEnd { get; private set; } = -1;

    /// <summary>是否有可显示的 X 范围。</summary>
    public bool HasRange => RangeStart >= 0 && RangeEnd >= RangeStart;

    /// <summary>Y 自动锁存下限（NaN = 未锁存；只增不减）。</summary>
    public double AutoYMin { get; private set; } = double.NaN;

    /// <summary>Y 自动锁存上限（NaN = 未锁存；只增不减）。</summary>
    public double AutoYMax { get; private set; } = double.NaN;

    /// <summary>是否已有 Y 自动锁存。</summary>
    public bool HasAutoY => !double.IsNaN(AutoYMin) && !double.IsNaN(AutoYMax) && AutoYMax > AutoYMin;

    /// <summary>清除 Y 自动锁存（显式适应/复位/清空/通道重建时）。</summary>
    public void ResetAutoY()
    {
        AutoYMin = double.NaN;
        AutoYMax = double.NaN;
    }

    /// <summary>把数据范围并入 Y 自动锁存；只扩不缩。</summary>
    /// <param name="min">本次数据下限（含 padding）。</param>
    /// <param name="max">本次数据上限（含 padding）。</param>
    public void ExpandAutoY(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
        {
            return;
        }

        AutoYMin = HasAutoY ? Math.Min(AutoYMin, min) : min;
        AutoYMax = HasAutoY ? Math.Max(AutoYMax, max) : max;
    }

    /// <summary>设置 X 范围（调用方已夹紧）。</summary>
    public void SetRange(long start, long end)
    {
        RangeStart = start;
        RangeEnd = end;
    }

    /// <summary>清空 X 范围。</summary>
    public void ResetRange()
    {
        RangeStart = -1;
        RangeEnd = -1;
    }

    /// <summary>声明帧率（最后一帧的 rateHz）。</summary>
    public double DeclaredRateHz { get; set; }

    /// <summary>实测帧率（约 500 ms 窗口）。</summary>
    public double ActualRateHz { get; private set; }

    /// <summary>最近一次收到帧的时刻（Environment.TickCount64）。</summary>
    public long LastSeenMs { get; private set; }

    /// <summary>收到的帧数。</summary>
    public long ReceivedFrames { get; private set; }

    /// <summary>序号缺口累计（只作完整性判据）。</summary>
    public long LostFrames { get; private set; }

    /// <summary>上一帧序号。</summary>
    public uint LastSequence { get; private set; }

    /// <summary>是否已有上一帧序号。</summary>
    public bool HasSequence { get; private set; }

    /// <summary>估算历史内存。</summary>
    public long EstimatedBytes
    {
        get
        {
            long total = 0;
            foreach (var history in Histories)
            {
                total += history.EstimatedBytes;
            }

            return total;
        }
    }

    private long _rateWindowStartMs;
    private long _rateWindowFrames;

    /// <summary>调整通道数；变化时重建历史并清零数据编号。</summary>
    public void EnsureChannels(int count)
    {
        if (count == ChannelCount)
        {
            return;
        }

        ChannelCount = count;
        Histories = new ChannelHistory[count];
        for (var index = 0; index < count; index++)
        {
            Histories[index] = new ChannelHistory();
        }

        DisplayIndex = 0;
        ResetRange();
        ResetAutoY();
    }

    /// <summary>追加一帧样本（无效通道写 NaN，保持断线）。</summary>
    public void Append(ReadOnlySpan<float> samples, ulong validMask, long nowMs)
    {
        if (ChannelCount <= 0 || Histories.Length < ChannelCount)
        {
            return;
        }

        for (var index = 0; index < ChannelCount; index++)
        {
            var valid = (validMask & (1UL << index)) != 0;
            Histories[index].Append(valid ? samples[index] : float.NaN);
        }

        DisplayIndex++;
        UpdateRate(nowMs);
    }

    /// <summary>记录帧序号并累计缺口（不参与时间/位置）。</summary>
    public void ObserveSequence(uint sequence)
    {
        if (HasSequence)
        {
            var delta = unchecked(sequence - LastSequence);
            if (delta is > 1 and < 0x8000_0000)
            {
                LostFrames += delta - 1;
            }
        }

        LastSequence = sequence;
        HasSequence = true;
    }

    /// <summary>更新实测帧率（500 ms 窗口，按到达帧数）。</summary>
    public void UpdateRate(long nowMs)
    {
        if (_rateWindowStartMs == 0)
        {
            _rateWindowStartMs = nowMs;
            _rateWindowFrames = 0;
        }

        _rateWindowFrames++;
        var elapsed = nowMs - _rateWindowStartMs;
        if (elapsed < 500)
        {
            return;
        }

        ActualRateHz = _rateWindowFrames * 1000d / elapsed;
        _rateWindowFrames = 0;
        _rateWindowStartMs = nowMs;
    }

    /// <summary>长时间无帧时把实测速率归零（观察量，不是猜测）。</summary>
    public void DecayRate(long nowMs)
    {
        if (LastSeenMs != 0 && nowMs - LastSeenMs > 1000)
        {
            ActualRateHz = 0;
        }
    }

    /// <summary>标记收到帧的时刻与计数。</summary>
    public void MarkReceived(long nowMs)
    {
        LastSeenMs = nowMs;
        ReceivedFrames++;
    }

    /// <summary>清空历史与数据编号。</summary>
    public void ClearHistory()
    {
        foreach (var history in Histories)
        {
            history.Clear();
        }

        DisplayIndex = 0;
        ResetRange();
        ResetAutoY();
    }
}
