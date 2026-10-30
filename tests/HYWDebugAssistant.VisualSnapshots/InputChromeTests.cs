using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **输入控件的共享绘制**：常态框、焦点圈、错误圈、候选项底色都来自 `InputChrome.axaml`，
/// 且聚焦不改变边框厚度（文字不位移）。
/// </summary>
[Collection("visual-snapshots")]
public sealed class InputChromeTests
{
    /// <summary>TextBox：常态 1px；聚焦/错误各加一圈；禁用只改字色；只读同框。</summary>
    [Fact]
    public async Task TextBox_常态一像素_焦点与错误各一圈_边框不变()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var box = new TextBox { Width = 220, Text = "abc" };
                var window = CreateWindow(box);

                try
                {
                    var border = Part<Border>(box, "PART_BorderElement");
                    var presenter = Part<TextPresenter>(box, "PART_TextPresenter");
                    var boundsBefore = presenter.Bounds;

                    box.GetVisualDescendants()
                        .OfType<Border>()
                        .ShouldContain(part => part.Name == "PART_FocusRing", "TextBox 的共享绘制模板必须生效");

                    ColorOf(border.BorderBrush).ShouldBe(ColorOf(Resource("BorderDefault")));
                    border.BorderThickness.ShouldBe(new Thickness(1));

                    box.Focus();
                    Pump();

                    var focusRing = Part<Border>(box, "PART_FocusRing");
                    focusRing.IsVisible.ShouldBeTrue("聚焦必须显示焦点圈");
                    focusRing.BorderThickness.ShouldBe(new Thickness(2));
                    ColorOf(focusRing.BorderBrush).ShouldBe(ColorOf(Resource("AccentPrimary")));
                    border.BorderThickness.ShouldBe(new Thickness(1), "聚焦不得改变边框厚度");
                    presenter.Bounds.ShouldBe(boundsBefore, "聚焦不得让文字位移");

                    DataValidationErrors.SetError(box, new InvalidOperationException("测试错误"));
                    Pump();

                    var errorRing = Part<Border>(box, "PART_ErrorRing");
                    errorRing.IsVisible.ShouldBeTrue("错误态必须显示错误圈");
                    ColorOf(errorRing.BorderBrush).ShouldBe(ColorOf(Resource("SemanticError")));
                    border.BorderThickness.ShouldBe(new Thickness(1), "错误圈不得改变边框厚度");
                    ColorOf(border.BorderBrush).ShouldBe(ColorOf(Resource("BorderDefault")));

                    DataValidationErrors.ClearErrors(box);
                    box.IsEnabled = false;
                    Pump();
                    ColorOf(box.Foreground).ShouldBe(ColorOf(Resource("TextMuted")), "禁用只改文字");
                    border.BorderThickness.ShouldBe(new Thickness(1));
                    ColorOf(border.Background).ShouldBe(ColorOf(Resource("BgModule")), "禁用不灰底");

                    box.IsEnabled = true;
                    box.IsReadOnly = true;
                    Pump();
                    border.BorderThickness.ShouldBe(new Thickness(1));
                    ColorOf(border.BorderBrush).ShouldBe(ColorOf(Resource("BorderDefault")));
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    /// <summary>ComboBox：只有一圈（无 Fluent 的 HighlightBackground）；候选项用叠加色、不加粗。</summary>
    [Fact]
    public async Task ComboBox_聚焦只有一圈_候选项用叠加色且不加粗()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var combo = new ComboBox { Width = 220 };
                combo.Items.Add(new ComboBoxItem { Content = "甲" });
                combo.Items.Add(new ComboBoxItem { Content = "乙" });
                combo.SelectedIndex = 0;

                var window = CreateWindow(combo);

                try
                {
                    combo.Focus();
                    Pump();

                    combo.CornerRadius.ShouldBe(new CornerRadius(2), "ComboBox 的共享绘制样式必须生效");
                    combo.MinWidth.ShouldBe(0, "chrome 必须清掉下拉框的最小宽度");
                    var names = string.Join(",", combo.GetVisualDescendants().OfType<Border>().Select(border => border.Name));
                    combo.GetVisualDescendants()
                        .OfType<Border>()
                        .ShouldContain(border => border.Name == "PART_FocusRing", $"ComboBox 的共享绘制模板必须生效（borders={names}）");
                    combo.GetVisualDescendants()
                        .OfType<Border>()
                        .ShouldNotContain(border => border.Name == "HighlightBackground", "聚焦不再用 Fluent 的高亮层");
                    combo.GetVisualDescendants()
                        .OfType<Border>()
                        .ShouldContain(border => border.Name == "PART_FocusRing" && border.IsVisible, "聚焦必须显示焦点圈");

                    ColorOf(combo.Foreground).ShouldBe(ColorOf(Resource("TextPrimary")));

                    combo.IsDropDownOpen = true;
                    Pump();

                    var item = combo.Items[0].ShouldBeOfType<ComboBoxItem>();
                    item.IsSelected.ShouldBeTrue();
                    item.FontWeight.ShouldBe(FontWeight.Normal, "选中项不得加粗");

                    var part = item.GetVisualDescendants().OfType<ContentPresenter>().First();
                    var selection = Resource("OverlaySelection");
                    ColorOf(part.Background).ShouldBe(ColorOf(selection));
                    OpacityOf(part.Background).ShouldBe(OpacityOf(selection));
                    ColorOf(part.Foreground).ShouldBe(ColorOf(Resource("TextPrimary")));
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    /// <summary>`chrome-bare`：透明无边框、不显示共享模板里的焦点圈/错误圈（外层容器自画）。</summary>
    [Fact]
    public async Task 内层编辑器chrome_bare_无边框且不显示共享圈()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var box = new TextBox { Width = 200, Text = "abc" };
                box.Classes.Add("chrome-bare");
                var window = CreateWindow(box);

                try
                {
                    var border = Part<Border>(box, "PART_BorderElement");
                    border.BorderThickness.ShouldBe(new Thickness(0), "chrome-bare 不画边框");

                    box.Focus();
                    Pump();
                    Part<Border>(box, "PART_FocusRing").IsVisible.ShouldBeFalse("焦点圈由外层容器画");
                    border.BorderThickness.ShouldBe(new Thickness(0));

                    DataValidationErrors.SetError(box, new InvalidOperationException("测试错误"));
                    Pump();
                    Part<Border>(box, "PART_ErrorRing").IsVisible.ShouldBeFalse("错误圈由外层容器画");
                    border.BorderThickness.ShouldBe(new Thickness(0));
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    /// <summary>窄输入框不得被 Fluent 主题的最小宽度（64）撑出所在矩形。</summary>
    [Theory]
    [InlineData(48)]
    [InlineData(46)]
    public async Task 窄输入框不被主题最小宽度撑开(int width)
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var box = new TextBox
                {
                    Width = width,
                    TextAlignment = TextAlignment.Right,
                    Text = "1234",
                };
                var window = CreateWindow(new Border
                {
                    Width = width,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Child = box,
                });

                try
                {
                    box.MinWidth.ShouldBe(0, "chrome 必须清掉 Fluent 的最小宽度");
                    box.Bounds.Width.ShouldBeLessThanOrEqualTo(width, "输入框不得被最小宽度撑开");

                    var border = Part<Border>(box, "PART_BorderElement");
                    border.Bounds.Width.ShouldBeLessThanOrEqualTo(width, "边框必须落在输入框矩形内");

                    var presenter = Part<TextPresenter>(box, "PART_TextPresenter");
                    var padding = box.Padding;
                    var left = presenter.TranslatePoint(new Point(0, 0), border)!.Value.X;
                    var right = presenter.TranslatePoint(new Point(presenter.Bounds.Width, 0), border)!.Value.X;

                    left.ShouldBeGreaterThanOrEqualTo(padding.Left, "文本左缘不得越过内边距");
                    right.ShouldBeLessThanOrEqualTo(
                        border.Bounds.Width - padding.Right,
                        "右对齐文本不得越过边框右缘（否则末位被裁）");
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    private static Window CreateWindow(Control content)
    {
        var window = new Window { Width = 360, Height = 220, Content = content };
        window.Show();
        Pump();
        return window;
    }

    private static T Part<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static SolidColorBrush Resource(string key)
    {
        Application.Current!.Resources.TryGetResource(key, ThemeVariant.Light, out var value);
        value.ShouldNotBeNull($"缺少资源：{key}");
        return value.ShouldBeOfType<SolidColorBrush>();
    }

    private static Color ColorOf(IBrush? brush) => brush.ShouldBeOfType<SolidColorBrush>().Color;

    private static double OpacityOf(IBrush? brush) => brush.ShouldBeOfType<SolidColorBrush>().Opacity;

    private static void Pump()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
