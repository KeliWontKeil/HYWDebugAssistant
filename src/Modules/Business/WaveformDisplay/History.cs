namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>
/// 单通道动态历史：分块 f32，按需增长；追加 O(1)，不做逐帧扩容/复制。
/// </summary>
internal sealed class ChannelHistory
{
    private const int ChunkSize = 8192;
    private readonly List<float[]> _chunks = [];

    /// <summary>最旧保留样本在分块存储中的物理位置（丢弃头部后前移）。</summary>
    private long _head;

    /// <summary>有效样本数。</summary>
    public long Count { get; private set; }

    /// <summary>第一条样本对应的全局样本索引（中途出现的通道从出现位置开始）。</summary>
    public long StartIndex { get; private set; }

    /// <summary>估算占用字节（按已分配块计）。</summary>
    public long EstimatedBytes => (long)_chunks.Count * ChunkSize * sizeof(float);

    /// <summary>设置起始索引；只在尚无样本时生效。</summary>
    public void StartAt(long index)
    {
        if (Count == 0)
        {
            StartIndex = Math.Max(0, index);
        }
    }

    /// <summary>追加一个样本。</summary>
    public void Append(float value)
    {
        var position = _head + Count;
        var chunkIndex = (int)(position / ChunkSize);
        while (chunkIndex >= _chunks.Count)
        {
            _chunks.Add(new float[ChunkSize]);
        }

        _chunks[chunkIndex][(int)(position % ChunkSize)] = value;
        Count++;
    }

    /// <summary>
    /// **丢弃最旧的 N 个样本**：保留新样本，只移动读头；跨块后回收已无用的首块。
    /// </summary>
    /// <param name="count">要丢弃的样本数（超过现有数量时清空）。</param>
    public void DropOldest(long count)
    {
        if (count <= 0 || Count == 0)
        {
            return;
        }

        var take = Math.Min(count, Count);
        _head += take;
        Count -= take;
        StartIndex += take;

        while (_head >= ChunkSize)
        {
            _chunks.RemoveAt(0);
            _head -= ChunkSize;
        }
    }

    /// <summary>按历史索引读样本；越界 = NaN。</summary>
    public float this[long index]
    {
        get
        {
            if (index < StartIndex || index >= StartIndex + Count)
            {
                return float.NaN;
            }

            var position = _head + (index - StartIndex);
            return _chunks[(int)(position / ChunkSize)][(int)(position % ChunkSize)];
        }
    }

    /// <summary>读取最后 N 个样本（不足则全取）。</summary>
    /// <returns>写入的目标样本数。</returns>
    public int ReadTail(Span<float> destination)
    {
        var take = (int)Math.Min(destination.Length, Count);
        var start = Count - take;
        for (var index = 0; index < take; index++)
        {
            destination[index] = this[StartIndex + start + index];
        }

        return take;
    }

    /// <summary>清空并释放块。</summary>
    public void Clear()
    {
        _chunks.Clear();
        _head = 0;
        Count = 0;
        StartIndex = 0;
    }
}
