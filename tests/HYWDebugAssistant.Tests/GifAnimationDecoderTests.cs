using HYWDebugAssistant.Sdk.Ui.Imaging;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>GIF 解码器的算法边界（T2）：帧、延迟、透明色与损坏数据。</summary>
public class GifAnimationDecoderTests
{
    // 3×2、两帧、100/200 ms、索引 2 透明、索引 3 蓝色；Pillow 生成后作为固定夹具。
    private const string TwoFrameGif =
        "R0lGODlhAwACAIEAAP8AAAD/AAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQJCgACACwAAAAAAwACAAAICAABBABAUEBAACH5BAgUAAAALAAAAAADAAIAgQAA/wAAAAAAAAAAAAgGAAEIHBgQADs=";

    [Fact]
    public void 两帧GIF_应解析出尺寸帧数延迟与颜色()
    {
        var animation = GifAnimationDecoder.Decode(Convert.FromBase64String(TwoFrameGif));

        animation.Width.ShouldBe(3);
        animation.Height.ShouldBe(2);
        animation.Frames.Count.ShouldBe(2);
        animation.LoopCount.ShouldBe(0);
        animation.Frames[0].Delay.ShouldBe(TimeSpan.FromMilliseconds(100));
        animation.Frames[1].Delay.ShouldBe(TimeSpan.FromMilliseconds(200));

        var first = animation.Frames[0].Pixels;
        first.Length.ShouldBe(3 * 2 * 4);
        Pixel(first, 0, 0).ShouldBe([0, 0, 255, 255]); // 红：BGRA
        Pixel(first, 1, 0).ShouldBe([0, 255, 0, 255]); // 绿
        Pixel(first, 2, 1).ShouldBe([0, 0, 0, 0]);     // 透明

        var second = animation.Frames[1].Pixels;
        Pixel(second, 0, 0).ShouldBe([255, 0, 0, 255]); // 蓝
        Pixel(second, 2, 1).ShouldBe([255, 0, 0, 255]);
    }

    [Fact]
    public void 图片头_应读出PNG_JPEG_BMP_GIF尺寸()
    {
        var png = new byte[24];
        png[0] = 0x89;
        png[1] = 0x50;
        png[2] = 0x4E;
        png[3] = 0x47;
        png[4] = 0x0D;
        png[5] = 0x0A;
        png[6] = 0x1A;
        png[7] = 0x0A;
        png[12] = (byte)'I';
        png[13] = (byte)'H';
        png[14] = (byte)'D';
        png[15] = (byte)'R';
        png[19] = 64;
        png[23] = 32;

        ImageHeader.TryReadSize(png, out var pngWidth, out var pngHeight).ShouldBeTrue();
        pngWidth.ShouldBe(64);
        pngHeight.ShouldBe(32);

        var jpeg = new byte[30];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;
        jpeg[3] = 0xC0;
        jpeg[4] = 0x00;
        jpeg[5] = 0x11;
        jpeg[7] = 0x00;
        jpeg[8] = 0x48;
        jpeg[9] = 0x00;
        jpeg[10] = 0x80;

        ImageHeader.TryReadSize(jpeg, out var jpegWidth, out var jpegHeight).ShouldBeTrue();
        jpegWidth.ShouldBe(128);
        jpegHeight.ShouldBe(72);

        var bmp = new byte[26];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        bmp[18] = 100;
        bmp[22] = 50;

        ImageHeader.TryReadSize(bmp, out var bmpWidth, out var bmpHeight).ShouldBeTrue();
        bmpWidth.ShouldBe(100);
        bmpHeight.ShouldBe(50);

        var gif = new byte[10];
        gif[0] = (byte)'G';
        gif[1] = (byte)'I';
        gif[2] = (byte)'F';
        gif[6] = 3;
        gif[8] = 2;

        ImageHeader.TryReadSize(gif, out var gifWidth, out var gifHeight).ShouldBeTrue();
        gifWidth.ShouldBe(3);
        gifHeight.ShouldBe(2);
    }

    [Fact]
    public void 坏数据_应抛出InvalidDataException()
    {
        Should.Throw<InvalidDataException>(() => GifAnimationDecoder.Decode([1, 2, 3]));
    }

    [Fact]
    public void 超宽GIF_应在分配画布前拒绝()
    {
        var header = new byte[13];
        "GIF89a"u8.CopyTo(header);
        header[6] = 0x21; // 20001
        header[7] = 0x4E;
        header[8] = 0x01; // 1
        header[9] = 0x00;

        Should.Throw<InvalidDataException>(() => GifAnimationDecoder.Decode(header));
    }

    private static byte[] Pixel(byte[] pixels, int x, int y)
    {
        var index = ((y * 3) + x) * 4;
        return pixels[index..(index + 4)];
    }
}
