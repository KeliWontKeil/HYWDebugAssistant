using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HYWDebugAssistant.Modules.ScaleBarHorizontal;

/// <summary>帧读取失败原因（模块内部用；对外只给一句状态）。</summary>
internal enum WaveformFrameReadError
{
    /// <summary>没有错误。</summary>
    None = 0,

    /// <summary>长度不足一帧。</summary>
    Short = 1,

    /// <summary>通道数越界。</summary>
    ChannelCount = 2,

    /// <summary>负载字节数与通道数不一致。</summary>
    PayloadBytes = 3,

    /// <summary>组标识非法（0）。</summary>
    GroupId = 4,
}

/// <summary>只读帧视图（样本区是原 payload 的 span，不复制）。</summary>
internal readonly ref struct WaveformFrameView
{
    public WaveformFrameView(
        int channelCount,
        int payloadBytes,
        uint groupId,
        uint sequence,
        float rateHz,
        ulong validMask,
        ReadOnlySpan<float> samples)
    {
        ChannelCount = channelCount;
        PayloadBytes = payloadBytes;
        GroupId = groupId;
        Sequence = sequence;
        RateHz = rateHz;
        ValidMask = validMask;
        Samples = samples;
    }

    public int ChannelCount { get; }
    public int PayloadBytes { get; }
    public uint GroupId { get; }
    public uint Sequence { get; }
    public float RateHz { get; }
    public ulong ValidMask { get; }
    public ReadOnlySpan<float> Samples { get; }

    public bool IsValid(int index) =>
        index >= 0 && index < ChannelCount && (ValidMask & (1UL << index)) != 0;
}

/// <summary>按约定读取/校验 280 B 波形帧。</summary>
internal static class WaveformFrameReader
{
    public static bool TryRead(
        ReadOnlySpan<byte> frame,
        out WaveformFrameView view,
        out WaveformFrameReadError error)
    {
        view = default;
        error = WaveformFrameReadError.None;

        if (frame.Length < WaveformFrameContract.SizeInBytes)
        {
            error = WaveformFrameReadError.Short;
            return false;
        }

        var channelCount = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        if (channelCount is < 1 or > WaveformFrameContract.MaxChannels)
        {
            error = WaveformFrameReadError.ChannelCount;
            return false;
        }

        var payloadBytes = BinaryPrimitives.ReadUInt16LittleEndian(frame[2..]);
        if (payloadBytes != 4 * channelCount)
        {
            error = WaveformFrameReadError.PayloadBytes;
            return false;
        }

        var groupId = BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]);
        if (groupId == 0)
        {
            error = WaveformFrameReadError.GroupId;
            return false;
        }

        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]);
        var rateHz = BinaryPrimitives.ReadSingleLittleEndian(frame[12..]);
        var validMask = BinaryPrimitives.ReadUInt64LittleEndian(frame[16..]);
        var samples = MemoryMarshal.Cast<byte, float>(frame.Slice(
            WaveformFrameContract.HeaderSize,
            WaveformFrameContract.MaxChannels * sizeof(float)));

        view = new WaveformFrameView(
            channelCount,
            payloadBytes,
            groupId,
            sequence,
            rateHz,
            validMask,
            samples);
        return true;
    }
}
