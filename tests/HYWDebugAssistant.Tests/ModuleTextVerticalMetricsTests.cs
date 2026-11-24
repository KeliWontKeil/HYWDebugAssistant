using Avalonia.Layout;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>模块文字的垂直度量：标签与读数共享一条与字体无关的光学基线。</summary>
public class ModuleTextVerticalMetricsTests
{
    // 合成度量：两种字体行盒/基线不同，但断言只比较关系（同一光学基线），不钉实测读数。
    private const double LabelLineHeight = 15;
    private const double LabelBaseline = 12;
    private const double NumericLineHeight = 13;
    private const double NumericBaseline = 10;

    [Fact]
    public void 单行居中_标签与读数落在同一条光学基线上()
    {
        const double fontSize = 11;
        const double contentHeight = 22;

        var label = ModuleTextVerticalMetrics.ComputeOffset(
            contentHeight, LabelLineHeight, LabelBaseline, fontSize, 1, VerticalAlignment.Center);
        var numeric = ModuleTextVerticalMetrics.ComputeOffset(
            contentHeight, NumericLineHeight, NumericBaseline, fontSize, 1, VerticalAlignment.Center);

        (label + LabelBaseline).ShouldBe(
            (contentHeight / 2) + (fontSize * ModuleTextVerticalMetrics.OpticalBaselineEm),
            0.001);
        (numeric + NumericBaseline).ShouldBe(label + LabelBaseline, 0.001);
    }

    [Fact]
    public void 显式顶底对齐_不受光学基线影响()
    {
        ModuleTextVerticalMetrics.ComputeOffset(
            22, LabelLineHeight, LabelBaseline, 11, 1, VerticalAlignment.Top).ShouldBe(0);
        ModuleTextVerticalMetrics.ComputeOffset(
            22, LabelLineHeight, LabelBaseline, 11, 1, VerticalAlignment.Bottom)
            .ShouldBe(22 - LabelLineHeight, 0.001);
    }

}
