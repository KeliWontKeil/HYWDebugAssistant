using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **AttitudeView 的交互探针**（按需运行，不是截图判据，也不进自动化门禁）：
/// 默认等轴测、按钮开合、选视角后立即关闭、空格/Esc 焦点域。
/// </summary>
[Collection("visual-snapshots")]
public class AttitudeViewSnapshots
{
    /// <summary>默认等轴测；按钮与空格都能开面板；选视角后立即关闭。</summary>
    [Fact]
    public async Task 姿态视图_点击与空格()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new AttitudeView { Width = 400, Height = 360 };
                var window = new Window
                {
                    Width = 400,
                    Height = 360,
                    Background = Brushes.White,
                    Content = view,
                };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    Assert.True(view.CurrentViewPreset == AttitudeViewPreset.Isometric);
                    Assert.InRange(view.ViewAngles.Yaw, 45 - 1e-6, 45 + 1e-6);
                    Assert.InRange(view.ViewAngles.Pitch, 35.264389682754654 - 1e-6, 35.264389682754654 + 1e-6);

                    window.MouseDown(new Point(18, 18), MouseButton.Left);
                    window.MouseUp(new Point(18, 18), MouseButton.Left);
                    SnapshotTool.Pump();
                    Assert.True(view.IsViewSelectorOpen);

                    var layout = AttitudeViewCubeLayout.Create(new Size(400, 360), 16);
                    var top = layout.TileRect(AttitudeViewPreset.Top);
                    var topCenter = new Point(
                        top.X + (top.Width / 2),
                        top.Y + (top.Height / 2));
                    window.MouseDown(topCenter, MouseButton.Left);
                    window.MouseUp(topCenter, MouseButton.Left);
                    SnapshotTool.Pump();

                    Assert.True(view.CurrentViewPreset == AttitudeViewPreset.Top);
                    Assert.False(view.IsViewSelectorOpen);

                    var before = view.ViewAngles;
                    window.MouseDown(new Point(360, 100), MouseButton.Left);
                    window.MouseMove(new Point(310, 120), RawInputModifiers.LeftMouseButton);
                    window.MouseUp(new Point(310, 120), MouseButton.Left);
                    SnapshotTool.Pump();

                    Assert.NotEqual(before, view.ViewAngles);
                    Assert.Null(view.CurrentViewPreset);

                    window.Activate();
                    view.Focus();
                    SnapshotTool.Pump();
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                    SnapshotTool.Pump();
                    Assert.True(view.IsViewSelectorOpen);

                    var beforePanelDrag = view.ViewAngles;
                    window.MouseDown(new Point(350, 250), MouseButton.Left);
                    window.MouseMove(new Point(300, 230), RawInputModifiers.LeftMouseButton);
                    window.MouseUp(new Point(300, 230), MouseButton.Left);
                    SnapshotTool.Pump();
                    Assert.False(view.IsViewSelectorOpen);
                    Assert.NotEqual(beforePanelDrag, view.ViewAngles);

                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                    SnapshotTool.Pump();
                    Assert.True(view.IsViewSelectorOpen);

                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    SnapshotTool.Pump();
                    Assert.False(view.IsViewSelectorOpen);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>控件不在窗口原点时，空白处拖动仍应命中（命中用控件本地坐标）。</summary>
    [Fact]
    public async Task 姿态视图_偏移摆放仍能拖动空白处()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new AttitudeView { Width = 400, Height = 360 };
                var host = new Canvas { Width = 500, Height = 420 };
                Canvas.SetLeft(view, 50);
                Canvas.SetTop(view, 30);
                host.Children.Add(view);

                var window = new Window
                {
                    Width = 500,
                    Height = 420,
                    Background = Brushes.White,
                    Content = host,
                };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var before = view.ViewAngles;
                    window.MouseDown(new Point(410, 130), MouseButton.Left);
                    window.MouseMove(new Point(360, 150), RawInputModifiers.LeftMouseButton);
                    window.MouseUp(new Point(360, 150), MouseButton.Left);
                    SnapshotTool.Pump();

                    Assert.NotEqual(before, view.ViewAngles);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>生成深色主题下面板展开的截图（<c>controls\attitude-view-dark.png</c>）。</summary>
    [Fact]
    public async Task 生成快照_姿态视角面板深色()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var app = Application.Current;
                var previous = app?.RequestedThemeVariant;
                if (app is not null)
                {
                    app.RequestedThemeVariant = ThemeVariant.Dark;
                }

                try
                {
                    var view = new AttitudeView { Width = 400, Height = 360 };
                    var window = new Window
                    {
                        Width = 400,
                        Height = 360,
                        Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
                        RequestedThemeVariant = ThemeVariant.Dark,
                        Content = view,
                    };

                    try
                    {
                        window.Show();
                        SnapshotTool.Pump();
                        view.OpenViewSelector();
                        SnapshotTool.Pump();
                        SnapshotTool.Save(window, Path.Combine("controls", "attitude-view-dark.png"));
                    }
                    finally
                    {
                        window.Close();
                    }
                }
                finally
                {
                    if (app is not null)
                    {
                        app.RequestedThemeVariant = previous;
                    }
                }
            },
            CancellationToken.None);
    }
}
