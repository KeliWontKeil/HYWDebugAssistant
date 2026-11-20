using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>仪表盘端点标签：用自己的精度，不跟主刻度的步长精度。</summary>
public class GaugeLabelTests
{
    [Theory]
    [InlineData(200.12, "200.12")]
    [InlineData(200, "200")]
    [InlineData(0.5, "0.5")]
    [InlineData(-3.25, "-3.25")]
    public void 端点标签保留端点的实际精度(double value, string expected) =>
        GaugeDial.FormatEndpoint(value).ShouldBe(expected);
}
