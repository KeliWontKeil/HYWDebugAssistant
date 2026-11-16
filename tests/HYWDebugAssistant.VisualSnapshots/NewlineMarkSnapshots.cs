using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>三种换行标记的截图：CRLF=↵、CR=↓、LF=→（同色）。</summary>
[Collection("visual-snapshots")]
public class NewlineMarkSnapshots
{
    [Fact]
    public async Task 生成快照_三种换行标记()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new IncrementalTextView
                {
                    Width = 280,
                    Height = 120,
                    Background = Brushes.Transparent,
                };

                view.SetText("CRLF\uF000\nCR\uF001\nLF\uF002\nend");

                var window = new Window
                {
                    Width = 320,
                    Height = 160,
                    Content = new Border
                    {
                        Padding = new Avalonia.Thickness(8),
                        Background = Brushes.White,
                        Child = view,
                    },
                };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    SnapshotTool.Save(window, "newline-marks.png");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}
