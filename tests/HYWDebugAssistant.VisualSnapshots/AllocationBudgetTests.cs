using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Headless;
using Avalonia.Threading;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.DataMonitor.Views;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **分配回归**：1 ms × 1 KB 的发送/显示链路上，稳态分配速率不得超过上限。
/// </summary>
/// <remarks>
/// 这不是"性能基准"（数值随机器/负载浮动），而是**分配行为的回归网**：
/// 逐字符拆包、每包一个 StringBuilder、每视觉行一个 Substring 这类改动都会让速率成倍上升。
/// 阈值给到当前实测值的 2 倍左右，只在行为退回去时红。
/// </remarks>
[Collection("visual-snapshots")]
public class AllocationBudgetTests
{
    [Theory]
    [InlineData("sender-nw", 12.0)]
    [InlineData("monitor", 35.0)]
    [InlineData("both", 40.0)]
    public async Task 一毫秒发送与显示的稳态分配不得超过上限(string mode, double limitMbPerSecond)
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "alloc-sender",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);
                harness.Register(
                    "alloc-monitor",
                    typeof(DataMonitorModule),
                    typeof(DataMonitorViewFactory),
                    windowWidth: 400,
                    windowHeight: 368);

                var withSender = mode is "sender-nw" or "both";
                var withMonitor = mode is "monitor" or "both";
                var senderWindowOpen = mode == "both";

                if (withSender)
                {
                    harness.Services.AddModule("s1", "test/alloc-sender").Succeeded.ShouldBeTrue();
                    harness.ViewState.SetSelectedPage("s1", "text-pages", 0);
                    harness.ViewState.SetInputText("s1", "text1", new string('x', 1000));
                    harness.ViewState.SetInputText("s1", "interval", "1");
                }

                if (withMonitor)
                {
                    harness.Services.AddModule("m1", "test/alloc-monitor").Succeeded.ShouldBeTrue();
                }

                if (mode == "both")
                {
                    harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
                    harness.Services.UpdatePortNetwork("m1", "in", "bus").Succeeded.ShouldBeTrue();
                }

                harness.Windows.Refresh();
                Pump();

                if (withMonitor)
                {
                    var monitor = harness.Windows.Modules.Single(item => item.InstanceId == "m1");
                    var monitorWindow = harness.OpenWindow(monitor);
                    Pump();

                    if (mode == "monitor")
                    {
                        var packet = new string('x', 1000);
                        Measure(() =>
                        {
                            for (var i = 0; i < 16; i++)
                            {
                                harness.AppendDisplayLine("m1", DataMonitorModule.LogSource, packet);
                            }

                            harness.Windows.Refresh();
                            Pump();
                        }, limitMbPerSecond, mode);

                        monitorWindow.Close();
                        Pump();
                        return;
                    }

                    var sender = harness.Windows.Modules.Single(item => item.InstanceId == "s1");
                    var senderWindow = senderWindowOpen ? harness.OpenWindow(sender) : null;
                    Pump();
                    harness.ViewState.SetInputText("s1", "continuous", "true");

                    Measure(() =>
                    {
                        harness.Windows.Refresh();
                        Pump();
                    }, limitMbPerSecond, mode);

                    harness.ViewState.SetInputText("s1", "continuous", "false");
                    senderWindow?.Close();
                    monitorWindow.Close();
                    Pump();
                    return;
                }

                harness.ViewState.SetInputText("s1", "continuous", "true");

                Measure(() =>
                {
                    harness.Windows.Refresh();
                    Pump();
                }, limitMbPerSecond, mode);

                harness.ViewState.SetInputText("s1", "continuous", "false");
                Pump();
            },
            CancellationToken.None);
    }

    private static void Measure(Action frame, double limitMbPerSecond, string mode)
    {
        // warmup：让 JIT / 池 / 首次布局都过去
        for (var i = 0; i < 60; i++)
        {
            frame();
            Thread.Sleep(16);
        }

        var startAllocated = GC.GetTotalAllocatedBytes();
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < 180; i++)
        {
            frame();
            Thread.Sleep(16);
        }

        watch.Stop();

        var allocated = GC.GetTotalAllocatedBytes() - startAllocated;
        var mbPerSecond = allocated / watch.Elapsed.TotalSeconds / 1024.0 / 1024.0;

        Console.WriteLine($"[alloc:{mode}] {mbPerSecond:F2} MB/s (limit {limitMbPerSecond:F1})");
        mbPerSecond.ShouldBeLessThan(
            limitMbPerSecond,
            $"{mode} 的稳态分配速率应在 {limitMbPerSecond:F1} MB/s 以内（实测 {mbPerSecond:F2}）");
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
