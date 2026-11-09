using System.Buffers.Binary;

namespace HYWDebugAssistant.Modules.WaveformGenerator;

/// <summary>按约定写入 280 B 波形帧（小端、定长）。</summary>
internal static class WaveformFrameWriter
{
    /// <summary>写入一帧；缓冲至少 280 B，未用槽清零。</summary>
    public static void Write(
        Span<byte> frame,
        uint groupId,
        uint sequence,
        float rateHz,
        int channelCount,
        ulong validMask,
        ReadOnlySpan<float> samples)
    {
        if (frame.Length < WaveformFrameContract.SizeInBytes)
        {
            throw new ArgumentException("frame buffer is too short", nameof(frame));
        }

        if (channelCount is < 1 or > WaveformFrameContract.MaxChannels)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        if (samples.Length < channelCount)
        {
            throw new ArgumentException("not enough samples", nameof(samples));
        }

        var target = frame[..WaveformFrameContract.SizeInBytes];
        target.Clear();

        BinaryPrimitives.WriteUInt16LittleEndian(target[0..], (ushort)channelCount);
        BinaryPrimitives.WriteUInt16LittleEndian(target[2..], (ushort)(4 * channelCount));
        BinaryPrimitives.WriteUInt32LittleEndian(target[4..], groupId);
        BinaryPrimitives.WriteUInt32LittleEndian(target[8..], sequence);
        BinaryPrimitives.WriteSingleLittleEndian(target[12..], rateHz);
        BinaryPrimitives.WriteUInt64LittleEndian(target[16..], validMask);

        for (var index = 0; index < channelCount; index++)
        {
            var enabled = (validMask & (1UL << index)) != 0;
            var value = enabled ? samples[index] : 0f;
            BinaryPrimitives.WriteSingleLittleEndian(target[(WaveformFrameContract.HeaderSize + (index * 4))..], value);
        }
    }
}
