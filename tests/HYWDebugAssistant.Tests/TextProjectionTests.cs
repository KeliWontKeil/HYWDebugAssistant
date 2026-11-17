using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>显示投影：不可显示字符的标记/方框与源↔显示映射。</summary>
public class TextProjectionTests
{
    [Fact]
    public void 标记模式_应只替换不可显示字符()
    {
        var projection = TextProjection.Create("A\0B", markers: true);

        projection.Display.ShouldBe("A⟨00⟩B");
        projection.HasMarkers.ShouldBeTrue();
        projection.RawLength.ShouldBe(3);
        projection.DisplayLength.ShouldBe(6);
        projection.RawToDisplay(1, preferRight: false).ShouldBe(1);
        projection.RawToDisplay(2, preferRight: false).ShouldBe(5);
        projection.DisplayToRaw(1).ShouldBe(1);
        projection.DisplayToRaw(4, preferRight: true).ShouldBe(2);
    }

    [Fact]
    public void 方框模式_每个文本簇一个方框()
    {
        var projection = TextProjection.Create("A\0B", markers: false);

        projection.Display.ShouldBe("A□B");
        projection.DisplayToRaw(2).ShouldBe(2);
        projection.RawToDisplay(2).ShouldBe(2);
    }

    [Fact]
    public void 多字节与制表符_应按UTF8字节写标记()
    {
        var projection = TextProjection.Create("A\t\u0085B", markers: true);

        projection.Display.ShouldBe("A⟨09⟩⟨C2\u00A085⟩B");
    }

    [Fact]
    public void 普通正文里的尖括号不应被当成标记()
    {
        var projection = TextProjection.Create("A⟨0C⟩B", markers: true);

        projection.Display.ShouldBe("A⟨0C⟩B");
        projection.HasMarkers.ShouldBeFalse();
    }

    [Fact]
    public void 换行不参与占位()
    {
        var projection = TextProjection.Create("A\r\nB", markers: true);

        projection.Display.ShouldBe("A\r\nB");
        projection.HasMarkers.ShouldBeFalse();
    }

    [Fact]
    public void 选区映射_覆盖标记时应扩展到整段()
    {
        var projection = TextProjection.Create("A\0B", markers: true);

        var (start, length) = projection.RawRangeToDisplay(1, 2);
        start.ShouldBe(1);
        length.ShouldBe(4);
        projection.Display.Substring(start, length).ShouldBe("⟨00⟩");

        projection.DisplayRangeToRaw(2, 3).ShouldBe((1, 2));
    }

    [Fact]
    public void emoji组合簇应保持字面()
    {
        // 家庭 emoji：👨 + ZWJ + 👩 + ZWJ + 👧
        const string emoji = "👨\u200D👩\u200D👧";
        var projection = TextProjection.Create(emoji, markers: true);

        projection.Display.ShouldBe(emoji);
        projection.HasMarkers.ShouldBeFalse();
    }
}
