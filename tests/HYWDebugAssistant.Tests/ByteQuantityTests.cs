using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Ui.Formatting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>字节数量格式化（B/KB/MB/GB、可选每秒）的边界与回归。</summary>
public class ByteQuantityTests
{
    [Theory]
    [InlineData(0, "0", "B")]
    [InlineData(1, "1", "B")]
    [InlineData(999, "999", "B")]
    [InlineData(1023, "1023", "B")]
    [InlineData(1024, "1.0", "KB")]
    [InlineData(1025, "1.0", "KB")]
    [InlineData(10 * 1024, "10.0", "KB")]
    [InlineData(9.25 * 1024, "9.25", "KB")]
    [InlineData(1024 * 1024, "1.0", "MB")]
    [InlineData((1024 * 1024) + 1, "1.0", "MB")]
    [InlineData(1024d * 1024 * 1024, "1.0", "GB")]
    public void 边界应按规则给出值与单位(double value, string expectedValue, string expectedUnit)
    {
        var parts = ByteQuantity.Split(value);

        parts.Value.ShouldBe(expectedValue);
        parts.Unit.ShouldBe(expectedUnit);
    }

    [Fact]
    public void 每秒单位应带后缀()
    {
        ByteQuantity.Format(1024, perSecond: true).ShouldBe("1.0 KB/s");
        ByteQuantity.Format(512, perSecond: true).ShouldBe("512 B/s");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 退化输入应按零(double value)
    {
        ByteQuantity.Format(value).ShouldBe("0 B");
        ByteQuantity.Split(value).ShouldBe(new ByteQuantityParts("0", "B"));
    }

    /// <summary>与旧实现逐字一致的取样点（Topology 文本）。</summary>
    [Theory]
    [InlineData(0, "—")]
    [InlineData(512, "512 B/s")]
    [InlineData(1024, "1.0 KB/s")]
    [InlineData(1536, "1.5 KB/s")]
    public void 拓扑速率文本应与旧实现一致(double value, string expected)
    {
        TopologyDisplay.FormatRate(value).ShouldBe(expected);
    }
}
