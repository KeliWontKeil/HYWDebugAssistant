namespace HYWDebugAssistant.Modules.ScaleBarHorizontal;

/// <summary>一个被发现的数据组（运行期状态，不持久化）。</summary>
internal sealed class ChannelGroupState
{
    private readonly bool[] _hasSample = new bool[WaveformFrameContract.MaxChannels];

    /// <summary>造一个组状态。</summary>
    /// <param name="groupId">组标识。</param>
    public ChannelGroupState(uint groupId) => GroupId = groupId;

    /// <summary>组标识（帧头给出）。</summary>
    public uint GroupId { get; }

    /// <summary>最近一帧声明的通道数。</summary>
    public int ChannelCount { get; set; }

    /// <summary>最近一帧声明的速率（Hz；0 = 未知）。</summary>
    public float RateHz { get; set; }

    /// <summary>最近一帧的有效通道掩码。</summary>
    public ulong ValidMask { get; set; }

    /// <summary>最近一次收到该组帧的时刻（模块时钟，毫秒）。</summary>
    public long LastFrameTick { get; set; }

    /// <summary>最近一帧的序号。</summary>
    public uint LastSequence { get; set; }

    /// <summary>各通道最后一次有效样本。</summary>
    public float[] Samples { get; } = new float[WaveformFrameContract.MaxChannels];

    /// <summary>各通道最后一次有效样本的时刻（毫秒）。</summary>
    public long[] SampleTicks { get; } = new long[WaveformFrameContract.MaxChannels];

    /// <summary>某个通道是否收到过有效样本。</summary>
    /// <param name="channelIndex">通道序号。</param>
    public bool HasSample(int channelIndex) =>
        channelIndex >= 0 && channelIndex < _hasSample.Length && _hasSample[channelIndex];

    /// <summary>记录一次有效样本。</summary>
    /// <param name="channelIndex">通道序号。</param>
    /// <param name="value">样本值。</param>
    /// <param name="nowMs">时刻（模块时钟）。</param>
    public void SetSample(int channelIndex, float value, long nowMs)
    {
        if (channelIndex < 0 || channelIndex >= Samples.Length)
        {
            return;
        }

        Samples[channelIndex] = value;
        SampleTicks[channelIndex] = nowMs;
        _hasSample[channelIndex] = true;
    }
}

/// <summary>一帧的处理结论。</summary>
[Flags]
internal enum FrameApplyResult
{
    /// <summary>没有变化。</summary>
    None = 0,

    /// <summary>帧被接受。</summary>
    Accepted = 1,

    /// <summary>帧格式不正确，已丢弃。</summary>
    InvalidFrame = 2,

    /// <summary>组或通道数有变化。</summary>
    StructureChanged = 4,
}

/// <summary>
/// **组/通道运行期状态 + 目标值解析**：只做数据事实，不做显示决定。
/// </summary>
/// <remarks>
/// <para>
/// 通道号是**用户输入的事实**（模块的值），这里不做"钳制到合法范围"；通道不存在就按
/// "没有样本"处理，目标值走同一条规则：新鲜给样本；过期按自动回零给 0 或保持；无样本给 0。
/// </para>
/// <para>
/// 时间由调用方以毫秒传入（模块时钟），因此这里的判定可在测试里用显式时间驱动。
/// </para>
/// </remarks>
internal sealed class ChannelFrameFeed
{
    private readonly Dictionary<uint, ChannelGroupState> _groups = [];
    private readonly List<uint> _order = [];

    /// <summary>结构变更号（组集合/通道数变化时递增）。</summary>
    public long StructureRevision { get; private set; }

    /// <summary>已发现的组（发现顺序）。</summary>
    public IReadOnlyList<uint> GroupIds => _order;

    /// <summary>第一个被发现的组（0 = 还没有任何组）。</summary>
    public uint FirstGroupId => _order.Count > 0 ? _order[0] : 0;

    /// <summary>取某个组的状态（未发现 ⇒ <see langword="null"/>）。</summary>
    /// <param name="groupId">组标识。</param>
    public ChannelGroupState? Group(uint groupId) =>
        groupId != 0 && _groups.TryGetValue(groupId, out var group) ? group : null;

    /// <summary>处理一帧；返回合法性/结构结论。</summary>
    /// <param name="payload">帧负载。</param>
    /// <param name="nowMs">收帧时刻（模块时钟，毫秒）。</param>
    public FrameApplyResult ApplyFrame(ReadOnlySpan<byte> payload, long nowMs)
    {
        if (!WaveformFrameReader.TryRead(payload, out var frame, out _))
        {
            return FrameApplyResult.InvalidFrame;
        }

        var result = FrameApplyResult.Accepted;

        if (!_groups.TryGetValue(frame.GroupId, out var group))
        {
            group = new ChannelGroupState(frame.GroupId);
            _groups[frame.GroupId] = group;
            _order.Add(frame.GroupId);
            StructureRevision++;
            result |= FrameApplyResult.StructureChanged;
        }
        else if (group.ChannelCount != frame.ChannelCount)
        {
            StructureRevision++;
            result |= FrameApplyResult.StructureChanged;
        }

        group.ChannelCount = frame.ChannelCount;
        group.ValidMask = frame.ValidMask;
        group.LastSequence = frame.Sequence;
        group.LastFrameTick = nowMs;
        if (float.IsFinite(frame.RateHz) && frame.RateHz > 0)
        {
            group.RateHz = frame.RateHz;
        }

        for (var index = 0; index < frame.ChannelCount; index++)
        {
            if (frame.IsValid(index))
            {
                group.SetSample(index, frame.Samples[index], nowMs);
            }
        }

        return result;
    }

    /// <summary>某个通道是否在该组当前声明的范围内。</summary>
    /// <param name="groupId">组标识。</param>
    /// <param name="channelIndex">通道序号（0 起）。</param>
    public bool HasChannel(uint groupId, int channelIndex)
    {
        var group = Group(groupId);
        return group is not null && channelIndex >= 0 && channelIndex < group.ChannelCount;
    }

    /// <summary>该通道在最近一帧里是否被标为有效。</summary>
    /// <param name="groupId">组标识。</param>
    /// <param name="channelIndex">通道序号（0 起）。</param>
    public bool ChannelValid(uint groupId, int channelIndex)
    {
        var group = Group(groupId);
        return group is not null
            && channelIndex >= 0
            && channelIndex < group.ChannelCount
            && (group.ValidMask & (1UL << channelIndex)) != 0;
    }

    /// <summary>
    /// **目标值的唯一解析**：新鲜 → 样本；过期 → 自动回零时给 0、否则保持最后样本；
    /// 没有样本（含通道不存在）时自然给 0。
    /// </summary>
    /// <param name="groupId">组标识。</param>
    /// <param name="channelIndex">通道序号（0 起）。</param>
    /// <param name="nowMs">当前时刻（模块时钟）。</param>
    /// <param name="autoZero">是否自动回零。</param>
    /// <param name="delayMs">回零延时（毫秒）。</param>
    public double Resolve(uint groupId, int channelIndex, long nowMs, bool autoZero, int delayMs)
    {
        var group = Group(groupId);
        if (group is null || !group.HasSample(channelIndex))
        {
            return 0;
        }

        var held = group.Samples[channelIndex];
        if (nowMs - group.SampleTicks[channelIndex] <= Math.Max(0, delayMs))
        {
            return held;
        }

        return autoZero ? 0 : held;
    }
}
