using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Modules.HexStringConverter.Views;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **转换器上下两半的对应区必须同高**：页签内容的高度只由 TabControl 的行分配决定，
/// 不能因为上下两半的内容不同而各自按 desired height 长高。
/// </summary>
public sealed class ConverterLayoutTests
{
    [Fact]
    public async Task 转换器_不同窗口高度上下对应文本面等高()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                double[] heights = [300.0, 330.0, 372.0, 450.0, 650.0];

                foreach (var height in heights)
                {
                    var view = new HexStringConverterView();
                    var window = new Window
                    {
                        Width = 400,
                        Height = height,
                        Content = new Border { Margin = new Thickness(20, 22, 20, 24), Child = view },
                    };

                    window.Show();
                    Pump();

                    var surfaces = view.GetVisualDescendants()
                        .OfType<ModuleTextSurface>()
                        .Where(surface => surface.Bounds.Width > 0)
                        .ToDictionary(surface => surface.Source, StringComparer.Ordinal);

                    var charInput = surfaces["char1"];
                    var hexInput = surfaces["hex1"];
                    var hexResult = surfaces["hexResult"];
                    var charResult = surfaces["charResult"];

                    charInput.Bounds.Height.ShouldBe(
                        hexInput.Bounds.Height,
                        0.01,
                        "上下两个输入框必须同高");
                    hexResult.Bounds.Height.ShouldBe(
                        charResult.Bounds.Height,
                        0.01,
                        "上下两个结果区必须同高");
                    charInput.Bounds.Height.ShouldBe(
                        hexResult.Bounds.Height,
                        1.0,
                        $"输入内容区与结果内容区必须像素级同高：input={charInput.Bounds.Height:F1} result={hexResult.Bounds.Height:F1}");
                    hexInput.Bounds.Height.ShouldBe(
                        charResult.Bounds.Height,
                        1.0,
                        "下半输入内容区与下半结果内容区必须像素级同高");
                    charInput.Bounds.Height.ShouldBeGreaterThan(0);
                    hexResult.Bounds.Height.ShouldBeGreaterThan(0);

                    foreach (var surface in surfaces.Values)
                    {
                        var bottom = surface.TranslatePoint(new Point(0, surface.Bounds.Height), view);
                        bottom.ShouldNotBeNull();
                        bottom!.Value.Y.ShouldBeLessThanOrEqualTo(
                            view.Bounds.Height + 0.01,
                            $"{surface.Source} 不能溢出内容区");
                    }

                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}





