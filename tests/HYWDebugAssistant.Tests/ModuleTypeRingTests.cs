using Avalonia.Media;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 模块清单预览图块的**类型色外圈**：取色来源、分色规则、身份判等。
/// </summary>
/// <remarks>
/// 只管"颜色/段数/身份"这些**看不见但会错**的部分（改错就与端口标记不同色、或列表每帧重画）；
/// 圈画得对不对、好不好看由截图 + 人眼判（见 `artifacts\snapshots\`）。
/// 不断言任何用户可见文案。
/// </remarks>
public class ModuleTypeRingTests
{
    [Fact]
    public void 类型色环_无类型的模块不画环()
    {
        var ring = new TypeRingViewModel([]);

        ring.HasRing.ShouldBeFalse();
        ring.SegmentCount.ShouldBe(0);
        ring.Brush.ShouldBeNull("没有类型就该保留图块自带的中性边框，而不是画成某种类型");
    }

    [Fact]
    public void 类型色环_单类型用色板的同一个颜色()
    {
        var ring = new TypeRingViewModel(["RawBytes"]);

        ring.HasRing.ShouldBeTrue();
        ring.SegmentCount.ShouldBe(1);

        var brush = (ISolidColorBrush)ring.Brush!;
        brush.Color.ShouldBe(
            PacketTypePalette.ColorFor("RawBytes"),
            "清单的环必须与端口标记的窄色条同色（同一个色板来源）");
    }

    [Fact]
    public void 类型色环_两类型按两色各占一半()
    {
        var ring = new TypeRingViewModel(["RawBytes", "WaveformFrame"]);

        ring.SegmentCount.ShouldBe(2);

        var brush = (ILinearGradientBrush)ring.Brush!;
        var stops = brush.GradientStops;

        // 每段两个停靠点、相邻段在分界线上硬切换 ⇒ 视觉上是"一色一半"，不是渐变过渡。
        stops.Count.ShouldBe(4);
        stops[0].Color.ShouldBe(PacketTypePalette.ColorFor("RawBytes"));
        stops[1].Color.ShouldBe(stops[0].Color);
        stops[2].Color.ShouldBe(PacketTypePalette.ColorFor("WaveformFrame"));
        stops[3].Color.ShouldBe(stops[2].Color);
        stops[1].Offset.ShouldBe(stops[2].Offset, "两段必须共用同一条分界线（否则中间会出现过渡带）");
    }

    [Fact]
    public void 类型色环_同一组类型判等而不同组不等()
    {
        var first = new TypeRingViewModel(["RawBytes", "WaveformFrame"]);
        var same = new TypeRingViewModel(["RawBytes", "WaveformFrame"]);
        var other = new TypeRingViewModel(["RawBytes"]);

        first.ShouldBe(same, "清单按刷新节拍重建行；同组类型必须判等，否则列表每帧换实例（悬停时整列乱闪）");
        first.GetHashCode().ShouldBe(same.GetHashCode());
        first.ShouldNotBe(other);
    }

    [Fact]
    public void 类型色环_用户改色后重新取色()
    {
        const string key = "hyw/test/ring-refresh";
        var ring = new TypeRingViewModel([key]);
        var before = ((ISolidColorBrush)ring.Brush!).Color;

        var changed = 0;
        ring.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TypeRingViewModel.Brush))
            {
                changed++;
            }
        };

        try
        {
            var custom = Color.FromRgb(0x12, 0x34, 0x56);
            PacketTypePalette.SetOverride(key, custom);

            ring.RefreshBrush();

            changed.ShouldBe(1, "改了颜色必须让绑定重新求值，否则清单一直显示旧色");
            ((ISolidColorBrush)ring.Brush!).Color.ShouldBe(custom);

            // 没改时再刷新一次：不通知（清单按刷新节拍调用，每拍重绘是不可接受的）。
            ring.RefreshBrush();
            changed.ShouldBe(1);
        }
        finally
        {
            _ = PacketTypePalette.RemoveOverride(key);
        }

        // 恢复自动配色后应回到原色（覆盖被真正移除，不是留了第二份事实）。
        ring.RefreshBrush();
        ((ISolidColorBrush)ring.Brush!).Color.ShouldBe(before);
    }
}
