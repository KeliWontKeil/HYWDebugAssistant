using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;
using NSubstitute;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 模块窗口的**初始尺寸**与**运行期最小尺寸**是两个独立事实：
/// 新建/还原用初始尺寸，运行期只受最小/最大约束。
/// </summary>
public class ModuleSizingTests
{
    [Fact]
    public void 初始尺寸与最小尺寸应独立保存且新建使用初始尺寸()
    {
        var module = Create();

        module.SetContentSize(
            width: 300,
            height: 200,
            initialWidth: 500,
            initialHeight: 400);

        module.EffectiveMinWidth.ShouldBe(300 + ChromeWidth);
        module.EffectiveMinHeight.ShouldBe(200 + ChromeHeight);
        module.InitialWindowWidth.ShouldBe(500 + ChromeWidth);
        module.InitialWindowHeight.ShouldBe(400 + ChromeHeight);
        module.StartupWindowWidth.ShouldBe(module.InitialWindowWidth);
        module.StartupWindowHeight.ShouldBe(module.InitialWindowHeight);
        module.Width.ShouldBe(module.StartupWindowWidth);
        module.Height.ShouldBe(module.StartupWindowHeight);
    }

    [Fact]
    public void 初始尺寸低于最小尺寸时只在新窗口取最小尺寸()
    {
        var module = Create();

        module.SetContentSize(
            width: 500,
            height: 400,
            initialWidth: 300,
            initialHeight: 200);

        module.StartupWindowWidth.ShouldBe(module.EffectiveMinWidth);
        module.StartupWindowHeight.ShouldBe(module.EffectiveMinHeight);
        module.Width.ShouldBe(module.EffectiveMinWidth);
        module.Height.ShouldBe(module.EffectiveMinHeight);
    }

    [Fact]
    public void 旧布局小于最小尺寸时必须在ApplyLayout夹紧()
    {
        var module = Create();
        module.SetContentSize(
            width: 300,
            height: 200,
            initialWidth: 500,
            initialHeight: 400);

        module.ApplyLayout(new ModuleLayoutView("m", 0, 0, 100, 100, true));

        module.Width.ShouldBe(module.EffectiveMinWidth);
        module.Height.ShouldBe(module.EffectiveMinHeight);
    }

    [Fact]
    public void 正常旧布局应保留而不被初始尺寸覆盖()
    {
        var module = Create();
        module.SetContentSize(
            width: 300,
            height: 200,
            initialWidth: 500,
            initialHeight: 400);

        module.ApplyLayout(new ModuleLayoutView("m", 0, 0, 700, 600, true));

        module.Width.ShouldBe(700);
        module.Height.ShouldBe(600);
    }

    [Fact]
    public void 设计预览不应伪造数据()
    {
        var context = new DesignModuleViewContext(
            typeof(SerialConfig),
            typeof(DesignModuleViewContext).Assembly);

        // 预览值由视图在 XAML 里声明；设计上下文只提供字段元数据，不再造样例数据。
        context.ReadSeriesWindow("series").ShouldBeNull();
        context.ReadLatest("latestIndex").ShouldBeNull();
        context.ReadLatest("rate").ShouldBeNull();
        context.ReadText("status").Lines.ShouldBeEmpty();
    }

    private const double ChromeWidth =
        (2 * ModuleSurfaceMetrics.FrameBorder) + ModuleSurfaceMetrics.InsetLeft + ModuleSurfaceMetrics.InsetRight;

    private const double ChromeHeight =
        (2 * ModuleSurfaceMetrics.FrameBorder) + ModuleSurfaceMetrics.InsetTop + ModuleSurfaceMetrics.InsetBottom;

    private static ModuleViewViewModel Create()
    {
        var view = new ModuleInstanceView(
            "m",
            "hyw/test",
            "Test",
            "Active",
            HasView: false,
            Ports: []);

        return new ModuleViewViewModel(
            view,
            layout: null,
            Substitute.For<IRuntimeQuery>(),
            Substitute.For<IRuntimeCommand>(),
            fallbackX: 0,
            fallbackY: 0);
    }
}
