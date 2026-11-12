namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>
/// 单通道动态历史：分块 f32，按需增长；追加 O(1)，不做逐帧扩容/复制。
/// </summary>
internal sealed class ChannelHistory
{
    private const int ChunkSize = 8192;
    private readonly List<float[]> _chunks = [];

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
        var chunkIndex = (int)(Count / ChunkSize);
        if (chunkIndex >= _chunks.Count)
        {
            _chunks.Add(new float[ChunkSize]);
        }

        _chunks[chunkIndex][(int)(Count % ChunkSize)] = value;
        Count++;
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

            var local = index - StartIndex;
            return _chunks[(int)(local / ChunkSize)][(int)(local % ChunkSize)];
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
        Count = 0;
        StartIndex = 0;
    }
}
