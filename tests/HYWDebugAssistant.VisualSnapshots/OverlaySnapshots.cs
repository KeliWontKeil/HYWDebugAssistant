using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Ui.Geometry;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **叠加层截图**：三窗布局（发送器在上、两个接收器在下、一个未连接）→ 每个窗口一张 PNG。
/// </summary>
/// <remarks>
/// 只负责“把它画出来”，不判断画得对不对；对错由人眼看 <c>artifacts\snapshots\overlay\</c> 下的图。
/// </remarks>
[Collection("visual-snapshots")]
public class OverlaySnapshots
{
    /// <summary>冲突截图里要抓的两个窗口（发送端与不兼容宿）。</summary>
    private static readonly string[] ConflictInstances = ["sender", "wide"];

    /// <summary>生成叠加层截图（<c>overlay\*.png</c>）。</summary>
    [Fact]
    public async Task 生成快照_叠加层()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                // 三窗布局覆盖：多对端分叉、未连接残段、信息区三档字号
                using var harness = new SnapshotHarness();

                harness.AddSender("sender", x: 260, y: 40);
                harness.AddSink("m1", x: 40, y: 380);
                harness.AddSink("m2", x: 480, y: 380);
                harness.AddSender("lonely", x: 700, y: 40);   // 未连接 ⇒ 方块照画，`m/n` 显示 0/--

                UpdateNetwork(harness, "sender", "out", "bus");
                UpdateNetwork(harness, "m1", "in", "bus");
                UpdateNetwork(harness, "m2", "in", "bus");

                harness.Windows.ShowInfoOverlay = true;
                harness.Windows.Refresh();

                foreach (var module in harness.Windows.Modules)
                {
                    var window = harness.OpenWindow(module);
                    SnapshotTool.Pump();

                    SnapshotTool.Save(window, Path.Combine("overlay", module.InstanceId + ".png"));
                    SnapshotTool.DumpTree(window, Path.Combine("overlay", module.InstanceId + "-tree.txt"));
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **建网预览粗边截图**：把发送端标记置为“即将建网”，检查 2 DIP 方向色粗边是否完整。
    /// </summary>
    /// <remarks>
    /// 这是**呈现**用例，不写像素断言；人眼看粗边是否被类型色条/指示灯盖住。
    /// </remarks>
    [Fact]
    public async Task 生成快照_建网预览粗边()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                using var harness = new SnapshotHarness();

                harness.AddSender("sender", x: 260, y: 40);
                harness.AddSink("sink", x: 40, y: 380);
                harness.Windows.Refresh();

                var module = harness.Windows.Modules.Single(m => m.InstanceId == "sender");
                var marker = Assert.Single(module.Ports);
                marker.IsDockCandidate = true;

                var window = harness.OpenWindow(module);
                SnapshotTool.Pump();
                SnapshotTool.Save(window, Path.Combine("overlay", "dock-highlight.png"));
                window.Close();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **同网络名但契约不兼容的红边截图**：发送器同时连一个兼容宿与一个不兼容宿。
    /// </summary>
    /// <remarks>只看红色粗边框是否出现在两端标记上；不写像素断言。</remarks>
    [Fact]
    public async Task 生成快照_契约冲突红边()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                using var harness = new SnapshotHarness();

                harness.AddSender("sender", x: 260, y: 40);
                harness.AddSink("ok", x: 40, y: 380);
                harness.AddWideSink("wide", x: 480, y: 380);

                UpdateNetwork(harness, "sender", "out", "bus");
                UpdateNetwork(harness, "ok", "in", "bus");
                UpdateNetwork(harness, "wide", "in", "bus");

                harness.Windows.Refresh();

                foreach (var instanceId in ConflictInstances)
                {
                    var module = harness.Windows.Modules.Single(m => m.InstanceId == instanceId);
                    var window = harness.OpenWindow(module);
                    SnapshotTool.Pump();
                    SnapshotTool.Save(window, Path.Combine("overlay", "conflict-" + instanceId + ".png"));
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **窄窗 chrome 机制**：宽度切到 &lt; 200 时按钮簇竖排、选中标题按字竖排。
    /// </summary>
    /// <remarks>
    /// 这是**机制**断言（排布方向/最大宽），不是观感判据：上一轮的窄窗样式被内联本地值压住，
    /// 只看截图很难发现"样式根本没生效"，所以把这条关系钉在真窗渲染上（观感仍看 PNG）。
    /// </remarks>
    [Fact]
    public async Task 生成快照_窄窗chrome()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                using var harness = new SnapshotHarness();
                harness.AddSink("wide", x: 40, y: 40, width: 320, height: 220);
                harness.AddSink("narrow", x: 40, y: 300, width: 150, height: 220);

                foreach (var (instanceId, expectedNarrow) in new[] { ("wide", false), ("narrow", true) })
                {
                    var module = harness.Windows.Modules.Single(m => m.InstanceId == instanceId);
                    module.IsSelected = true;

                    var window = harness.OpenWindow(module);

                    // 直接开窗不走 `ModuleWindowManager`：按模型把尺寸落到窗口（与运行时 ApplyGeometry 等价）。
                    window.Width = module.Width;
                    window.Height = module.Height;
                    SnapshotTool.Pump();
                    SnapshotTool.Pump();

                    Assert.Equal(module.Width, window.Bounds.Width);
                    Assert.Equal(expectedNarrow, window.Classes.Contains("narrow"));

                    var clusterGrid = window.GetVisualDescendants().OfType<Grid>()
                        .First(grid => grid.Name == "ClusterGrid");
                    var buttons = new (string Name, Point Origin)[]
                    {
                        ("WallpaperButton", OriginOf("WallpaperButton")),
                        ("ResetButton", OriginOf("ResetButton")),
                        ("LockButton", OriginOf("LockButton")),
                        ("HideButton", OriginOf("HideButton")),
                        ("CloseButton", OriginOf("CloseButton")),
                    };

                    Point OriginOf(string name) =>
                        window.GetVisualDescendants().OfType<Control>()
                            .First(control => control.Name == name)
                            .TranslatePoint(default, clusterGrid)!.Value;

                    // 窄窗：一列、**关闭在最上**（行号反序）；宽窗：一行、关闭在最右。
                    if (expectedNarrow)
                    {
                        Assert.Single(buttons.Select(b => b.Origin.X).Distinct());
                        Assert.True(buttons[4].Origin.Y < buttons[3].Origin.Y);
                        Assert.True(buttons[3].Origin.Y < buttons[2].Origin.Y);
                        Assert.True(buttons[2].Origin.Y < buttons[1].Origin.Y);
                        Assert.True(buttons[1].Origin.Y < buttons[0].Origin.Y);
                    }
                    else
                    {
                        Assert.Single(buttons.Select(b => b.Origin.Y).Distinct());
                        Assert.True(buttons[0].Origin.X < buttons[1].Origin.X);
                        Assert.True(buttons[1].Origin.X < buttons[2].Origin.X);
                        Assert.True(buttons[2].Origin.X < buttons[3].Origin.X);
                        Assert.True(buttons[3].Origin.X < buttons[4].Origin.X);
                    }

                    var badgeText = window.GetVisualDescendants().OfType<Border>()
                        .First(border => border.Name == "NameBadge")
                        .GetVisualDescendants().OfType<TextBlock>()
                        .First();
                    Assert.Equal(expectedNarrow ? 14 : 180, badgeText.MaxWidth);

                    // 「固定/解锁」按钮必须贴着标记（沿边方向），不能落到窗口尽头。
                    var anchor = window.GetVisualDescendants().OfType<Button>()
                        .First(button => button.Classes.Contains("anchorToggle"));
                    var anchorOrigin = anchor.TranslatePoint(default, window)!.Value;
                    var marker = window.GetVisualDescendants().OfType<Border>()
                        .First(border => border.Classes.Contains("portMarker"));
                    var markerOrigin = marker.TranslatePoint(default, window)!.Value;
                    var alongAnchor = expectedNarrow ? anchorOrigin.Y : anchorOrigin.X;
                    var alongMarker = expectedNarrow ? markerOrigin.Y : markerOrigin.X;
                    var alongLength = expectedNarrow ? marker.Bounds.Height : marker.Bounds.Width;
                    var buttonLength = expectedNarrow ? anchor.Bounds.Height : anchor.Bounds.Width;
                    Assert.InRange(
                        alongAnchor,
                        alongMarker - buttonLength - 8,
                        alongMarker + alongLength + 8);

                    SnapshotTool.Save(window, Path.Combine("chrome", instanceId + ".png"));
                    SnapshotTool.DumpTree(window, Path.Combine("chrome", instanceId + "-tree.txt"));
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **端口标记**：尺寸固定（150×20），放不下的边会被换掉，且贴住窗口外沿。
    /// </summary>
    [Fact]
    public async Task 生成快照_顶边端口标记()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                using var harness = new SnapshotHarness();
                harness.AddTopSink("wide", x: 40, y: 40, width: 260, height: 180);
                harness.AddTopSink("narrow", x: 400, y: 40, width: 130, height: 420);

                foreach (var (instanceId, width, height) in new[] { ("wide", 260.0, 180.0), ("narrow", 130.0, 420.0) })
                {
                    var module = harness.Windows.Modules.Single(m => m.InstanceId == instanceId);
                    var window = harness.OpenWindow(module);
                    window.Width = width;
                    window.Height = height;
                    SnapshotTool.Pump();
                    SnapshotTool.Pump();

                    var marker = window.GetVisualDescendants().OfType<Border>()
                        .First(border => border.Classes.Contains("portMarker"));
                    var origin = marker.TranslatePoint(default, window);
                    Assert.NotNull(origin);

                    // 声明边 Top；宽 130 放不下 ⇒ 换到左右边（标记仍 20×150）。
                    var expectedSide = PortMarkerLayout.SideOf(
                        PortMarkerLayout.FitEdge(PortEdge.Top, PortDirection.Receive, width, height));
                    var vertical = expectedSide is PortSide.Left or PortSide.Right;
                    Assert.Equal(vertical ? PortMarkerMetrics.Width : PortMarkerMetrics.Length, marker.Bounds.Width, 0.5);
                    Assert.Equal(vertical ? PortMarkerMetrics.Length : PortMarkerMetrics.Width, marker.Bounds.Height, 0.5);

                    var frameBorder = ModuleSurfaceMetrics.FrameBorder;
                    if (vertical)
                    {
                        var expectedX = PortMarkerLayout.EdgeInset + frameBorder + (expectedSide == PortSide.Left ? 0 : width - marker.Bounds.Width);
                        Assert.InRange(origin!.Value.X, expectedX - 0.5, expectedX + 0.5);
                        Assert.True(origin.Value.Y >= -0.5);
                        Assert.True(origin.Value.Y + marker.Bounds.Height <= height + 0.5);
                    }
                    else
                    {
                        var expectedY = PortMarkerLayout.EdgeInset + frameBorder;
                        Assert.InRange(origin!.Value.Y, expectedY - 0.5, expectedY + 0.5);
                        Assert.True(origin.Value.X >= -0.5);
                        Assert.True(origin.Value.X + marker.Bounds.Width <= width + 0.5);
                    }

                    SnapshotTool.Save(window, Path.Combine("chrome", "port-marker-" + instanceId + ".png"));
                    SnapshotTool.DumpTree(window, Path.Combine("chrome", "port-marker-" + instanceId + "-tree.txt"));
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>把某实例的某口挂到命名网络上（失败即抛——截图现场必须是真的）。</summary>
    private static void UpdateNetwork(SnapshotHarness harness, string instanceId, string port, string network)
    {
        var update = harness.Services.UpdatePortNetwork(instanceId, port, network);

        if (!update.Succeeded)
        {
            throw new InvalidOperationException($"挂网络失败：{instanceId}/{port} → {network}");
        }
    }

    /// <summary>快照现场（复用生产 VM 与真实模块窗口）。</summary>
    internal sealed class SnapshotHarness : IDisposable
    {
        public SnapshotHarness()
        {
            ViewState = new ViewStateStore();
            Runtime = new ModuleRuntime(viewState: ViewState);
            Services = new RuntimeServices(Runtime);
            Windows = new ModuleWindowsViewModel(Services, Services, ViewState);

            // 视图由模块自己的 XAML 提供；截图夹具只需要端口与角色（叠加层几何）。
            Register("snapshot-sender", typeof(SnapshotSenderModule));
            Register("snapshot-sink", typeof(SnapshotSinkModule));
            Register("snapshot-wide-sink", typeof(SnapshotWideSinkModule));
            Register("snapshot-top-sink", typeof(SnapshotTopSinkModule));
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ViewStateStore ViewState { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void AddSender(string instanceId, double x, double y, double width = InitialWindowWidth, double height = InitialWindowHeight) =>
            Add(instanceId, "test/snapshot-sender", x, y, width, height);

        public void AddSink(string instanceId, double x, double y, double width = InitialWindowWidth, double height = InitialWindowHeight) =>
            Add(instanceId, "test/snapshot-sink", x, y, width, height);

        public void AddWideSink(string instanceId, double x, double y, double width = InitialWindowWidth, double height = InitialWindowHeight) =>
            Add(instanceId, "test/snapshot-wide-sink", x, y, width, height);

        /// <summary>**顶边**接收口模块：验证窄窗里顶部标记的尺寸/落位（窗口宽 > 高时契约仍给顶边）。</summary>
        public void AddTopSink(string instanceId, double x, double y, double width = InitialWindowWidth, double height = InitialWindowHeight) =>
            Add(instanceId, "test/snapshot-top-sink", x, y, width, height);

        public ModuleWindow OpenWindow(ModuleViewViewModel module)
        {
            var window = new ModuleWindow { DataContext = module, Controller = Windows };
            window.Show();
            return window;
        }

        public void Dispose() => Runtime.Dispose();

        private const double InitialWindowWidth = 320;

        private const double InitialWindowHeight = 220;

        private void Add(string instanceId, string moduleIdLiteral, double x, double y, double width, double height)
        {
            if (!Services.AddModule(instanceId, moduleIdLiteral).Succeeded)
            {
                throw new InvalidOperationException($"加入模块失败：{instanceId} / {moduleIdLiteral}");
            }

            Windows.Refresh();

            if (!Services.UpdateModuleLayout(new ModuleLayoutView(
                    instanceId, x, y, width, height)).Succeeded)
            {
                throw new InvalidOperationException($"设置布局失败：{instanceId}");
            }

            Windows.Refresh();
        }

        internal void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
                ModuleId.Create("test", name),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                entryType,
                packageDirectory: string.Empty,
                assemblyPath: string.Empty,
                viewFactory: null,
                windowWidth: 0,
                windowHeight: 0);

            if (!Services.Registry.TryRegister(definition, out var failure))
            {
                throw new InvalidOperationException($"登记快照模块失败：{failure}");
            }
        }
    }

    /// <summary>截图用发送模块（一个发送口）：声明一个“发送”角色（纯源分块，无输入口）。</summary>
    [ModuleDefinition("test", "snapshot-sender")]
    public sealed class SnapshotSenderModule : IModule
    {
        /// <summary>截图用数据类型标识。</summary>
        public const string TypeId = "test/snapshot-sample";

        /// <summary>截图用数据类型（`{seq:u64, value:f32}` = 12B）。</summary>
        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape)
            {
                TypeId = TypeId,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Output(
                "send", TypeId, "out", "发送"));
        }
    }

    /// <summary>截图用接收模块（一个接收口）：声明一个“显示”角色（纯汇分块，无输出口）。</summary>
    [ModuleDefinition("test", "snapshot-sink")]
    public sealed class SnapshotSinkModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(
                new DataTypeDeclaration(SnapshotSenderModule.TypeId, SnapshotSenderModule.Shape));
            context.Declaration.DeclarePort(new PortDeclaration(
                "in", PortDirection.Receive, SnapshotSenderModule.Shape)
            {
                TypeId = SnapshotSenderModule.TypeId,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Input(
                "show", SnapshotSenderModule.TypeId, "in", "显示"));

            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }

    /// <summary>截图用**宽契约**接收模块（与发送器同标签但不兼容，用来验证红边）。</summary>
    [ModuleDefinition("test", "snapshot-wide-sink")]
    public sealed class SnapshotWideSinkModule : IModule
    {
        /// <summary>与发送器不同的类型标识（不同标识 = 不兼容）。</summary>
        public const string TypeId = "test/snapshot-wide-sample";

        /// <summary>不同形状（F64）。</summary>
        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F64);

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Shape)
            {
                TypeId = TypeId,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Input("show", TypeId, "in", "宽显示"));
        }
    }

    /// <summary>截图用**顶边**接收模块：验证窄窗里顶部标记的尺寸与落位。</summary>
    [ModuleDefinition("test", "snapshot-top-sink")]
    public sealed class SnapshotTopSinkModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(
                new DataTypeDeclaration(SnapshotSenderModule.TypeId, SnapshotSenderModule.Shape));
            context.Declaration.DeclarePort(new PortDeclaration(
                "in", PortDirection.Receive, SnapshotSenderModule.Shape)
            {
                TypeId = SnapshotSenderModule.TypeId,
                Edge = PortEdge.Top,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Input(
                "show", SnapshotSenderModule.TypeId, "in", "顶边显示"));

            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }
}
