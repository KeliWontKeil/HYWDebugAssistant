using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **"点空白 = 主动放弃焦点"**的回归网〔2026-09 用户裁定〕。
/// </summary>
/// <remarks>
/// 用户原话："鼠标必须点其他控件/窗口才能离开焦点，**点其他空白不行**" ⇒
/// 非可聚焦处（空白背景 / 标签 / 边框）点了也要释放键盘焦点；而**可聚焦控件照旧**（按钮、文本框、开关、滚动条）。
/// <para>
/// 规则只有一处（`PointerFocusRelease.Install`，`Window` 的类处理器 + 隧道阶段）⇒ 主窗口与模块窗口同时受益；
/// 本用例用**真实窗口 + 真实指针点击**验它（不是"直接调一下那个方法"——那样测不出接线）。
/// </para>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class PointerFocusReleaseTests : IClassFixture<HeadlessUiFixture>
{
    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具。</param>
    public PointerFocusReleaseTests(HeadlessUiFixture ui) => _ui = ui;

    /// <summary>点**不可聚焦的标签** ⇒ 焦点被释放（本裁定要修的就是这一条）。</summary>
    [Fact]
    public void 点击空白或标签_应释放键盘焦点() => _ui.Run(() =>
    {
        using var host = new FocusHarness();
        host.Box.Focus();
        Settle();

        host.Box.IsFocused.ShouldBeTrue("前提：焦点先真的在文本框里");

        host.ClickOn(host.Label);

        host.Box.IsFocused.ShouldBeFalse("点标签（不可聚焦）⇒ 必须离开焦点（旧行为：焦点赖着不走）");
        host.Window.FocusManager?.GetFocusedElement().ShouldBeNull("释放 = 谁都没有焦点，而不是把焦点偷偷转给别的控件");
    });

    /// <summary>点**窗口自己的空白背景** ⇒ 同样释放焦点。</summary>
    [Fact]
    public void 点击窗口空白背景_应释放键盘焦点() => _ui.Run(() =>
    {
        using var host = new FocusHarness();
        host.Box.Focus();
        Settle();

        // 窗口底部一块没有任何控件的区域（`Content` 只占上半部分）
        host.ClickOnPoint(new Point(host.Window.Bounds.Width / 2, host.Window.Bounds.Height - 6));

        host.Box.IsFocused.ShouldBeFalse("点空白背景 ⇒ 必须离开焦点");
    });

    /// <summary>
    /// 点**可聚焦控件**（文本框自己 / 按钮）⇒ 规矩不变。
    /// </summary>
    /// <remarks>
    /// 这一条是防"改过头"：若判据写成"只有按钮算可聚焦"，点文本框自己也会把焦点清掉 ⇒
    /// 用户点自己正在编辑的框会**失去焦点**（比原缺陷更糟）。故正向反向都要钉住。
    /// </remarks>
    [Fact]
    public void 点击可聚焦控件_不得释放焦点() => _ui.Run(() =>
    {
        using var host = new FocusHarness();
        host.Box.Focus();
        Settle();

        host.ClickOn(host.Box);
        host.Box.IsFocused.ShouldBeTrue("点文本框自己 ⇒ 焦点当然还在（判据必须认得出它可聚焦）");

        host.ClickOn(host.Button);
        host.Window.FocusManager?.GetFocusedElement().ShouldBeSameAs(
            host.Button, "点按钮 ⇒ 焦点正常转到按钮上（照今天的规矩，不算'点了空白'）");
    });

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>真实窗口小夹具：一个可聚焦文本框 + 一个不可聚焦标签 + 一个按钮（下半屏留白）。</summary>
    private sealed class FocusHarness : IDisposable
    {
        public FocusHarness()
        {
            Box = new TextBox { Name = "Box", Width = 120 };
            Label = new TextBlock { Name = "Label", Text = "单位：ms", Margin = new Thickness(0, 6, 0, 0) };
            Button = new Button { Name = "Button", Content = "确定", Margin = new Thickness(0, 6, 0, 0) };

            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Children = { Box, Label, Button },
            };

            Window = new Window { Width = 320, Height = 240, Content = panel };
            Window.Show();
            Settle();
        }

        public Window Window { get; }

        public TextBox Box { get; }

        public TextBlock Label { get; }

        public Button Button { get; }

        /// <summary>在某个控件中心点一下（真实指针按下 + 抬起）。</summary>
        /// <param name="control">目标控件。</param>
        public void ClickOn(Control control)
        {
            var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window);

            center.ShouldNotBeNull($"控件 {control.Name} 必须在窗口坐标系里可定位（否则点不到）");
            ClickOnPoint(center.Value);
        }

        /// <summary>在窗口坐标系里的某点按一下。</summary>
        /// <param name="point">窗口内坐标。</param>
        public void ClickOnPoint(Point point)
        {
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Settle();
        }

        public void Dispose()
        {
            Window.Close();
            Settle();
        }
    }
}
