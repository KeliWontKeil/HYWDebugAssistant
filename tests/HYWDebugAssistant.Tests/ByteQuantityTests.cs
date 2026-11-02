using HYWDebugAssistant.Sdk.Ui.Formatting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>字节数量格式化的**退化输入**（NaN / 无穷 / 负数 ⇒ 0 B）。</summary>
/// <remarks>单位选择与小数位属**呈现**（用户可调观感），不是测试契约（AR-19 / 最小测试面）。</remarks>
public class ByteQuantityTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 退化输入应按零(double value)
    {
        ByteQuantity.Format(value).ShouldBe("0 B");
        ByteQuantity.Split(value).ShouldBe(new ByteQuantityParts("0", "B"));
    }

}
