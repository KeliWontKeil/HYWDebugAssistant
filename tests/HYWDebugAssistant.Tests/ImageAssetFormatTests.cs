using HYWDebugAssistant.Core.Persistence;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>图片资产按文件头识别真实扩展名。</summary>
public class ImageAssetFormatTests
{
    [Fact]
    public void 识别常见图片格式()
    {
        ImageAssetFormat.DetectExtension([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).ShouldBe("png");
        ImageAssetFormat.DetectExtension([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("jpg");
        ImageAssetFormat.DetectExtension("GIF89a"u8).ShouldBe("gif");
        ImageAssetFormat.DetectExtension("GIF87a"u8).ShouldBe("gif");
        ImageAssetFormat.DetectExtension([0x42, 0x4D, 0x00, 0x00]).ShouldBe("bmp");
        ImageAssetFormat.DetectExtension(
            [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50]).ShouldBe("webp");
    }

    [Fact]
    public void 未知格式_返回空()
    {
        ImageAssetFormat.DetectExtension([1, 2, 3, 4, 5, 6]).ShouldBeNull();
        ImageAssetFormat.DetectExtension([]).ShouldBeNull();
    }
}
