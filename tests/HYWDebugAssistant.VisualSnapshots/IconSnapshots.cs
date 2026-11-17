using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.WaveformDisplay;
using HYWDebugAssistant.Modules.WaveformGenerator;
using HYWDebugAssistant.Sdk.Ui.Controls;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>线条图标的人眼验收图（按需运行，不是判据）。</summary>
[Collection("visual-snapshots")]
public class IconSnapshots
{
    [Fact]
    public async Task 生成快照_线条图标()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 18,
                    Margin = new Avalonia.Thickness(16),
                };

                Add(panel, "加", typeof(WaveformGeneratorModule).Assembly, "icon.edit.add");
                Add(panel, "减", typeof(WaveformGeneratorModule).Assembly, "icon.edit.remove");
                Add(panel, "删", typeof(WaveformGeneratorModule).Assembly, "icon.edit.trash");
                Add(panel, "组删", typeof(WaveformDisplayModule).Assembly, "icon.group.delete");
                Add(panel, "刷新", typeof(SerialModule).Assembly, "icon.refresh");

                var window = new Window
                {
                    Width = 360,
                    Height = 110,
                    Background = Brushes.White,
                    Content = panel,
                };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    SnapshotTool.Save(window, "icon-line-style.png");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static void Add(Panel panel, string label, System.Reflection.Assembly owner, string key)
    {
        var stack = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        stack.Children.Add(new IconControl
        {
            IconKey = key,
            Owner = owner,
            Width = 28,
            Height = 28,
            Foreground = Brushes.Black,
        });

        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.Black,
        });

        panel.Children.Add(stack);
    }
}
