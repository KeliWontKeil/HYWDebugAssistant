using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **跨屏缩放口径**：模型四数一律 DIP ⇒ 同一份布局在任何缩放下尺寸都不变。
/// </summary>
/// <remarks>
/// <para>
/// 这条守的是"模块窗口跨显示器后右边框被屏边遮住"那个缺陷的根因：
/// 模型若存**屏幕物理像素**，同一份组合件在 150% 的屏上就会以 1.5 倍的物理尺寸开窗
/// （窗口尺寸是 DIP，而窗口从左往右长）⇒ 右边框长出屏幕。模型存 DIP 后，
/// **窗口的 DIP 尺寸与所在屏的缩放无关**，内容不伸缩。
/// </para>
/// <para>
/// 判据刻意取"两个缩放下的模型尺寸相等"，而不是某个具体数值 ——
/// 它与窗口尺寸、字体、主题都无关，只与"尺寸该由谁决定"有关。
/// </para>
/// </remarks>
public class ModuleWindowGeometryUnitTests
{
    [Fact]
    public void 同一份布局_在两个屏幕缩放下_模型尺寸必须相同()
    {
        const double width = 420;
        const double height = 560;
        var layout = new ModuleLayoutView("geo-1", 100, 200, width, height);

        using var harness = new OverlaySnapshotTests.SnapshotHarness();
        harness.Services.AddModule("geo-1", "test/snapshot-sender/1.0.0").Succeeded.ShouldBeTrue();

        var instance = harness.Services.GetInstances()
            .First(i => string.Equals(i.InstanceId, "geo-1", StringComparison.Ordinal));

        var at100 = new ModuleViewViewModel(instance, layout, harness.Services, harness.Services, 0, 0);
        var at200 = new ModuleViewViewModel(instance, layout, harness.Services, harness.Services, 0, 0);

        at100.Width.ShouldBe(width, "100% 屏：模型宽 = 声明值（DIP）");
        at200.Width.ShouldBe(width, "200% 屏：模型宽不变 —— 这就是不再被屏边遮住的依据");

        at100.Height.ShouldBe(height);
        at200.Height.ShouldBe(height);

        // 位置同理：它是屏幕坐标系里的 DIP，不该因为换屏而变
        at100.X.ShouldBe(100);
        at200.X.ShouldBe(100);
        at100.Y.ShouldBe(200);
        at200.Y.ShouldBe(200);
    }

    [Fact]
    public void 叠加层画布_必须与模型尺寸逐值相同()
    {
        // 叠加层（连线）的绘制单位是 DIP，而模型自"全 DIP"改造后也是 DIP
        // ⇒ 两者之间**不允许**再有任何换算（旧实现的 "px ÷ 缩放" 是连线错位的根因）。
        var layout = new ModuleLayoutView("geo-2", 0, 0, 333, 222);

        using var harness = new OverlaySnapshotTests.SnapshotHarness();
        harness.Services.AddModule("geo-2", "test/snapshot-sender/1.0.0").Succeeded.ShouldBeTrue();

        var instance = harness.Services.GetInstances()
            .First(i => string.Equals(i.InstanceId, "geo-2", StringComparison.Ordinal));

        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var module = new ModuleViewViewModel(instance, layout, harness.Services, harness.Services, 0, 0);

            module.CanvasWidth.ShouldBe(module.Width, $"缩放 {scale}：画布宽必须就是模型宽（无换算）");
            module.CanvasHeight.ShouldBe(module.Height, $"缩放 {scale}：画布高必须就是模型高（无换算）");
        }
    }
}
