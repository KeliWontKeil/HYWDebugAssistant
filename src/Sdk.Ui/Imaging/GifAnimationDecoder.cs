using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HYWDebugAssistant.Sdk.Ui.Imaging;

/// <summary>GIF 的一帧（BGRA 字节，行优先）。</summary>
/// <param name="Pixels">BGRA 像素字节；长度 = 宽 × 高 × 4。</param>
/// <param name="Delay">这一帧的停留时间。</param>
internal sealed record GifFrame(byte[] Pixels, TimeSpan Delay);

/// <summary>解码后的 GIF：逻辑画布尺寸、帧序列与循环次数。</summary>
/// <param name="Width">逻辑画布宽。</param>
/// <param name="Height">逻辑画布高。</param>
/// <param name="Frames">至少一帧。</param>
/// <param name="LoopCount">0 = 无限循环；正数 = 总播放轮数。</param>
internal sealed record GifAnimation(int Width, int Height, IReadOnlyList<GifFrame> Frames, int LoopCount);

/// <summary>
/// **最小 GIF 动画解码器**：只依赖 BCL，输出 BGRA 帧。
/// </summary>
/// <remarks>
/// <para>
/// 支持 GIF87a/89a 的全局/局部调色板、透明色、隔行、disposal 0–3、NETSCAPE2.0 循环次数。
/// 输出的是**合成后的完整画布**，调用方不需要理解帧偏移与 disposal。
/// </para>
/// <para>
/// 这里没有引入第三方图片库：<c>Sdk.Ui</c> 的依赖组被 SDK 打包门禁固定为
/// <c>Sdk + Avalonia</c>；当前 Avalonia 只把 GIF 解成首帧，不提供动画播放。
/// </para>
/// <para>
/// 解码上限是**硬边界**：超过单边、总像素或帧数时直接抛 <see cref="InvalidDataException"/>，
/// 不让病态文件把内存打爆。
/// </para>
/// </remarks>
internal static class GifAnimationDecoder
{
    /// <summary>单边像素上限。</summary>
    internal const int MaxEdge = 20_000;

    /// <summary>逻辑画布总像素上限。</summary>
    internal const long MaxPixels = 32_000_000;

    /// <summary>帧数上限。</summary>
    internal const int MaxFrames = 512;

    private const int MaxCode = 4096;
    private const int MaxStack = 4096;

    /// <summary>解码一个 GIF。</summary>
    /// <param name="data">完整 GIF 字节。</param>
    /// <returns>解码结果。</returns>
    /// <exception cref="InvalidDataException">不是合法 GIF、数据截断或超出上限。</exception>
    internal static GifAnimation Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13)
        {
            throw new InvalidDataException("GIF data is too short.");
        }

        if (!data[..6].SequenceEqual("GIF87a"u8) && !data[..6].SequenceEqual("GIF89a"u8))
        {
            throw new InvalidDataException("Not a GIF file.");
        }

        var width = ReadU16(data, 6);
        var height = ReadU16(data, 8);
        if (width <= 0 || height <= 0 || width > MaxEdge || height > MaxEdge)
        {
            throw new InvalidDataException("GIF canvas is outside the supported size.");
        }

        var canvasPixels = (long)width * height;
        if (canvasPixels > MaxPixels)
        {
            throw new InvalidDataException("GIF canvas has too many pixels.");
        }

        var packed = data[10];
        var backgroundIndex = data[11];
        var offset = 13;

        var globalPalette = ReadPalette(data, ref offset, packed);
        var backgroundColor = globalPalette is not null && backgroundIndex < globalPalette.Length
            ? globalPalette[backgroundIndex]
            : 0u;

        var canvas = new uint[width * height];
        var frames = new List<GifFrame>(16);
        var decodedPixels = 0L;
        var loopCount = 0;

        var pendingDisposal = 0;
        var pendingDelay = 0;
        var pendingTransparent = false;
        var pendingTransparentIndex = 0;

        var hasPrevious = false;
        var previousLeft = 0;
        var previousTop = 0;
        var previousWidth = 0;
        var previousHeight = 0;
        var previousDisposal = 0;
        uint[]? beforePrevious = null;

        while (offset < data.Length)
        {
            var marker = data[offset++];
            switch (marker)
            {
                case 0x3B: // trailer
                    offset = data.Length;
                    break;

                case 0x21: // extension
                    if (offset >= data.Length)
                    {
                        throw new InvalidDataException("Truncated GIF extension.");
                    }

                    var label = data[offset++];
                    if (label == 0xF9)
                    {
                        ReadGraphicControl(
                            data,
                            ref offset,
                            out pendingDisposal,
                            out pendingDelay,
                            out pendingTransparent,
                            out pendingTransparentIndex);
                    }
                    else if (label == 0xFF)
                    {
                        loopCount = ReadApplicationExtension(data, ref offset, loopCount);
                    }
                    else
                    {
                        SkipSubBlocks(data, ref offset);
                    }

                    break;

                case 0x2C: // image descriptor
                    if (hasPrevious)
                    {
                        ApplyDisposal(
                            canvas,
                            width,
                            previousLeft,
                            previousTop,
                            previousWidth,
                            previousHeight,
                            previousDisposal,
                            backgroundColor,
                            beforePrevious);
                    }

                    if (offset + 9 > data.Length)
                    {
                        throw new InvalidDataException("Truncated GIF image descriptor.");
                    }

                    var left = ReadU16(data, offset);
                    var top = ReadU16(data, offset + 2);
                    var frameWidth = ReadU16(data, offset + 4);
                    var frameHeight = ReadU16(data, offset + 6);
                    var framePacked = data[offset + 8];
                    offset += 9;

                    if (frameWidth <= 0 || frameHeight <= 0 || frameWidth > MaxEdge || frameHeight > MaxEdge)
                    {
                        throw new InvalidDataException("GIF frame is outside the supported size.");
                    }

                    var framePixels = (long)frameWidth * frameHeight;
                    if (framePixels > MaxPixels || decodedPixels + framePixels > MaxPixels)
                    {
                        throw new InvalidDataException("GIF animation has too many pixels.");
                    }

                    decodedPixels += framePixels;

                    var localPalette = ReadPalette(data, ref offset, framePacked);
                    var palette = localPalette ?? globalPalette
                        ?? throw new InvalidDataException("GIF has no color table.");

                    if (offset >= data.Length)
                    {
                        throw new InvalidDataException("Truncated GIF image data.");
                    }

                    var minCodeSize = data[offset++];
                    if (minCodeSize is < 2 or > 8)
                    {
                        throw new InvalidDataException("Unsupported GIF LZW code size.");
                    }

                    var compressed = ReadSubBlocks(data, ref offset);
                    var indices = new byte[frameWidth * frameHeight];
                    DecodeLzw(compressed, minCodeSize, indices);

                    var beforeFrame = pendingDisposal == 3 ? (uint[])canvas.Clone() : null;
                    var rowMap = (framePacked & 0x40) != 0 ? BuildInterlaceRows(frameHeight) : null;

                    for (var index = 0; index < indices.Length; index++)
                    {
                        var sourceRow = index / frameWidth;
                        var column = index - (sourceRow * frameWidth);
                        var row = rowMap is null ? sourceRow : rowMap[sourceRow];
                        var x = left + column;
                        var y = top + row;

                        if (x < 0 || x >= width || y < 0 || y >= height)
                        {
                            continue;
                        }

                        var paletteIndex = indices[index];
                        if (pendingTransparent && paletteIndex == pendingTransparentIndex)
                        {
                            continue;
                        }

                        canvas[(y * width) + x] = paletteIndex < palette.Length ? palette[paletteIndex] : 0u;
                    }

                    if (frames.Count >= MaxFrames)
                    {
                        throw new InvalidDataException("GIF has too many frames.");
                    }

                    frames.Add(new GifFrame(
                        MemoryMarshal.AsBytes(canvas.AsSpan()).ToArray(),
                        DelayOf(pendingDelay)));

                    hasPrevious = true;
                    previousLeft = left;
                    previousTop = top;
                    previousWidth = frameWidth;
                    previousHeight = frameHeight;
                    previousDisposal = pendingDisposal;
                    beforePrevious = beforeFrame;

                    pendingDisposal = 0;
                    pendingDelay = 0;
                    pendingTransparent = false;
                    pendingTransparentIndex = 0;
                    break;

                case 0x00: // tolerated padding between blocks
                    break;

                default:
                    throw new InvalidDataException("Unknown GIF block.");
            }
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("GIF has no frames.");
        }

        return new GifAnimation(width, height, frames, loopCount);
    }

    private static void ReadGraphicControl(
        ReadOnlySpan<byte> data,
        ref int offset,
        out int disposal,
        out int delay,
        out bool transparent,
        out int transparentIndex)
    {
        if (offset >= data.Length)
        {
            throw new InvalidDataException("Truncated GIF graphic control extension.");
        }

        var blockSize = data[offset++];
        if (blockSize < 4 || offset + blockSize > data.Length)
        {
            throw new InvalidDataException("Invalid GIF graphic control extension.");
        }

        var packed = data[offset++];
        delay = ReadU16(data, offset);
        offset += 2;
        transparentIndex = data[offset++];
        transparent = (packed & 0x01) != 0;
        disposal = (packed >> 2) & 0x07;
        offset += blockSize - 4;

        if (offset >= data.Length || data[offset++] != 0)
        {
            throw new InvalidDataException("Invalid GIF graphic control terminator.");
        }
    }

    private static int ReadApplicationExtension(ReadOnlySpan<byte> data, ref int offset, int currentLoopCount)
    {
        if (offset >= data.Length)
        {
            throw new InvalidDataException("Truncated GIF application extension.");
        }

        var blockSize = data[offset++];
        if (blockSize != 11 || offset + 11 > data.Length)
        {
            // 非 NETSCAPE 的应用扩展（XMP 等）不应让整段动画退回首帧。
            if (offset + blockSize > data.Length)
            {
                throw new InvalidDataException("Invalid GIF application extension.");
            }

            offset += blockSize;
            SkipSubBlocks(data, ref offset);
            return currentLoopCount;
        }

        var identifier = data.Slice(offset, 11);
        offset += 11;
        var payload = ReadSubBlocks(data, ref offset);

        if ((identifier.SequenceEqual("NETSCAPE2.0"u8) || identifier.SequenceEqual("ANIMEXTS1.0"u8))
            && payload.Length >= 3
            && payload[0] == 1)
        {
            return payload[1] | (payload[2] << 8);
        }

        return currentLoopCount;
    }

    private static uint[]? ReadPalette(ReadOnlySpan<byte> data, ref int offset, int packed)
    {
        if ((packed & 0x80) == 0)
        {
            return null;
        }

        var count = 1 << ((packed & 0x07) + 1);
        var byteCount = count * 3;
        if (offset + byteCount > data.Length)
        {
            throw new InvalidDataException("Truncated GIF color table.");
        }

        var palette = new uint[count];
        for (var index = 0; index < count; index++)
        {
            var source = offset + (index * 3);
            palette[index] = 0xFF000000u
                | ((uint)data[source] << 16)
                | ((uint)data[source + 1] << 8)
                | data[source + 2];
        }

        offset += byteCount;
        return palette;
    }

    private static byte[] ReadSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        var writer = new ArrayBufferWriter<byte>();
        while (true)
        {
            if (offset >= data.Length)
            {
                throw new InvalidDataException("Truncated GIF sub-block.");
            }

            var length = data[offset++];
            if (length == 0)
            {
                return writer.WrittenSpan.ToArray();
            }

            if (offset + length > data.Length)
            {
                throw new InvalidDataException("Truncated GIF sub-block payload.");
            }

            writer.Write(data.Slice(offset, length));
            offset += length;
        }
    }

    private static void SkipSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        while (true)
        {
            if (offset >= data.Length)
            {
                throw new InvalidDataException("Truncated GIF extension data.");
            }

            var length = data[offset++];
            if (length == 0)
            {
                return;
            }

            if (offset + length > data.Length)
            {
                throw new InvalidDataException("Truncated GIF extension payload.");
            }

            offset += length;
        }
    }

    private static void DecodeLzw(ReadOnlySpan<byte> compressed, int minCodeSize, byte[] output)
    {
        var clearCode = 1 << minCodeSize;
        var endCode = clearCode + 1;
        var prefix = new short[MaxCode];
        var suffix = new byte[MaxCode];
        var stack = new byte[MaxStack];

        for (var index = 0; index < clearCode; index++)
        {
            suffix[index] = (byte)index;
        }

        var codeSize = minCodeSize + 1;
        var nextCode = endCode + 1;
        var previousCode = -1;
        var firstByte = (byte)0;
        var outputIndex = 0;
        var reader = new BitReader(compressed);

        while (true)
        {
            var code = reader.ReadCode(codeSize);
            if (code < 0)
            {
                throw new InvalidDataException("Truncated GIF LZW stream.");
            }

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                nextCode = endCode + 1;
                previousCode = -1;
                continue;
            }

            if (code == endCode)
            {
                break;
            }

            if (code > nextCode)
            {
                throw new InvalidDataException("Invalid GIF LZW code.");
            }

            var stackSize = 0;
            if (code == nextCode)
            {
                if (previousCode < 0)
                {
                    throw new InvalidDataException("Invalid GIF LZW repeat code.");
                }

                stack[stackSize++] = firstByte;
                var value = previousCode;
                while (value >= clearCode)
                {
                    if (stackSize >= MaxStack)
                    {
                        throw new InvalidDataException("Invalid GIF LZW chain.");
                    }

                    stack[stackSize++] = suffix[value];
                    value = prefix[value];
                }

                stack[stackSize++] = (byte)value;
            }
            else
            {
                var value = code;
                while (value >= clearCode)
                {
                    if (stackSize >= MaxStack)
                    {
                        throw new InvalidDataException("Invalid GIF LZW chain.");
                    }

                    stack[stackSize++] = suffix[value];
                    value = prefix[value];
                }

                stack[stackSize++] = (byte)value;
            }

            firstByte = stack[stackSize - 1];
            for (var index = stackSize - 1; index >= 0; index--)
            {
                if (outputIndex >= output.Length)
                {
                    throw new InvalidDataException("GIF frame has too many pixels.");
                }

                output[outputIndex++] = stack[index];
            }

            if (previousCode >= 0 && nextCode < MaxCode)
            {
                prefix[nextCode] = (short)previousCode;
                suffix[nextCode] = firstByte;
                nextCode++;
                if (nextCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previousCode = code;

            if (outputIndex == output.Length)
            {
                break;
            }
        }

        if (outputIndex != output.Length)
        {
            throw new InvalidDataException("GIF frame has too few pixels.");
        }
    }

    private static int[] BuildInterlaceRows(int height)
    {
        var rows = new int[height];
        var index = 0;
        for (var start = 0; start < height; start += 8)
        {
            rows[index++] = start;
        }

        for (var start = 4; start < height; start += 8)
        {
            rows[index++] = start;
        }

        for (var start = 2; start < height; start += 4)
        {
            rows[index++] = start;
        }

        for (var start = 1; start < height; start += 2)
        {
            rows[index++] = start;
        }

        return rows;
    }

    private static void ApplyDisposal(
        uint[] canvas,
        int canvasWidth,
        int left,
        int top,
        int width,
        int height,
        int disposal,
        uint backgroundColor,
        uint[]? beforePrevious)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var canvasHeight = canvas.Length / canvasWidth;
        var startX = Math.Clamp(left, 0, canvasWidth);
        var startY = Math.Clamp(top, 0, canvasHeight);
        var endX = Math.Clamp(left + width, 0, canvasWidth);
        var endY = Math.Clamp(top + height, 0, canvasHeight);
        if (startX >= endX || startY >= endY)
        {
            return;
        }

        switch (disposal)
        {
            case 2:
                for (var y = startY; y < endY; y++)
                {
                    Array.Fill(canvas, backgroundColor, (y * canvasWidth) + startX, endX - startX);
                }

                break;

            case 3 when beforePrevious is not null:
                for (var y = startY; y < endY; y++)
                {
                    var sourceIndex = (y * canvasWidth) + startX;
                    Array.Copy(beforePrevious, sourceIndex, canvas, sourceIndex, endX - startX);
                }

                break;
        }
    }

    private static TimeSpan DelayOf(int centiseconds)
    {
        var milliseconds = centiseconds <= 1
            ? 100
            : Math.Clamp(centiseconds * 10, 20, 1000);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static int ReadU16(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 2 > data.Length)
        {
            throw new InvalidDataException("Truncated GIF value.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bitBuffer;
        private int _bitCount;
        private int _byteIndex;

        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bitBuffer = 0;
            _bitCount = 0;
            _byteIndex = 0;
        }

        public int ReadCode(int bits)
        {
            while (_bitCount < bits)
            {
                if (_byteIndex >= _data.Length)
                {
                    return -1;
                }

                _bitBuffer |= _data[_byteIndex++] << _bitCount;
                _bitCount += 8;
            }

            var code = _bitBuffer & ((1 << bits) - 1);
            _bitBuffer >>= bits;
            _bitCount -= bits;
            return code;
        }
    }
}
