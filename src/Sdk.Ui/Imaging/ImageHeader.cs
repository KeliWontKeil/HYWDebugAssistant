using System.Buffers.Binary;

namespace HYWDebugAssistant.Sdk.Ui.Imaging;

/// <summary>
/// **图片头尺寸读取**：只读文件头，给静态图解码提供原始宽高。
/// </summary>
/// <remarks>
/// Avalonia 的 <c>Bitmap.DecodeToWidth</c> 会按请求宽度缩放；为了不把小图放大，
/// 控件先从这里读原始宽度，再按原宽解码。支持的格式与图片面前端一致：
/// PNG / JPEG / BMP / GIF / WebP。
/// </remarks>
internal static class ImageHeader
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>读取图片宽高。</summary>
    /// <param name="data">完整文件字节（至少包含文件头）。</param>
    /// <param name="width">宽。</param>
    /// <param name="height">高。</param>
    /// <returns>能否识别并读出有效尺寸。</returns>
    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length >= 24
            && data[..8].SequenceEqual(PngSignature)
            && data[12] == (byte)'I'
            && data[13] == (byte)'H'
            && data[14] == (byte)'D'
            && data[15] == (byte)'R')
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
            return width > 0 && height > 0;
        }

        if (data.Length >= 10
            && data[0] == (byte)'G'
            && data[1] == (byte)'I'
            && data[2] == (byte)'F')
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
            return width > 0 && height > 0;
        }

        if (data.Length >= 26
            && data[0] == (byte)'B'
            && data[1] == (byte)'M')
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4));
            var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4));
            height = rawHeight == int.MinValue ? 0 : Math.Abs(rawHeight);
            return width > 0 && height > 0;
        }

        if (data.Length >= 30
            && data[0] == (byte)'R'
            && data[1] == (byte)'I'
            && data[2] == (byte)'F'
            && data[3] == (byte)'F'
            && data[8] == (byte)'W'
            && data[9] == (byte)'E'
            && data[10] == (byte)'B'
            && data[11] == (byte)'P')
        {
            return TryReadWebp(data, out width, out height);
        }

        return TryReadJpeg(data, out width, out height);
    }

    private static bool TryReadWebp(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 30)
        {
            return false;
        }

        var chunk = data.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = ReadUInt24LittleEndian(data.Slice(24, 3)) + 1;
            height = ReadUInt24LittleEndian(data.Slice(27, 3)) + 1;
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (data.Length < 25 || data[20] != 0x2F)
            {
                return false;
            }

            var packed = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
            width = (int)(packed & 0x3FFF) + 1;
            height = (int)((packed >> 14) & 0x3FFF) + 1;
            return true;
        }

        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (data.Length < 30
                || data[23] != 0x9D
                || data[24] != 0x01
                || data[25] != 0x2A)
            {
                return false;
            }

            width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF;
            return width > 0 && height > 0;
        }

        return false;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            while (offset < data.Length && data[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                return false;
            }

            var marker = data[offset++];
            if (marker == 0xD9 || marker == 0xDA)
            {
                return false;
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (length < 2 || offset + length > data.Length)
            {
                return false;
            }

            if (marker is >= 0xC0 and <= 0xCF
                && marker is not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 7)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                return width > 0 && height > 0;
            }

            offset += length;
        }

        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> data) =>
        data[0] | (data[1] << 8) | (data[2] << 16);
}
