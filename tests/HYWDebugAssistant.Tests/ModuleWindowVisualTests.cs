using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;


namespace HYWDebugAssistant.Tests;

/// <summary>
/// 无头 UI 测试**专用集合**（禁止与其他测试并行）。
/// </summary>
/// <remarks>
/// 无头会话会在自己的线程上创建 `Application`；若与其他测试类并行，会出现
/// "The calling thread cannot access this object because a different thread owns it"（实测踩过）。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaUiGroup
{
    /// <summary>集合名（xunit 的 Collection 约定要求定义类名不以 Collection 结尾，CA1711）。</summary>
    public const string Name = "AvaloniaUi";
}

/// <summary>
/// **视图层兜底测试**【M1-16 / ADR-0039 · 用户裁决 D-9】：
/// 在无头会话里构造**真实窗口** → 跑真实布局 → 断言"标记可见、尺寸正确、命中落在标记上"。
/// </summary>
/// <remarks>
/// <para><b>立项原因</b>（两次真实缺陷都没有任何自动化能拦住，而且是用户先撞上的）：
/// ① 口标记被 6 px 拖动带与 12×12 边中点手柄压住 → 双击改标签无效；
/// ② `CornerRadius="{DynamicResource ModuleCornerRadius}"` 引用的是 `x:Double` token →
/// <b>运行时</b> InvalidCastException（点"加入现场"即崩）。
/// 这两类只有"装载真实 XAML + 真实布局 + 真实命中测试"才拦得住 —— 本文件即该最小集。</para>
/// <para><b>技术选型</b>：用 `Avalonia.Headless` 的 <see cref="HeadlessUnitTestSession"/>（自带调度线程，不绑定测试框架）。
/// **不引** `Avalonia.Headless.XUnit`：它在 12.1.2 依赖 xunit v3，与本工程 xunit v2 冲突（CS0433）。
/// 应用宿主用**真实 `App`**，从而加载真实的 Tokens/Controls 资源与 FluentTheme —— 这正是能发现"资源键类型写错"的关键。</para>
/// <para><b>不做的部分</b>：鼠标输入模拟（双击 → 弹窗 → 提交链路）——用户裁决 D-9 明确本批不做（成本更高、收益见后）。</para>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class ModuleWindowVisualTests : IClassFixture<HeadlessUiFixture>
{
    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具。</param>
    public ModuleWindowVisualTests(HeadlessUiFixture ui) => _ui = ui;

    [Fact]
    public void 模块窗口_应能装载真实XAML并显示端口标记()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var marker = FindMarker(window);
            marker.ShouldNotBeNull("模块窗口必须渲染出口标记（资源类型写错 / 层级被盖住时根本到不了这一步）");
            marker.IsEffectivelyVisible.ShouldBeTrue("标记必须**可见**（底色与边框对比过低＝等于看不见）");

            // 左右边缘：宽 20 · 长 114（ADR-0039 D3：长度比原 76 **加长 50%**）
            marker.Bounds.Width.ShouldBe(20, 1e-6);
            marker.Bounds.Height.ShouldBe(114, 1e-6);

            // 贴边：标记外沿与窗口边框重合（EdgeInset = 0）
            marker.Bounds.Left.ShouldBe(0, 1e-6, "标记应与外边框完全重合");
        });
    }

    [Fact]
    public void 端口标记_不应被交互带或缩放手柄抢走命中()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            var marker = FindMarker(window).ShouldNotBeNull();

            var center = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window);
            center.ShouldNotBeNull();

            VisualHarness.Settle();
            var hit = window.InputHitTest(center.Value);
            hit.ShouldNotBeNull(
                $"标记中心必须命得到元素（命中为空 = 被透明层吃掉）；"
                + $"窗口 {window.Bounds.Width}×{window.Bounds.Height}，标记 {marker.Bounds.Left},{marker.Bounds.Top} {marker.Bounds.Width}×{marker.Bounds.Height}，"
                + $"探测点 {center.Value.X:F1},{center.Value.Y:F1}");
            IsInsideMarker(hit).ShouldBeTrue(
                $"标记中心的命中必须落在标记上（实际命中 {Describe(hit)}）：此前边中点缩放手柄（同为边缘垂直居中）"
                + "与 6 px 拖动带压在标记之上，双击被吃掉 → 表现为\"网络标签改不了\"");
        });
    }

    [Fact]
    public void 模块窗口_未选中时手柄不得可命中_选中后放开()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var handle = FindByName<Avalonia.Controls.Shapes.Rectangle>(window, "HandleLeft").ShouldNotBeNull();
            handle.IsHitTestVisible.ShouldBeFalse("未选中时手柄不得可命中（否则压住端口标记中心 → 双击失效）");

            module.IsSelected = true;
            Dispatcher.UIThread.RunJobs();

            handle.IsHitTestVisible.ShouldBeTrue("选中后手柄可命中（可缩放）");
        });
    }

    [Fact]
    public void 外壳窗口_应能装载真实XAML并完成布局()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();

            var shell = new ShellViewModel(harness.Services, harness.Services);
            var window = new MainWindow { DataContext = shell };
            window.Show();
            VisualHarness.Settle();

            window.IsVisible.ShouldBeTrue();
            window.Bounds.Width.ShouldBeGreaterThan(0, "外壳必须完成布局（XAML/资源异常会在此前抛出）");

            // 侧边栏形态【ADR-0038 D1】：**活动栏** 6 个图标入口（按 Name 定位，避免把页面里的同类按钮算进来）
            var rail = window.GetVisualDescendants()
                .OfType<StackPanel>()
                .FirstOrDefault(p => string.Equals(p.Name, "RailButtons", StringComparison.Ordinal));

            rail.ShouldNotBeNull("活动栏容器必须存在（Name=RailButtons）");
            rail!.GetVisualDescendants().OfType<Button>().Count()
                .ShouldBe(6, "活动栏应有 6 个入口（模块清单 / 交互 / 拓扑 / Issue / 设置 / 帮助）");

            shell.IsCatalogPage.ShouldBeTrue("默认停在模块清单页");
            window.GetVisualDescendants().OfType<ModuleCatalogView>().ShouldNotBeEmpty("模块清单是 B 区常驻页面");

            // 【用户第七轮反馈】模块清单顶部：**分类栏**（图标无文字）+ 搜索框
            shell.Catalog.Categories.Count.ShouldBe(5, "分类 = 全部 / 数据源 / 显示与接收 / 记录与断言 / 其他");
            shell.Catalog.SelectedCategory.ShouldBe(ModuleCategories.All);

            // 切到交互页：页面切换必须真的换视图（滑块高亮状态由 Classes.active 表达）
            shell.ShowPageCommand.Execute(ShellPages.Interaction);
            VisualHarness.Settle();

            shell.IsInteractionPage.ShouldBeTrue();
            window.GetVisualDescendants().OfType<InteractionView>().ShouldNotBeEmpty("交互也是 B 区常驻页面");
        });
    }

    [Fact]
    public void 叠加信息层_默认关闭且开关真正控制可见性()
    {
        // 【ADR-0045 D1 · 用户第十五轮裁决】叠加层是**调试/观测层**：默认关闭（快捷键 L 切换），
        // 且**不进常驻界面**；端口标记**常显**、不受开关影响。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var overlay = window.GetVisualDescendants().OfType<ConnectionOverlay>().SingleOrDefault();
            overlay.ShouldNotBeNull("模块窗口必须包含叠加信息层（原'口上标记'开关是死开关：没有任何绑定）");

            overlay.IsVisible.ShouldBeFalse("**默认关闭**（ADR-0045 D1）");
            module.ShowInfoOverlay.ShouldBeFalse();
            new ApplicationSettings().ShowInfoOverlay.ShouldBeFalse("设置模型的默认值也必须是关闭");

            // 打开开关 → 叠加层显示（端口标记始终显示）
            harness.Windows.ShowInfoOverlay = true;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            overlay.IsVisible.ShouldBeTrue("开关必须真的控制叠加层的可见性");
            FindMarker(window).ShouldNotBeNull("端口标记是模块标准件，始终显示");

            // 关掉 → 隐藏
            harness.Windows.ShowInfoOverlay = false;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            overlay.IsVisible.ShouldBeFalse();
        });
    }

    [Fact]
    public void 叠加信息层_遮罩在标记之下而连线在标记之上()
    {
        // 【ADR-0045 + 用户第十七轮】层级（自下而上）：内容 → **遮罩** → **端口标记** → **连线层** → 运行指示/按钮簇。
        // · 遮罩在标记之下 ⇒ 标记不被压暗、可双击建网；
        // · 连线层在标记**之上** ⇒ 连线**跨过网络标签**直达窗口边界：标记是不透明块（20px 宽），
        //   连线若在其下，拼接处会被**两个标记遮掉 40px**，整条连接看起来"从标签连到中心"、失去意义。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var frame = FindByName<Border>(window, "Frame").ShouldNotBeNull();
            var root = frame!.Child.ShouldBeOfType<Grid>();

            var scrim = root.Children.OfType<Border>().SingleOrDefault(b => b.Name == "OverlayScrim");
            scrim.ShouldNotBeNull("必须有一层遮罩（'灰玻璃' = 半透明深色 + 其上浅色信息）");

            var overlay = root.Children.OfType<ConnectionOverlay>().SingleOrDefault();
            overlay.ShouldNotBeNull();

            var markers = root.Children.OfType<ItemsControl>().SingleOrDefault(c => c.Name == "PortMarkers");
            markers.ShouldNotBeNull();

            var overlaps = root.Children.OfType<ConnectionOverlay>().Select(root.Children.IndexOf).ToList();
            overlaps.ShouldHaveSingleItem("叠加层图形/连线应当只有一层（连线压在标记之上）");

            var scrimIndex = root.Children.IndexOf(scrim!);
            var markerIndex = root.Children.IndexOf(markers!);
            var overlayIndex = root.Children.IndexOf(overlay!);

            scrimIndex.ShouldBeLessThan(markerIndex, "遮罩必须在端口标记之下（标记不被压暗）");
            markerIndex.ShouldBeLessThan(overlayIndex, "连线层必须在端口标记之上（连线要跨过网络标签）");
            overlay!.IsHitTestVisible.ShouldBeFalse("叠加层不得参与命中（双击改网络名/拖动/缩放照常）");
            scrim!.IsHitTestVisible.ShouldBeFalse("遮罩同样不得参与命中");
        });
    }

    [Fact]
    public void 叠加信息层_未连标签必须朝窗口外且不压住中心方块()
    {
        // 【用户第十六轮反馈】截图里"未连"压在中心方块上：原实现**固定**写在残段右侧 4px，
        // 左边缘的口因此把文字推向模块中心（与方块重叠）。现按"口所在边"朝外摆并做窗口内钳制。
        const double textWidth = 24;
        var stub = new Point(100, 110);

        // 左边缘的口 ⇒ 文字在残段**左侧**（整段都在残段左边，不会碰到位于模块中心的方块）
        var left = ConnectionOverlay.UnconnectedLabelOrigin(PortSide.Left, stub, textWidth, 320, 220);
        (left.X + textWidth).ShouldBeLessThanOrEqualTo(stub.X, "文字右边缘不得越过残段外端（否则会压到中心方块）");
        left.X.ShouldBeGreaterThanOrEqualTo(0, "不得跑出窗口左侧");
        left.Y.ShouldBeLessThan(stub.Y, "文字画在连线上方");

        // 右边缘的口 ⇒ 文字在残段**右侧**
        var right = ConnectionOverlay.UnconnectedLabelOrigin(PortSide.Right, stub, textWidth, 320, 220);
        right.X.ShouldBeGreaterThanOrEqualTo(stub.X, "文字左边缘不得越过残段外端");

        // 贴近窗口边缘时被钳制在窗口内
        var clamped = ConnectionOverlay.UnconnectedLabelOrigin(PortSide.Right, new Point(310, 110), textWidth, 320, 220);
        (clamped.X + textWidth).ShouldBeLessThanOrEqualTo(320, "文字不得跑出窗口右侧");
    }

    [Fact]
    public void 叠加信息层_连线应从中心类型方块出发到窗口边界()
    {
        // 【ADR-0045】几何：起点 = **该口类型对应的中心方块**，终点 = **窗口边界**。
        // 旧实现从"标记中心"（= 边带中心，离边界仅 10px）出发 ⇒ 线短得像坏掉。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var marker = module.Ports.ShouldHaveSingleItem();
            var width = window.Bounds.Width;
            var height = window.Bounds.Height;

            var boxes = ConnectionOverlay.TypeBoxesOf(module.Ports, width, height);
            boxes.Count.ShouldBe(
                module.Ports.Select(p => p.TypeText).Distinct(StringComparer.Ordinal).Count(),
                "**有几种数据类型就有几个中心方块**（用户草案）");
            boxes[0].TypeText.ShouldBe(marker.TypeText);
            boxes[0].Box.Center.X.ShouldBe(width / 2, 0.001, "单个方块时水平正好落在模块中心");
            boxes[0].Box.Center.Y.ShouldBe(
                height / 2,
                ConnectionOverlay.TypeBoxSize,
                "方块**连同其下的类型文本作为一块**整体居中 ⇒ 方块中心与模块中心最多差半个方块");

            var (start, end) = ConnectionOverlay.LineOf(marker, boxes, width, height);

            start.ShouldBe(boxes[0].Box.Center, "线从**中心方块**出发（不是从标记中心）");
            end.X.ShouldBe(width, 0.001, "线的终点在**窗口边界**上（拼接后与对端那半段相接）");
            start.X.ShouldBeLessThan(end.X, "发送口在右边缘 ⇒ 线向右走");

            (end.X - start.X).ShouldBeGreaterThan(
                PortMarkerPanel.BandThickness * 2,
                "线长必须显著大于旧实现的 10px 残段（否则又'像 bug'）");
        });
    }

    [Fact]
    public void 叠加信息层_中点色与虚线相位应是两端一致的纯函数()
    {
        // 【ADR-0045 D4】两个窗口各画自己那半段，靠"**同一条中点色规则**"在公共边颜色相接；
        // 虚线相位则是"发送/接收互为相反数"——这些必须是**纯函数**，否则两端会各算各的。
        var send = Color.Parse("#EA580C");
        var receive = Color.Parse("#0891B2");

        var mid = ConnectionOverlay.MidColor(send, receive);
        mid.ShouldBe(ConnectionOverlay.MidColor(receive, send), "中点色对两端对称（否则拼接处会出现色差）");
        mid.R.ShouldBe((byte)((send.R + receive.R) / 2));
        mid.G.ShouldBe((byte)((send.G + receive.G) / 2));
        mid.B.ShouldBe((byte)((send.B + receive.B) / 2));

        var (sendOffset, receiveOffset) = OverlayAnimationClock.DashOffsets(3);
        sendOffset.ShouldBe(-receiveOffset, "发送与接收的虚线方向必须相反（流动方向 = 数据流方向）");

        for (var tick = 0; tick < 200; tick++)
        {
            var phase = OverlayAnimationClock.DashPhase(tick);
            phase.ShouldBeInRange(0, OverlayAnimationClock.Period, $"相位必须落在 [0, 周期)（tick={tick}）");
        }

        OverlayAnimationClock.DashPhase(0).ShouldBe(0, "起始相位为 0（两端同相启动）");
    }

    [Fact]
    public void 叠加信息层_动画时钟只在可见时订阅()
    {
        // 【ADR-0045】动画纪律：一个**共享** 20Hz 时钟；叠加层关闭时**退订**（不能白烧 CPU）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var baseline = OverlayAnimationClock.SubscriberCount;

            // 默认关闭 ⇒ 不订阅
            OverlayAnimationClock.SubscriberCount.ShouldBe(baseline, "叠加层关闭时不得订阅动画时钟");

            // 打开 ⇒ 订阅 +1
            harness.Windows.ShowInfoOverlay = true;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            OverlayAnimationClock.SubscriberCount.ShouldBe(baseline + 1, "可见的叠加层应订阅共享时钟");
            OverlayAnimationClock.IsRunning.ShouldBeTrue("有订阅者时时钟才跑");

            // 关闭 ⇒ 退订
            harness.Windows.ShowInfoOverlay = false;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            OverlayAnimationClock.SubscriberCount.ShouldBe(baseline, "关闭后必须退订");
        });
    }

    [Fact]
    public void 叠加信息层_已连接时应指向对端方位()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);
            harness.PlaceSinkAt("r1", 400);   // 接收端在 s1 右侧 → s1 的发送口对端在"右"

            harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
            harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();
            harness.Windows.Refresh();
            VisualHarness.Settle();

            var marker = harness.Windows.Modules
                .Single(m => m.InstanceId == "s1")
                .Ports.ShouldHaveSingleItem();

            marker.IsConnected.ShouldBeTrue();
            marker.HasLink.ShouldBeTrue("已连接且已装配对端方位 → 叠加层应画线");
            marker.PeerSide.ShouldBe(PortSide.Right, "对端在右侧 → 线画向右边（拼接后与对端窗口的线自然相连）");
            marker.PeerText.ShouldBe("r1.in");

            // 相隔 400px（未拼接）⇒ 线末端淡出并标注对端（ADR-0045 D4）
            marker.PeerAdjacent.ShouldBeFalse("两窗相隔很远 ⇒ 不是拼接状态");
        });
    }

    [Fact]
    public void 叠加信息层_贴合的对端应识别为已拼接()
    {
        // 【ADR-0045 D4】拼接（接触）时：线到边界直接接过去、**不标对端**（标在缝上会糊）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);
            harness.PlaceSinkAt("r1", ModuleViewViewModel.DefaultWidth);   // 紧贴 s1 右边缘

            harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
            harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();
            harness.Windows.Refresh();
            VisualHarness.Settle();

            var marker = harness.Windows.Modules
                .Single(m => m.InstanceId == "s1")
                .Ports.ShouldHaveSingleItem();

            marker.PeerAdjacent.ShouldBeTrue("两窗接触 ⇒ 已拼接 ⇒ 连线在公共边直接接过去（不淡出、不标对端）");
        });
    }

    [Fact]
    public void 主题应用器_应把主题色与背景写到资源上()
    {
        _ui.Run(() =>
        {
            var settings = new ApplicationSettings
            {
                AccentColor = "#16A34A",
                BackgroundMode = ThemeApplier.BackgroundLightGray,
            };

            ThemeApplier.Apply(settings);

            Application.Current!.TryGetResource("AccentPrimary", ThemeVariant.Light, out var accentResource).ShouldBeTrue();
            accentResource.ShouldBeAssignableTo<ISolidColorBrush>()!.Color
                .ShouldBe(Color.Parse("#16A34A"), "主题色必须落到角色 token 上（视图只取 token，故全体窗口一并跟随）");

            Application.Current.TryGetResource("BgWorkspace", ThemeVariant.Light, out var workspaceResource).ShouldBeTrue();
            workspaceResource.ShouldBeAssignableTo<ISolidColorBrush>()!.Color
                .ShouldBe(Color.Parse("#E8EAED"), "背景档位必须覆盖 BgWorkspace");

            // 跟随主题（默认）→ 撤销覆盖：资源回到内置主题字典的值
            ThemeApplier.Apply(new ApplicationSettings());
            Application.Current.TryGetResource("BgWorkspace", ThemeVariant.Light, out var restored).ShouldBeTrue();
            restored.ShouldBeAssignableTo<ISolidColorBrush>()!.Color
                .ShouldBe(Color.Parse("#F3F4F6"), "档位回到 Theme 后必须还原内置 token 值");
        });
    }

    [Fact]
    public void 人机操作三通道_应在模块窗口渲染并投递()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceInputsAt("in1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();

            // ① 声明驱动的渲染：文本输入 / 命令按钮 / 开关各一个，且**没有**"未渲染"占位
            module.Elements.OfType<TextInputElementViewModel>().Count().ShouldBe(1);
            module.Elements.OfType<CommandElementViewModel>().Count().ShouldBe(1);
            module.Elements.OfType<ToggleElementViewModel>().Count().ShouldBe(1);
            module.Elements.ShouldNotContain(e => e is UnsupportedElementViewModel);

            // ② 真窗口里确实渲染出输入控件（不只是视图模型存在）
            var window = harness.OpenWindow(module);
            window.GetVisualDescendants().OfType<TextBox>().ShouldNotBeEmpty("文本输入必须渲染成可输入的控件");

            // ③ 命令按钮：点击 = 投递到模块线程（成功即无错误提示）
            var command = module.Elements.OfType<CommandElementViewModel>().Single();
            command.InvokeCommand.Execute(null);
            command.HasError.ShouldBeFalse($"命令投递应成功（实际：{command.LastResult}）");
            command.LastResult.ShouldNotBeNullOrEmpty("必须给出投递反馈（不静默）");

            // ④ 文本输入：提交后不再处于"未提交"状态；成功后清空错误
            var input = module.Elements.OfType<TextInputElementViewModel>().Single();
            input.Text = "2.5";
            input.IsDirty.ShouldBeTrue("编辑后应标记未提交（D-5：回车/失焦才写入）");
            input.CommitCommand.Execute(null);
            input.IsDirty.ShouldBeFalse("提交成功后不应再显示未提交");

            // ⑤ 未声明的输入源 → 就地报错（不静默）
            var bogus = new TextInputElementViewModel(
                new HYWDebugAssistant.Sdk.Views.ViewElement(HYWDebugAssistant.Sdk.Views.ViewElementKind.TextInput, "nope", "假输入"),
                (source, text) => harness.Services.SubmitInput("in1", source, text));
            bogus.Text = "1";
            bogus.CommitCommand.Execute(null);
            bogus.LastResult.ShouldNotBeNull().ShouldContain("未生效");
        });
    }

    [Fact]
    public void 端口标记_类型色条与指示灯应贴在标记两端()
    {
        // 【用户第十三轮反馈】标记内容重排：窄颜色条（数据包类型）· 网络名 · 放大箭头 · 加大且贴边的指示灯。
        // 本测试把**几何**钉住（尺寸 / 贴边 / 槽位顺序），避免"文本一变排版就抖"与"灯又缩回中间"。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var marker = FindMarker(window).ShouldNotBeNull();
            var markerBox = ScreenRect(marker, window);

            var strip = FindVisibleByClass<Border>(window, "typeStrip").ShouldNotBeNull("标记上必须有类型窄色条（颜色 = PacketTypePalette 占位色，映射待定）");
            var lamp = FindVisibleByClass<StateIndicator>(window, "markerLamp").ShouldNotBeNull("标记上必须有连接指示灯");

            // ① 色条 = **窄条**：沿标记长边 6 px（token PortMarkerStripWidth）、跨满标记内宽 18 px
            //    （标记 20 宽 − 1 px 边框 ×2 = 18）
            var stripBox = ScreenRect(strip, window);
            stripBox.Height.ShouldBe(6, 1.0, "竖排（旋转后）色条沿标记长边必须是 6 px 窄条");
            stripBox.Width.ShouldBe(18, 1.0, "竖排色条应跨满标记内宽（20 − 边框 1×2）");

            // ② 指示灯 6×6 → **8×8**（半径 +1），且**离端部留 5px**（复验："过于靠边" ⇒ 由贴边改为留间距）
            var lampBox = ScreenRect(lamp, window);
            lampBox.Width.ShouldBe(8, 1.0, "指示灯 6×6 → 8×8（StateIndicator 半径 = 边长/2 − 1 ⇒ 2 → 3）");
            lampBox.Height.ShouldBe(8, 1.0);
            lamp.Margin.ShouldBe(new Thickness(0, 0, 5, 0), "指示灯离标记端部留 5px（内容坐标右边距）");

            // ③ 两端贴齐：色条贴标记一端、灯在另一端内侧 5px（各留 1 px 边框）
            stripBox.Top.ShouldBeGreaterThan(markerBox.Top, "色条与灯必须分居标记两端");
            lampBox.Top.ShouldBeLessThan(stripBox.Top, "槽位顺序 = 读数顺序：内容旋转 −90° ⇒ 色条在下端、指示灯在上端");
            lampBox.Top.ShouldBe(markerBox.Top + 6, 1.5, "指示灯距端部 = 1 px 边框 + 5 px 间距");
            stripBox.Bottom.ShouldBe(markerBox.Bottom - 1, 1.5, "色条必须贴到标记另一端部");

            // ④ 标记上不再有"数据包类型文本"与行内提示文本（类型只进悬停；提示进 `?` 帮助弹窗）
            var markerVm = marker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text)
                .ToList();

            texts.ShouldNotContain(markerVm.TypeText, "类型文本已从标记上移除（标签名通常很长，塞不下；类型只在悬停里给出）");
            texts.ShouldNotContain(markerVm.Hint, "编辑提示文本必须收进 `?` 帮助弹窗（不再行内展开、不吃版面）");
            texts.ShouldContain(markerVm.NetworkText, "标记上的显示文本 = 网络名（无\"未连\"后缀）");
        });
    }

    [Fact]
    public void 端口标记_方向色应来自方向语义色且与主题色无关()
    {
        // 【ADR-0044 · 用户第十四轮 D1a/D2c】：标记**边框**与**箭头**都按方向着色，
        // 且颜色**只**由方向语义色驱动（改主题色不得影响它）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();

            // 发送色 / 接收色设成两个**彼此不同、也与主题色不同**的值
            ThemeApplier.Apply(new ApplicationSettings
            {
                AccentColor = "#16A34A",          // 主题色故意选第三个颜色，验证"方向色不跟随主题色"
                DirectionSendColor = "#123456",
                DirectionReceiveColor = "#654321",
            });

            try
            {
                // ① 发送口（右边缘）
                harness.Place("s1");
                var sender = harness.Windows.Modules.ShouldHaveSingleItem();
                var sendWindow = harness.OpenWindow(sender);
                var sendMarker = FindMarker(sendWindow).ShouldNotBeNull();

                ColorOf(sendMarker.BorderBrush).ShouldBe(Color.Parse("#123456"),
                    "标记**边框**必须用发送方向色（D2c）");

                var sendArrow = FindVisibleArrow(sendWindow).ShouldNotBeNull();
                ColorOf(sendArrow.Foreground).ShouldBe(Color.Parse("#123456"),
                    "标记**箭头**必须用发送方向色");

                // 建网预览描边 = 同一个方向色（D3b）
                var sendVm = sendMarker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
                sendVm.IsDockCandidate = true;
                VisualHarness.Settle();

                var highlight = FindVisibleByClass<Border>(sendWindow, "dockHighlight");
                highlight.ShouldNotBeNull("建网预览描边必须存在（即将自动建网时可见）");
                ColorOf(highlight!.BorderBrush).ShouldBe(Color.Parse("#123456"),
                    "建网预览描边用**同一方向色**（仅以线宽区分，D3b）");

                // ② 接收口（左边缘）
                harness.PlaceSinkAt("r1", 400);
                var sink = harness.Windows.Modules.Single(m => m.InstanceId == "r1");
                var receiveWindow = harness.OpenWindow(sink);
                var receiveMarker = FindMarker(receiveWindow).ShouldNotBeNull();

                ColorOf(receiveMarker.BorderBrush).ShouldBe(Color.Parse("#654321"),
                    "接收口标记边框必须用接收方向色");
                ColorOf(FindVisibleArrow(receiveWindow)!.Foreground).ShouldBe(Color.Parse("#654321"),
                    "接收口箭头必须用接收方向色");
            }
            finally
            {
                // 还原为"跟随主题默认值"（空串），并验证**深浅两套内置默认值**真的生效
                ThemeApplier.Apply(new ApplicationSettings());
            }

            using var restored = new VisualHarness();
            restored.Place("s2");
            var restoredWindow = restored.OpenWindow(restored.Windows.Modules.ShouldHaveSingleItem());
            var fallbackMarker = FindMarker(restoredWindow).ShouldNotBeNull();

            Application.Current!.TryGetResource("DirectionSend", ThemeVariant.Light, out var tokenBrush).ShouldBeTrue();
            ColorOf(tokenBrush as IBrush).ShouldBe(ColorOf(fallbackMarker.BorderBrush),
                "空设置 = 跟随主题默认 ⇒ 必须回到 Tokens.axaml 的内置方向色（否则深色默认值就成了死值）");
        });
    }

    // ---------------------------------------------------------------- 辅助

    /// <summary>取画刷颜色（非纯色/为空时返回 <see cref="Colors.Transparent"/>，便于断言直接失败）。</summary>
    private static Color ColorOf(IBrush? brush) =>
        brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    /// <summary>找**真正可见**的方向箭头（标记模板的竖排/横排两套分支只有一套可见）。</summary>
    private static TextBlock? FindVisibleArrow(Visual root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text is "↓" or "→");

    private static Border? FindMarker(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("portMarker"));

    private static bool IsInsideMarker(IInputElement? hit)
    {
        var current = hit as Visual;
        while (current is not null)
        {
            if (current is Control { DataContext: PortMarkerViewModel })
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static T? FindByName<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    /// <summary>
    /// 按 `Classes` 找**真正可见**的控件（标记模板里竖排/横排两套分支同时存在，只有一套可见；
    /// 两套分支**同名会撞命名域**，故模板内元素用 Classes 标识）。
    /// </summary>
    private static T? FindVisibleByClass<T>(Visual root, string className)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>()
            .FirstOrDefault(c => c.Classes.Contains(className) && c.IsEffectivelyVisible);

    /// <summary>控件在给定根坐标系里的**变换后**外框（竖排标记内容带 −90° 旋转变换，Bounds 是变换前的）。</summary>
    private static Rect ScreenRect(Visual visual, Visual root)
    {
        var corners = new[]
        {
            new Point(0, 0),
            new Point(visual.Bounds.Width, 0),
            new Point(0, visual.Bounds.Height),
            new Point(visual.Bounds.Width, visual.Bounds.Height),
        }
        .Select(p => visual.TranslatePoint(p, root) ?? default)
        .ToList();

        var left = corners.Min(p => p.X);
        var top = corners.Min(p => p.Y);
        return new Rect(left, top, corners.Max(p => p.X) - left, corners.Max(p => p.Y) - top);
    }

    /// <summary>命中元素的诊断描述（断言失败时能一眼看出"被谁挡住了"）。</summary>
    private static string Describe(IInputElement? hit) =>
        hit is Control control
            ? $"{control.GetType().Name}(Name={control.Name ?? "-"}, DataContext={control.DataContext?.GetType().Name ?? "-"})"
            : hit?.GetType().Name ?? "null";

    /// <summary>视觉测试现场：真实运行时 + 真实服务面 + 真实窗口。</summary>
    private sealed class VisualHarness : IDisposable
    {
        public VisualHarness()
        {
            Runtime = new ModuleRuntime();
            Services = new RuntimeServices(Runtime);
            Register("visual-sender", typeof(VisualSenderModule));
            Register("visual-sink", typeof(VisualSinkModule));
            Register("visual-inputs", typeof(VisualInputsModule));
            Windows = new ModuleWindowsViewModel(Services, Services);
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        /// <summary>加入一个发送模块并摆放（端口在右边缘）。</summary>
        public void Place(string instanceId) => PlaceAt(instanceId, 0);

        /// <summary>加入一个发送模块并**显式摆放**到给定屏幕位置（叠加层方位判定需要确定几何）。</summary>
        public void PlaceAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-sender/1.0.0", x, y);

        /// <summary>加入一个接收模块并显式摆放（建网测试用）。</summary>
        public void PlaceSinkAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-sink/1.0.0", x, y);

        /// <summary>加入一个"人机操作三通道"模块并摆放（命令/输入/开关渲染与投递测试用）。</summary>
        public void PlaceInputsAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-inputs/1.0.0", x, y);

        private void PlaceModuleAt(string instanceId, string moduleIdLiteral, double x, double y)
        {
            Services.AddModule(instanceId, moduleIdLiteral).Succeeded.ShouldBeTrue();
            Windows.Refresh();

            Services.UpdateModuleLayout(new ModuleLayoutView(
                instanceId,
                x,
                y,
                ModuleViewViewModel.DefaultWidth,
                ModuleViewViewModel.DefaultHeight)).Succeeded.ShouldBeTrue();

            Windows.Refresh();
        }

        /// <summary>按窗口管理器的方式开窗（**赋值顺序与生产代码一致**，故也能覆盖接线类缺陷）。</summary>
        public ModuleWindow OpenWindow(ModuleViewViewModel module)
        {
            var window = new ModuleWindow
            {
                DataContext = module,
                Controller = Windows,
            };

            window.Show();
            Settle();
            return window;
        }

        /// <summary>
        /// 把无头环境推进到"布局完成"：调度队列 + **渲染帧**。
        /// </summary>
        /// <remarks>
        /// 只跑 <c>RunJobs</c> 不够：布局/渲染由渲染帧驱动，无头环境下渲染定时器不会自己走，
        /// 于是"单独跑通过、多个测试一起跑却命不中"（实测踩过：命中为 null）。
        /// </remarks>
        public static void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => Runtime.Dispose();

        private void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
                ModuleId.Create("test", name, "1.0.0"),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [],
                null,
                true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }
    }

    /// <summary>视觉测试用发送模块（一个发送口，右边缘）。</summary>
    [ModuleDefinition("test", "visual-sender", "1.0.0")]
    public sealed class VisualSenderModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }

    /// <summary>视觉测试用接收模块（一个接收口，左边缘；建网测试用）。</summary>
    [ModuleDefinition("test", "visual-sink", "1.0.0")]
    public sealed class VisualSinkModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));

            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }

    /// <summary>视觉测试用"人机操作三通道"模块（文本输入 + 命令 + 开关）——ADR-0040。</summary>
    [ModuleDefinition("test", "visual-inputs", "1.0.0")]
    public sealed class VisualInputsModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclareView(new ViewDeclaration(
                [
                    new ViewElement(ViewElementKind.TextInput, "input", "值"),
                    new ViewElement(ViewElementKind.CommandButton, "fire"),
                    new ViewElement(ViewElementKind.ToggleSlider, "auto", "自动"),
                ],
                commands: [new ViewCommand("fire", "触发")]));
    }
}

/// <summary>
/// 无头会话夹具（M1-16 · ADR-0039 · D-9）：**一个会话跑完一个测试类**，用真实 `App` 作宿主。
/// </summary>
public sealed class HeadlessUiFixture : IDisposable
{
    private readonly HeadlessUnitTestSession _session =
        HeadlessUnitTestSession.StartNew(typeof(HYWDebugAssistant.App.App));

    /// <summary>在 UI 线程上执行测试体（Avalonia 控件只能在 UI 线程上操作）。</summary>
    /// <param name="action">测试体。</param>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();
}
