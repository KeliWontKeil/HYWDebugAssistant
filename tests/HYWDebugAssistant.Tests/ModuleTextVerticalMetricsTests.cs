using Avalonia.Layout;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>模块文字的垂直度量：标签与读数共享一条与字体无关的光学基线。</summary>
public class ModuleTextVerticalMetricsTests
{
    // 真实行盒/基线：来自文字对齐探针的实测（FontUi 与 FontData，11px）。
    private const double LabelLineHeight = 14.668;
    private const double LabelBaseline = 11.87;
    private const double NumericLineHeight = 12.783;
    private const double NumericBaseline = 10.205;

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
    public void 单行居中_不同高度都按内容盒中心锚定()
    {
        const double fontSize = 11;

        foreach (var height in new[] { 22d, 24, 26 })
        {
            var offset = ModuleTextVerticalMetrics.ComputeOffset(
                height, LabelLineHeight, LabelBaseline, fontSize, 1, VerticalAlignment.Center);

            (offset + LabelBaseline).ShouldBe(
                (height / 2) + (fontSize * ModuleTextVerticalMetrics.OpticalBaselineEm),
                0.001);
        }
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

    [Fact]
    public void 多行_仍按整块行盒居中()
    {
        ModuleTextVerticalMetrics.ComputeOffset(
            40, 30, 12, 11, 2, VerticalAlignment.Center).ShouldBe(5, 0.001);
    }

    [Fact]
    public void 文字比内容盒高_不再二次偏移()
    {
        ModuleTextVerticalMetrics.ComputeOffset(
            12, 14.668, 11.87, 11, 1, VerticalAlignment.Center).ShouldBe(0);
        ModuleTextVerticalMetrics.ComputeOffset(
            10, 14.668, 11.87, 11, 1, VerticalAlignment.Bottom).ShouldBe(0);
    }

    [Fact]
    public void 光学偏移被钳在内容盒内()
    {
        // 基线极大：期望负偏移，钳到 0。
        ModuleTextVerticalMetrics.ComputeOffset(
            22, 14, 30, 11, 1, VerticalAlignment.Center).ShouldBe(0);

        // 基线极小：期望超过 free，钳到底。
        ModuleTextVerticalMetrics.ComputeOffset(
            22, 14, 0, 11, 1, VerticalAlignment.Center).ShouldBe(8, 0.001);
    }
}
