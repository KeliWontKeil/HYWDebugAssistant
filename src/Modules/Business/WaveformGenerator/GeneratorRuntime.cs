using HYWDebugAssistant.Sdk.Timing;

namespace HYWDebugAssistant.Modules.WaveformGenerator;

/// <summary>一组运行期状态（定时器、序号、一次性剩余、噪声状态）。</summary>
internal sealed class GeneratorRuntime
{
    /// <summary>本组配置快照。</summary>
    public required GeneratorGroupConfig Config { get; set; }

    /// <summary>本组定时器（低频逐帧/高频时间片批量）。</summary>
    public IModuleTimer? Timer { get; set; }

    /// <summary>噪声用的确定性随机源。</summary>
    public Random Random { get; set; } = new();

    /// <summary>组内序号（仅完整性；回绕）。</summary>
    public uint Sequence { get; set; }

    /// <summary>已产生帧数（波形时间轴用）。</summary>
    public long SentFrames { get; set; }

    /// <summary>一次性剩余帧数；连续发送为 -1。</summary>
    public long Remaining { get; set; } = -1;

    /// <summary>本次一次性发送启动时冻结的计划帧数（连续发送 = 0）。</summary>
    public long PlannedFrames { get; set; }

    /// <summary>高频批量的小数欠账。</summary>
    public double Due { get; set; }

    /// <summary>本组样本复用缓冲。</summary>
    public float[] Samples { get; } = new float[WaveformFrameContract.MaxChannels];
}
