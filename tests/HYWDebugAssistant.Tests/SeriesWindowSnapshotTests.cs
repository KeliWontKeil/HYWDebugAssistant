using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>通用序列窗口快照源：整段替换、跨线程读取面、实例移除清理。</summary>
public class SeriesWindowSnapshotTests
{
    [Fact]
    public void 序列窗口快照_应整段替换并在移除实例后清空()
    {
        var store = new ViewStateStore();
        var first = new SeriesWindowSnapshot(
            1,
            0,
            3,
            3,
            1000,
            -1,
            1,
            true,
            [new SeriesWindowChannel(0, "CH1", null, [1f, 2f, 3f, 4f])]);

        store.ReadSeriesWindow("m1", "series").ShouldBeNull();
        store.DisplayFor("m1").SetSeriesWindow("series", first);
        store.ReadSeriesWindow("m1", "series").ShouldBeSameAs(first);

        var second = first with { Version = 2, XRangeStart = 1, XRangeEnd = 2 };
        store.DisplayFor("m1").SetSeriesWindow("series", second);
        store.ReadSeriesWindow("m1", "series").ShouldBeSameAs(second);

        store.RemoveInstance("m1");
        store.ReadSeriesWindow("m1", "series").ShouldBeNull();
    }
}
