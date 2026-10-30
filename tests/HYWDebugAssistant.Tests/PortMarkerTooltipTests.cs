using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 口标记的**队列压力一行**：只在有队列压力时出现在悬停里；标记本体不变。
/// </summary>
public class PortMarkerTooltipTests
{
    [Fact]
    public void 悬停_有队列压力时多一行_无队列时不多行()
    {
        var marker = new PortMarkerViewModel("m1", "in", isSend: false, label: "bus", offset: 0.5, typeText: "f32")
        {
            IsConnected = true,
            NetworkName = "bus",
        };

        var withoutQueue = marker.Tooltip;

        marker.QueueText = "队列 3/16 · 已丢 2（饱和）";

        marker.Tooltip.Contains("队列 3/16", StringComparison.Ordinal)
            .ShouldBeTrue("有压力时悬停里应出现队列一行");
        marker.Tooltip.Replace(Environment.NewLine + "队列 3/16 · 已丢 2（饱和）", string.Empty, StringComparison.Ordinal)
            .ShouldBe(withoutQueue, "只是**多一行**，既有内容一字不改");

        marker.QueueText = null;

        marker.Tooltip.ShouldBe(withoutQueue, "队列压力消失后，悬停回到原样");
    }
}
