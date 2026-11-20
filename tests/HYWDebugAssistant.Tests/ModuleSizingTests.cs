using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Ui.Geometry;
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

        module.SetWindowSize(
            width: 402,
            height: 338,
            initialWidth: 500,
            initialHeight: 400);

        module.EffectiveMinWidth.ShouldBe(402);
        module.EffectiveMinHeight.ShouldBe(338);
        module.InitialWindowWidth.ShouldBe(500);
        module.InitialWindowHeight.ShouldBe(400);
        module.StartupWindowWidth.ShouldBe(module.InitialWindowWidth);
        module.StartupWindowHeight.ShouldBe(module.InitialWindowHeight);
        module.Width.ShouldBe(module.StartupWindowWidth);
        module.Height.ShouldBe(module.StartupWindowHeight);
    }

    [Fact]
    public void 初始尺寸低于最小尺寸时只在新窗口取最小尺寸()
    {
        var module = Create();

        module.SetWindowSize(
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
        module.SetWindowSize(
            width: 402,
            height: 338,
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
        module.SetWindowSize(
            width: 402,
            height: 338,
            initialWidth: 500,
            initialHeight: 400);

        module.ApplyLayout(new ModuleLayoutView("m", 0, 0, 700, 600, true));

        module.Width.ShouldBe(700);
        module.Height.ShouldBe(600);
    }

    [Fact]
    public void 宽高变化必须通知叠加层画布尺寸()
    {
        var module = Create();
        var changed = new List<string?>();
        module.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        module.Width = 700;
        module.Height = 600;

        changed.ShouldContain(nameof(ModuleViewViewModel.CanvasWidth));
        changed.ShouldContain(nameof(ModuleViewViewModel.CanvasHeight));
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

    /// <summary>窄模块的地板比高模块小：声明 120 宽时按 120 生效（不再被旧地板 160 夹）。</summary>
    [Fact]
    public void 声明更窄的宽度时按声明生效()
    {
        var module = Create();
        module.SetWindowSize(
            width: 120,
            height: 360,
            initialWidth: 150,
            initialHeight: 420);

        module.EffectiveMinWidth.ShouldBe(120);
        module.StartupWindowWidth.ShouldBe(150);
        module.EffectiveMinHeight.ShouldBe(360);
    }

    /// <summary>低于框架宽度地板时仍夹到地板。</summary>
    [Fact]
    public void 宽度低于框架地板时夹到地板()
    {
        var module = Create();
        module.SetWindowSize(
            width: PortMarkerMetrics.MinWindowWidth - 20,
            height: 300,
            initialWidth: 0,
            initialHeight: 0);

        module.EffectiveMinWidth.ShouldBe(PortMarkerMetrics.MinWindowWidth);
    }

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
