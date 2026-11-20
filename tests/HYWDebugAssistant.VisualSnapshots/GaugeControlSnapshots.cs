using System;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **仪表盘小数端点截图**：端点精度与邻近主刻度避让用图看，不写像素断言。
/// </summary>
/// <remarks>
/// 这是本轮缺陷（`200.12` 退化成 `200` 并和主刻度重叠）的固定人眼场景。
/// </remarks>
[Collection("visual-snapshots")]
public class GaugeControlSnapshots
{
    /// <summary>生成 <c>controls\gauge-fractional.png</c>。</summary>
    [Fact]
    public async Task 生成快照_仪表小数端点()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var gauge = new GaugeDial
                {
                    Width = 300,
                    Height = 300,
                    Minimum = 0,
                    Maximum = 200.12,
                    MajorTickStep = 20,
                    MinorTickCount = 0,
                    Value = 42,
                    Title = "小数端点",
                    Unit = "V",
                    Padding = 0,
                    DialBrush = Brushes.DimGray,
                    TrackBrush = Brushes.LimeGreen,
                    TickBrush = Brushes.White,
                    TickLabelBrush = Brushes.White,
                    NeedleBrush = Brushes.OrangeRed,
                    HubBrush = Brushes.OrangeRed,
                    TitleBrush = Brushes.White,
                    ValueBrush = Brushes.White,
                    UnitBrush = Brushes.White,
                };

                var window = new Window
                {
                    Width = 300,
                    Height = 300,
                    Background = Brushes.Black,
                    Content = gauge,
                };

                window.Show();
                SnapshotTool.Pump();
                Thread.Sleep(50);
                SnapshotTool.Pump();
                SnapshotTool.Save(window, Path.Combine("controls", "gauge-fractional.png"));
                window.Close();
            },
            CancellationToken.None);
    }
}
