using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// AttitudeView 的渲染截图（人眼验收用；不是判据，也不进自动化门禁）。
/// </summary>
[Collection("visual-snapshots")]
public class AttitudeViewSnapshots
{
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
