using HYWDebugAssistant.Core.Persistence;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>组合件资产的唯一命名规则：引用即文件名，扩展名来自真实格式。</summary>
public class CompositeAssetRefTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void 归一引用_只接受assets前缀()
    {
        CompositeAssetRef.Normalize(@"assets\" + Hash + ".png").ShouldBe("assets/" + Hash + ".png");
        CompositeAssetRef.Normalize("/tmp/" + Hash + ".png").ShouldBeNull();
        CompositeAssetRef.Normalize(null).ShouldBeNull();
    }

    [Fact]
    public void 解析引用_拒绝目录穿越与非法哈希()
    {
        CompositeAssetRef.TryParse("assets/" + Hash + ".jpg", out var normalized, out var fileName)
            .ShouldBeTrue();
        normalized.ShouldBe("assets/" + Hash + ".jpg");
        fileName.ShouldBe(Hash + ".jpg");

        CompositeAssetRef.TryParse("assets/../" + Hash + ".jpg", out _, out _).ShouldBeFalse();
        CompositeAssetRef.TryParse("assets/" + Hash[..32] + ".jpg", out _, out _).ShouldBeFalse();
        CompositeAssetRef.TryParse("assets/nothex.jpg", out _, out _).ShouldBeFalse();
        CompositeAssetRef.TryParse("wallpaper/" + Hash + ".png", out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void 旧img扩展名仍可解析()
    {
        CompositeAssetRef.TryParse("assets/" + Hash + ".img", out var normalized, out _).ShouldBeTrue();
        normalized.ShouldEndWith(".img");
    }

    [Fact]
    public void 创建引用_同字节同扩展名得到同一引用()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        CompositeAssetRef.TryCreate(bytes, "png", out var first, out var firstName).ShouldBeTrue();
        CompositeAssetRef.TryCreate(bytes, "PNG", out var second, out _).ShouldBeTrue();

        first.ShouldBe(second);
        first.ShouldStartWith("assets/");
        firstName.ShouldEndWith(".png");
        CompositeAssetRef.TryParse(first, out _, out _).ShouldBeTrue();
    }

    [Fact]
    public void 创建引用_拒绝未知扩展名与空字节()
    {
        CompositeAssetRef.TryCreate([1, 2, 3], "img", out _, out _).ShouldBeFalse();
        CompositeAssetRef.TryCreate([1, 2, 3], "exe", out _, out _).ShouldBeFalse();
        CompositeAssetRef.TryCreate([], "png", out _, out _).ShouldBeFalse();
        CompositeAssetRef.TryCreate(null, "png", out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void 创建引用_jpeg归一为jpg()
    {
        CompositeAssetRef.TryCreate([0xFF, 0xD8, 0xFF, 0xE0], "jpeg", out var reference, out _)
            .ShouldBeTrue();
        reference.ShouldEndWith(".jpg");
    }
}
