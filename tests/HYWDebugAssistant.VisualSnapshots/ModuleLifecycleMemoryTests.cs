using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.DataMonitor.Views;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **反复开关模块不得累积**：每次"加入 → 灌满 → 关窗 → 移除"之后，
/// 托管堆必须回到基线附近（不随开关次数线性上升）。
/// </summary>
/// <remarks>
/// 判据用**强制 GC 后的活对象**，不是工作集：工作集是 GC 的high-water，天然不回缩。
/// 阈值给足噪声余量（一次完整视图 + 缓冲 ≈ 数 MB；这里只容忍 16 MB 的抖动）。
/// </remarks>
[Collection("visual-snapshots")]
public class ModuleLifecycleMemoryTests
{
    [Fact]
    public async Task 反复开关满缓冲模块_活对象不得随次数累积()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "lifecycle-monitor",
                    typeof(DataMonitorModule),
                    typeof(DataMonitorViewFactory),
                    windowWidth: 400,
                    windowHeight: 368);

                var shell = new Window { Width = 900, Height = 700 };
                shell.Show();

                using var manager = new ModuleWindowManager(harness.Windows, shell);
                var line = new string('x', 80);

                long baseline = 0;

                for (var cycle = 0; cycle < 6; cycle++)
                {
                    var instanceId = $"lifecycle-{cycle}";
                    harness.AddModule(instanceId, "test/lifecycle-monitor", 400, 368);
                    manager.SyncAll();
                    Pump();

                    for (var i = 0; i < 20_000; i++)
                    {
                        harness.AppendDisplayLine(instanceId, DataMonitorModule.LogSource, line);
                    }

                    harness.Windows.Refresh();
                    Pump();

                    manager.WindowFor(instanceId)?.Close();
                    Pump();

                    harness.Services.RemoveModule(instanceId);
                    harness.Windows.Refresh();
                    manager.SyncAll();
                    Pump();

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    var live = GC.GetTotalMemory(forceFullCollection: true);
                    baseline = cycle == 0 ? live : Math.Min(baseline, live);

                    harness.Windows.Modules.Count.ShouldBe(0, "移除之后视图模型必须清空");
                    manager.AttachedWindows.Count.ShouldBe(0, "关窗之后窗口必须摘干净");
                }

                var grown = GC.GetTotalMemory(forceFullCollection: true) - baseline;
                grown.ShouldBeLessThan(
                    16L * 1024 * 1024,
                    "反复开关不应把活对象留在堆上（超过 16 MB 说明有引用没断）");

                shell.Close();
                Pump();
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
