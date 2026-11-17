using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    [Fact]
    public async Task 生成快照_不可显示字符标记与方框()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var marker = new IncrementalTextView
                {
                    Width = 300,
                    Height = 110,
                    MarkerBrush = Brushes.OrangeRed,
                    Background = Brushes.Transparent,
                };
                marker.SetText("A\0B\tC\u0085D\r\nnext");

                var box = new IncrementalTextView
                {
                    Width = 300,
                    Height = 110,
                    MarkerBrush = Brushes.OrangeRed,
                    ShowControlCharacterMarkers = false,
                    Background = Brushes.Transparent,
                };
                box.SetText("A\0B\tC\u0085D\r\nnext");

                var input = new ModuleTextSurface
                {
                    Mode = TextSurfaceMode.Input,
                    Value = "probe-escape",
                    EscapeInput = true,
                    Width = 300,
                    Height = 90,
                    Text = "A\\x0C B\0C",
                };

                var multiline = new ModuleTextSurface
                {
                    Mode = TextSurfaceMode.Input,
                    Value = "probe-escape-multiline",
                    EscapeInput = true,
                    Multiline = true,
                    Width = 300,
                    Height = 100,
                    Text = "asds\\\r\n\\x0e",
                };

                var window = new Window
                {
                    Width = 340,
                    Height = 520,
                    Content = new Border
                    {
                        Padding = new Avalonia.Thickness(8),
                        Background = Brushes.White,
                        Child = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                marker,
                                box,
                                input,
                                multiline,
                            },
                        },
                    },
                };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    SnapshotTool.Save(window, "nonprintable-modes.png");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 生成快照_输入光标()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var surface = new ModuleTextSurface
                {
                    Mode = TextSurfaceMode.Input,
                    Value = "probe-caret",
                    EscapeInput = true,
                    Multiline = true,
                    Width = 300,
                    Height = 80,
                    Text = "asds\\\r\n\\x0e",
                };

                var window = new Window
                {
                    Width = 340,
                    Height = 140,
                    Content = new Border
                    {
                        Padding = new Avalonia.Thickness(8),
                        Background = Brushes.White,
                        Child = surface,
                    },
                };

                try
                {
                    window.Show();
                    var box = surface.GetVisualDescendants().OfType<TextBox>().First();
                    box.Focus();
                    box.CaretIndex = 9;
                    SnapshotTool.Pump();
                    Assert.Equal(9, box.CaretIndex);
                    SnapshotTool.Save(window, "input-caret.png");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 生成快照_原生TextBox光标对照()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var box = new TextBox
                {
                    Width = 300,
                    Height = 80,
                    Text = "asds\\\r\n\\x0e",
                };

                var window = new Window
                {
                    Width = 340,
                    Height = 140,
                    Content = new Border
                    {
                        Padding = new Avalonia.Thickness(8),
                        Background = Brushes.White,
                        Child = box,
                    },
                };

                try
                {
                    window.Show();
                    box.Focus();
                    box.CaretIndex = 9;
                    SnapshotTool.Pump();
                    SnapshotTool.Save(window, "input-caret-native.png");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}
