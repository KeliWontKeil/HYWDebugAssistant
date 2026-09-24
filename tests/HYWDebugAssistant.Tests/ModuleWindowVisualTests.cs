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

            // 侧边栏形态【ADR-0038 D1】：活动栏 6 个图标入口；默认显示**模块清单页**（B 区常驻页面）
            var rail = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("rail")).ToList();
            rail.Count.ShouldBe(6, "活动栏应有 6 个入口（模块清单 / 交互 / 拓扑 / Issue / 设置 / 帮助）");

            shell.IsCatalogPage.ShouldBeTrue("默认停在模块清单页");
            window.GetVisualDescendants().OfType<ModuleCatalogView>().ShouldNotBeEmpty("模块清单是 B 区常驻页面");

            // 切到交互页：页面切换必须真的换视图（滑块高亮状态由 Classes.active 表达）
            shell.ShowPageCommand.Execute(ShellPages.Interaction);
            VisualHarness.Settle();

            shell.IsInteractionPage.ShouldBeTrue();
            window.GetVisualDescendants().OfType<InteractionView>().ShouldNotBeEmpty("交互也是 B 区常驻页面");
        });
    }

    [Fact]
    public void 叠加信息层_开关应真正控制可见性()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var overlay = window.GetVisualDescendants().OfType<ConnectionOverlay>().SingleOrDefault();
            overlay.ShouldNotBeNull("模块窗口必须包含叠加信息层（原'口上标记'开关是死开关：没有任何绑定）");

            overlay.IsVisible.ShouldBeTrue("默认显示");
            module.ShowInfoOverlay.ShouldBeTrue();

            // 关掉开关 → 叠加层隐藏；**端口标记本身仍常显**（ADR-0039 D5）
            harness.Windows.ShowInfoOverlay = false;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            overlay.IsVisible.ShouldBeFalse("开关必须真的控制叠加层的可见性");
            FindMarker(window).ShouldNotBeNull("端口标记是模块标准件，始终显示");
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
            accentResource.ShouldBeOfType<SolidColorBrush>().Color
                .ShouldBe(Color.Parse("#16A34A"), "主题色必须落到角色 token 上（视图只取 token，故全体窗口一并跟随）");

            Application.Current.TryGetResource("BgWorkspace", ThemeVariant.Light, out var workspaceResource).ShouldBeTrue();
            workspaceResource.ShouldBeOfType<SolidColorBrush>().Color
                .ShouldBe(Color.Parse("#E8EAED"), "背景档位必须覆盖 BgWorkspace");

            // 跟随主题（默认）→ 撤销覆盖：资源回到内置主题字典的值
            ThemeApplier.Apply(new ApplicationSettings());
            Application.Current.TryGetResource("BgWorkspace", ThemeVariant.Light, out var restored).ShouldBeTrue();
            restored.ShouldBeOfType<SolidColorBrush>().Color
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

    // ---------------------------------------------------------------- 辅助

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
