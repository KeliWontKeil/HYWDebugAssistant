using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.Controls;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **控件机制探针**（按需运行，不是截图判据，也不进自动化门禁）：
/// 验证“声明的高度被采用”“滚动条是细条且不展开”这两个容易被默认样式悄悄改掉的事实。
/// </summary>
[Collection("visual-snapshots")]
public class ControlMechanismProbes
{
    [Fact]
    public async Task 渐变条_点击应命中并改值()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var bar = new GradientBar
                {
                    Width = 100,
                    Height = 20,
                    Minimum = 0,
                    Maximum = 100,
                    Gradient = new SolidColorBrush(Colors.Black),
                };

                var host = new Canvas { Width = 120, Height = 40 };
                Canvas.SetLeft(bar, 10);
                Canvas.SetTop(bar, 10);
                host.Children.Add(bar);

                var window = new Window { Width = 120, Height = 40, Content = host };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    window.MouseDown(new Point(85, 20), MouseButton.Left);
                    window.MouseUp(new Point(85, 20), MouseButton.Left);
                    SnapshotTool.Pump();

                    Assert.InRange(bar.Value, 74, 76);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 渐变条_反向纵向顶部应是最小值()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var bar = new GradientBar
                {
                    Width = 20,
                    Height = 100,
                    Orientation = Orientation.Vertical,
                    Invert = true,
                    Minimum = 0,
                    Maximum = 100,
                    Gradient = new SolidColorBrush(Colors.Black),
                };

                var host = new Canvas { Width = 40, Height = 120 };
                Canvas.SetLeft(bar, 10);
                Canvas.SetTop(bar, 10);
                host.Children.Add(bar);

                var window = new Window { Width = 40, Height = 120, Content = host };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    window.MouseDown(new Point(20, 10), MouseButton.Left);
                    window.MouseUp(new Point(20, 10), MouseButton.Left);
                    SnapshotTool.Pump();
                    Assert.InRange(bar.Value, 0, 1);

                    window.MouseDown(new Point(20, 105), MouseButton.Left);
                    window.MouseUp(new Point(20, 105), MouseButton.Left);
                    SnapshotTool.Pump();
                    Assert.InRange(bar.Value, 94, 96);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 命令按钮_danger图标应与按钮文字同色()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var button = new ModuleButton
                {
                    Width = 28,
                    Height = 28,
                    Icon = "missing-icon",
                    Appearance = ModuleButtonAppearance.Danger,
                };

                var normal = new ModuleButton
                {
                    Width = 28,
                    Height = 28,
                    Icon = "missing-icon",
                };

                var accent = new ModuleButton
                {
                    Width = 28,
                    Height = 28,
                    Icon = "missing-icon",
                    Appearance = ModuleButtonAppearance.Accent,
                };

                var host = new Canvas { Width = 120, Height = 60 };
                Canvas.SetLeft(button, 10);
                Canvas.SetTop(button, 10);
                Canvas.SetLeft(normal, 45);
                Canvas.SetTop(normal, 10);
                Canvas.SetLeft(accent, 80);
                Canvas.SetTop(accent, 10);
                host.Children.Add(button);
                host.Children.Add(normal);
                host.Children.Add(accent);

                var window = new Window { Width = 120, Height = 60, Content = host };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    Assert.Equal(Colors.White, ColorOf(button));
                    Assert.Equal(Colors.White, ColorOf(accent));
                    Assert.NotEqual(ColorOf(button), ColorOf(normal));

                    Check(button);
                    Check(normal);
                    Check(accent);

                    static Color ColorOf(ModuleButton target) =>
                        ((ISolidColorBrush)target.Foreground!).Color;

                    static void Check(ModuleButton target)
                    {
                        var icon = target.GetVisualDescendants()
                            .OfType<HYWDebugAssistant.Sdk.Ui.Controls.Icon>()
                            .First();

                        var textColor = ((ISolidColorBrush)target.Foreground!).Color;
                        var iconColor = ((ISolidColorBrush)icon.Foreground!).Color;
                        Assert.Equal(textColor, iconColor);
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 命令按钮_箭头只开菜单而主体才触发命令()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var clicks = 0;
                var button = new ModuleButton
                {
                    Width = 84,
                    Height = 27,
                    Content = "发送",
                    MenuValue = "submitKey",
                };

                button.Click += (_, _) => clicks++;

                var host = new Canvas { Width = 120, Height = 60 };
                Canvas.SetLeft(button, 10);
                Canvas.SetTop(button, 10);
                host.Children.Add(button);

                var window = new Window { Width = 120, Height = 60, Content = host };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var body = button.GetVisualDescendants()
                        .OfType<Border>()
                        .Single(item => item.Name == "PART_BodyHost");
                    var menu = button.GetVisualDescendants()
                        .OfType<Border>()
                        .Single(item => item.Name == "PART_MenuHost");

                    // 右侧箭头段：只开菜单（没有上下文时会静默返回），绝不触发命令。
                    window.MouseDown(new Point(82, 23), MouseButton.Left);
                    window.MouseUp(new Point(82, 23), MouseButton.Left);
                    SnapshotTool.Pump();
                    Assert.Equal(0, clicks);
                    Assert.Contains("segment-hover", menu.Classes);
                    Assert.DoesNotContain("segment-hover", body.Classes);

                    // 主体段：仍走 Button 的点击路径。
                    window.MouseDown(new Point(40, 23), MouseButton.Left);
                    window.MouseUp(new Point(40, 23), MouseButton.Left);
                    SnapshotTool.Pump();
                    Assert.Equal(1, clicks);
                    Assert.Contains("segment-hover", body.Classes);
                    Assert.DoesNotContain("segment-hover", menu.Classes);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 调色板_明度条应与平面HSV联动且保留明度()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var dialog = new ColorPickerDialog { Color = Color.FromRgb(30, 144, 255) };

                try
                {
                    dialog.Show();
                    SnapshotTool.Pump();

                    var valueBar = dialog.FindControl<GradientBar>("ValueBar");
                    var valueShade = dialog.FindControl<Border>("ValueShade");
                    var spectrumInput = dialog.FindControl<Border>("SpectrumInput");
                    var crosshair = dialog.FindControl<Grid>("CrosshairGlyph");
                    Assert.NotNull(valueBar);
                    Assert.NotNull(valueShade);
                    Assert.NotNull(spectrumInput);
                    Assert.NotNull(crosshair);

                    valueBar!.Value = 40;
                    SnapshotTool.Pump();

                    var hsv = new HsvColor(dialog.Color);
                    Assert.InRange(hsv.V, 0.39, 0.41);
                    Assert.InRange(valueShade!.Opacity, 0.59, 0.61);

                    // 平面只改 H/S；不得把 V 重置为 1（否则就是用户看到的“条不跟着平面”）。
                    var origin = spectrumInput!.TranslatePoint(new Point(0, 0), dialog)
                        ?? throw new InvalidOperationException("平面输入层未挂到窗口");
                    var planePoint = new Point(
                        origin.X + (spectrumInput.Bounds.Width / 3),
                        origin.Y + (spectrumInput.Bounds.Height / 2));
                    dialog.MouseDown(planePoint, MouseButton.Left);
                    dialog.MouseUp(planePoint, MouseButton.Left);
                    SnapshotTool.Pump();

                    hsv = new HsvColor(dialog.Color);
                    Assert.InRange(hsv.H, 119, 121);
                    Assert.InRange(hsv.V, 0.39, 0.41);
                    Assert.InRange(valueBar.Value, 39, 41);
                    Assert.InRange(valueShade.Opacity, 0.59, 0.61);

                    var crosshairLeft = Canvas.GetLeft(crosshair!);
                    var crosshairTop = Canvas.GetTop(crosshair!);

                    // 改 V 不应移动平面选择点，包括 V=0（黑色最容易把 H/S 弄丢）。
                    valueBar.Value = 0;
                    SnapshotTool.Pump();
                    Assert.Equal(crosshairLeft, Canvas.GetLeft(crosshair));
                    Assert.Equal(crosshairTop, Canvas.GetTop(crosshair));

                    valueBar.Value = 100;
                    SnapshotTool.Pump();
                    Assert.Equal(crosshairLeft, Canvas.GetLeft(crosshair));
                    Assert.Equal(crosshairTop, Canvas.GetTop(crosshair));
                }
                finally
                {
                    dialog.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 调色板_推荐色只选中而不覆盖第一位自定义色()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var dialog = new ColorPickerDialog { Color = Colors.DodgerBlue };

                try
                {
                    dialog.Show();
                    SnapshotTool.Pump();

                    var buttons = dialog.GetVisualDescendants()
                        .OfType<Button>()
                        .Where(button => button.Classes.Contains("candidate"))
                        .ToList();
                    Assert.True(buttons.Count > 1);

                    var first = (ColorCandidate)buttons[0].DataContext!;
                    var preset = (ColorCandidate)buttons[1].DataContext!;
                    var presetColor = preset.Color;

                    buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    SnapshotTool.Pump();

                    Assert.Equal(Colors.DodgerBlue, first.Color);
                    Assert.False(first.IsSelected);
                    Assert.True(preset.IsSelected);
                    Assert.Equal(presetColor, dialog.ResultColor);

                    // 点第一位“自定义”只恢复选中，不改变颜色。
                    buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    SnapshotTool.Pump();

                    Assert.True(first.IsSelected);
                    Assert.Equal(Colors.DodgerBlue, dialog.ResultColor);

                    // 调色板输入才更新第一位，并把选中态切回第一位。
                    buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var valueBar = dialog.FindControl<GradientBar>("ValueBar");
                    Assert.NotNull(valueBar);
                    valueBar!.Value = 40;
                    SnapshotTool.Pump();

                    Assert.True(first.IsSelected);
                    Assert.False(preset.IsSelected);
                    Assert.NotEqual(Colors.DodgerBlue, first.Color);
                }
                finally
                {
                    dialog.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 调色板_RGB输入应过滤非数字并把超限值夹到255()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var dialog = new ColorPickerDialog { Color = Colors.DodgerBlue };

                try
                {
                    dialog.Show();
                    SnapshotTool.Pump();

                    var red = dialog.FindControl<TextBox>("RBox");
                    Assert.NotNull(red);

                    red!.Text = "300";
                    SnapshotTool.Pump();
                    Assert.Equal("255", red.Text);
                    Assert.Equal((byte)255, dialog.Color.R);

                    red.Text = "-5";
                    SnapshotTool.Pump();
                    Assert.Equal("5", red.Text);
                    Assert.Equal((byte)5, dialog.Color.R);

                    red.Text = "1a2";
                    SnapshotTool.Pump();
                    Assert.Equal("12", red.Text);
                    Assert.Equal((byte)12, dialog.Color.R);

                    red.Text = "12";
                    red.CaretIndex = 2;
                    red.Focus();
                    dialog.KeyTextInput("a");
                    SnapshotTool.Pump();
                    Assert.Equal("12", red.Text);
                }
                finally
                {
                    dialog.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task ModuleSelect_声明高度应生效()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var select = new ModuleSelect
                {
                    Width = 80,
                    Height = 18,
                    Options = [new ModuleSelectOption("index", "A")],
                    SelectedKey = "index",
                };

                var window = new Window { Width = 200, Height = 100, Content = select };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    Assert.InRange(select.Bounds.Height, 17, 19);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 只读文本视图_三种换行都算真实换行且哨兵不进文本()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new IncrementalTextView();
                view.SetText("a\uF000\nb\uF001\nc\uF002\nd");

                Assert.Equal(3, view.MarkCount);
                Assert.Equal("a\r\nb\rc\nd", view.Text);
                Assert.DoesNotContain("\uF000", view.Text);
                Assert.DoesNotContain("\uF001", view.Text);
                Assert.DoesNotContain("\uF002", view.Text);

                // 扁平偏移按真实换行长度计数：CRLF 占两位，CR/LF 各占一位。
                Assert.Equal(8, view.TextLength);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 只读文本视图_单独CR应换行且复制保留CR()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new IncrementalTextView();
                view.SetText("A\rB\r\nC\nD");

                Assert.Equal(3, view.MarkCount);
                Assert.Equal("A\rB\r\nC\nD", view.Text);
                Assert.Equal(8, view.TextLength);

                view.SelectAll();
                Assert.Equal("A\rB\r\nC\nD", view.SelectedText);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 文本面_输入覆盖层应区分三种换行()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var surface = new ModuleTextSurface
                {
                    Multiline = true,
                    Value = "probe-marks",
                    Width = 220,
                    Height = 100,
                };

                var window = new Window { Width = 260, Height = 160, Content = surface };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var box = surface.GetVisualDescendants().OfType<TextBox>().First();
                    box.Text = "a\r\nb\rc\nd";
                    SnapshotTool.Pump();

                    var overlay = surface.GetVisualDescendants().OfType<NewlineMarkOverlay>().First();
                    SnapshotTool.Pump();

                    Assert.Contains(overlay.Marks, mark => mark.Text == NewlineEncoding.CrLfMark);
                    Assert.Contains(overlay.Marks, mark => mark.Text == NewlineEncoding.CrMark);
                    Assert.Contains(overlay.Marks, mark => mark.Text == NewlineEncoding.LfMark);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 文本面_输入面应投影不可显示字符并保留Raw()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var surface = new ModuleTextSurface
                {
                    Multiline = true,
                    Value = "probe-projection",
                    Width = 220,
                    Height = 80,
                    Text = "A\0B",
                };

                var window = new Window { Width = 260, Height = 140, Content = surface };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var box = surface.GetVisualDescendants().OfType<TextBox>().First();
                    Assert.Equal("A⟨00⟩B", box.Text);
                    Assert.Equal("A\0B", surface.Text);

                    surface.ShowControlCharacterMarkers = false;
                    SnapshotTool.Pump();
                    Assert.Equal("A□B", box.Text);
                    Assert.Equal("A\0B", surface.Text);

                    surface.EscapeInput = true;
                    surface.Text = @"A\x0C";
                    SnapshotTool.Pump();
                    Assert.Equal(@"A\x0C", box.Text);
                    Assert.Equal(@"A\x0C", surface.Text);

                    var overlay = surface.GetVisualDescendants().OfType<NewlineMarkOverlay>().First();
                    Assert.Single(overlay.EscapeSpans!);
                    Assert.Equal(1, overlay.EscapeSpans![0].Start);
                    Assert.Equal(4, overlay.EscapeSpans![0].Length);

                    // 退格在标记右边界应删除整个不可显示字符，而不是只删一个显示字符。
                    surface.EscapeInput = false;
                    surface.ShowControlCharacterMarkers = true;
                    surface.Text = "A\0B";
                    SnapshotTool.Pump();
                    Assert.Single(overlay.MarkerSpans!);
                    Assert.Equal(1, overlay.MarkerSpans![0].Start);
                    Assert.Equal(4, overlay.MarkerSpans![0].Length);
                    SnapshotTool.Pump();
                    box.Focus();
                    box.CaretIndex = 5;
                    SnapshotTool.Pump();
                    Assert.Equal(5, box.CaretIndex);
                    window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
                    SnapshotTool.Pump();
                    Assert.Equal("AB", surface.Text);
                    Assert.Equal("AB", box.Text);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }


    [Fact]
    public async Task 设置_不可显示字符开关应安装为主题资源()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var settings = new HYWDebugAssistant.Core.Api.ApplicationSettings();
                settings.Display = settings.Display with { ShowControlCharacterMarkers = false };
                HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(settings, []);

                var app = Application.Current!;
                Assert.True(app.TryGetResource("TextControlCharacterMarkers", out var value));
                Assert.Equal(false, value);

                var textView = new IncrementalTextView();
                textView.Classes.Add("module-text-read");
                var window = new Window { Width = 200, Height = 80, Content = textView };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    Assert.False(textView.ShowControlCharacterMarkers, "DynamicResource 应立即下发到文本面");

                    HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(
                        new HYWDebugAssistant.Core.Api.ApplicationSettings(),
                        []);
                    SnapshotTool.Pump();
                    Assert.True(textView.ShowControlCharacterMarkers);
                }
                finally
                {
                    window.Close();
                }

                var surface = new ModuleTextSurface
                {
                    Value = "probe-resource",
                    Text = "A\0B",
                    Width = 200,
                    Height = 60,
                };
                var surfaceWindow = new Window { Width = 220, Height = 100, Content = surface };

                try
                {
                    HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(settings, []);
                    surfaceWindow.Show();
                    SnapshotTool.Pump();
                    var box = surface.GetVisualDescendants().OfType<TextBox>().First();
                    Assert.Equal("A□B", box.Text);

                    HYWDebugAssistant.Ui.Theming.ThemeApplier.Apply(
                        new HYWDebugAssistant.Core.Api.ApplicationSettings(),
                        []);
                    SnapshotTool.Pump();
                    Assert.Equal("A⟨00⟩B", box.Text);
                }
                finally
                {
                    surfaceWindow.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 只读文本视图_标记字符复制应使用原始字符()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new IncrementalTextView { Width = 200, Height = 60 };
                view.SetText("A\0B");

                Assert.Equal("A\0B", view.Text);
                view.SelectionStart = 1;
                view.SelectionEnd = 2;
                Assert.Equal("\0", view.SelectedText);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 文本面_多行转义应跨行标蓝()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var surface = new ModuleTextSurface
                {
                    Mode = TextSurfaceMode.Input,
                    Value = "probe-multiline-escape",
                    EscapeInput = true,
                    Multiline = true,
                    Width = 220,
                    Height = 80,
                    Text = "asds\\\r\n\\x0e",
                };
                var window = new Window { Width = 260, Height = 140, Content = surface };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    var overlay = surface.GetVisualDescendants().OfType<NewlineMarkOverlay>().First();
                    Assert.NotNull(overlay.EscapeSpans);
                    Assert.NotNull(overlay.EscapeBrush);
                    Assert.True(overlay.Bounds.Width > 0 && overlay.Bounds.Height > 0);
                    Assert.Single(overlay.EscapeSpans!);
                    var box = surface.GetVisualDescendants().OfType<TextBox>().First();
                    Assert.Same(box, overlay.Target);
                    Assert.NotEmpty(overlay.SpanMarks);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 文本面_输入编辑_复制剪切撤销使用Raw()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var box = new TextBox { Width = 200, Height = 60 };
                var editing = new ProjectedTextEditing(box);

                try
                {
                    editing.SetRaw("A\0B");
                    Assert.Equal("A⟨00⟩B", box.Text);

                    box.SelectionStart = 1;
                    box.SelectionEnd = 5;
                    Assert.Equal("\0", editing.GetSelectedRawText());

                    editing.CutSelection();
                    Assert.Equal("AB", editing.RawText);
                    Assert.Equal("AB", box.Text);

                    editing.Undo();
                    Assert.Equal("A\0B", editing.RawText);
                    Assert.Equal("A⟨00⟩B", box.Text);

                    editing.Redo();
                    Assert.Equal("AB", editing.RawText);
                }
                finally
                {
                    editing.Detach();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 文本面_回车应写入CRLF而不是原生LF()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var surface = new ModuleTextSurface
                {
                    Multiline = true,
                    Value = "probe",
                    Width = 220,
                    Height = 80,
                };

                var window = new Window { Width = 260, Height = 140, Content = surface };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var box = surface.GetVisualDescendants().OfType<TextBox>().First();
                    box.Text = "a";
                    box.CaretIndex = 1;
                    box.Focus();
                    SnapshotTool.Pump();

                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    SnapshotTool.Pump();

                    Assert.Equal("a\r\n", box.Text);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 滚动条_应常驻且不展开()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var viewer = new ScrollViewer
                {
                    Width = 120,
                    Height = 60,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Border
                    {
                        Width = 100,
                        Height = 400,
                        Background = Brushes.Transparent,
                    },
                };

                var window = new Window { Width = 200, Height = 120, Content = viewer };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var bar = viewer.GetVisualDescendants().OfType<ScrollBar>().First();
                    Assert.False(bar.AllowAutoHide);
                    Assert.True(bar.Bounds.Width < 8, "滚动条应为细条，不得展开到默认宽条");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 模块清单列表_标题不可选且模块行可显示()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var header = new ModuleCatalogListItem("header:terminal", Header: "终端");
                var module = new ModuleCatalogListItem(
                    "module:waveform-display",
                    Entry: new ModuleCatalogRowViewModel(
                        "hyw/waveform-display",
                        "波形显示器",
                        string.Empty,
                        string.Empty));

                var list = new ModuleCatalogListBox
                {
                    Width = 240,
                    Height = 120,
                    ItemsSource = new[] { header, module },
                    ItemTemplate = new FuncDataTemplate<ModuleCatalogListItem>((item, _) => new TextBlock
                    {
                        Height = 24,
                        Text = item.IsHeader ? item.Header : item.DisplayName,
                    }),
                };

                var window = new Window { Width = 260, Height = 160, Content = list };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var containers = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
                    Assert.Equal(2, containers.Count);
                    Assert.False(containers[0].IsEnabled);
                    Assert.False(containers[0].Focusable);
                    Assert.False(containers[0].IsHitTestVisible);
                    Assert.True(containers[1].IsEnabled);

                    list.SelectedItem = module;
                    SnapshotTool.Pump();
                    Assert.Same(module, list.SelectedItem);
                    Assert.NotSame(header, list.SelectedItem);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 只有横向滚动_普通滚轮应横向滚动()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Width = 600,
                    Height = 40,
                };

                for (var index = 0; index < 10; index++)
                {
                    panel.Children.Add(new Border
                    {
                        Width = 60,
                        Height = 40,
                        Background = Brushes.Transparent,
                    });
                }

                var viewer = new ScrollViewer
                {
                    Width = 120,
                    Height = 60,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = panel,
                };

                var window = new Window { Width = 200, Height = 120, Content = viewer };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    Assert.True(WheelScroll.GetHorizontalFallback(viewer), "滚轮回退没有挂到 ScrollViewer");
                    Assert.True(viewer.Extent.Width > viewer.Viewport.Width, "测试内容没有产生横向滚动范围");

                    window.MouseWheel(new Point(50, 30), new Vector(0, -1), RawInputModifiers.None);
                    SnapshotTool.Pump();

                    Assert.True(viewer.Offset.X > 0, "只有横向滚动时，普通滚轮应横向滚动");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}
