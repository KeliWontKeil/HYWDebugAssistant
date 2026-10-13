using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **叠加层渲染冒烟 + 观感快照**：
/// 在**真实渲染栈（Skia）**下把带叠加层的模块窗口画成 PNG。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么要有它</b>：自绘层在**无头空绘制**模式下"画了但没画出来"不会被任何断言发现
///     （`implementation/02` P39 就是这么漏的：箭头从来没被 `DrawGeometry`）。真实渲染栈同时覆盖
///     ① 渲染不抛异常；② 字/线/方块**真的出像素**。渲染自检还当场抓到了"结构文字与左下角运行指示叠字"、
///     "`未连` 标签压到端口标记带"两处观感缺陷 —— 这类问题**单元断言看不见**。</item>
///   <item><b>产物</b>：每个窗口一张 PNG（<c>%TEMP%\hyw-overlay-snapshot\</c>）——调 UI 时直接打开看，
///     不必启动整个应用、不必手点叠加层开关。</item>
///   <item><b>为什么单独一个测试项目</b>：`AvaloniaHeadlessPlatform` 的配置是**进程级**的
///     （`UseHeadlessDrawing` 初始化后不可换）⇒ 与主测试项目的"空绘制"会话同进程会互相破坏（实测 13 项集体失败）。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class OverlaySnapshotTests
{
    /// <summary>快照输出目录（临时目录，便于直接打开看）。</summary>
    public static string OutputDirectory => Path.Combine(Path.GetTempPath(), "hyw-overlay-snapshot");

    [Fact]
    public async Task 叠加层_应在真实渲染栈下画出方块与连线_并留下快照()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SkiaHeadlessEntryPoint));

        if (Directory.Exists(OutputDirectory))
        {
            Directory.Delete(OutputDirectory, recursive: true);
        }

        _ = Directory.CreateDirectory(OutputDirectory);

        await session.Dispatch(
            () =>
            {
                // 照"三窗布局"（发送器在上、两个接收器在下）摆一场：覆盖多对端分叉、未连接残段、信息区三档字号
                using var harness = new SnapshotHarness();

                harness.AddSender("sender", x: 260, y: 40);
                harness.AddSink("m1", x: 40, y: 380);
                harness.AddSink("m2", x: 480, y: 380);
                harness.AddSender("lonely", x: 700, y: 40);   // 未连接 ⇒ 方块照画，`m/n` 显示 0/--（无「未连」文字）

                harness.Services.UpdatePortNetwork("sender", "out", "bus").Succeeded.ShouldBeTrue();
                harness.Services.UpdatePortNetwork("m1", "in", "bus").Succeeded.ShouldBeTrue();
                harness.Services.UpdatePortNetwork("m2", "in", "bus").Succeeded.ShouldBeTrue();

                harness.Windows.ShowInfoOverlay = true;
                harness.Windows.Refresh();

                foreach (var module in harness.Windows.Modules)
                {
                    var window = harness.OpenWindow(module);
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();

                    var size = new PixelSize(
                        Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
                        Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

                    using var bitmap = new RenderTargetBitmap(size);
                    bitmap.Render(window);

#pragma warning disable CS0618   // 快照工具：旧的 Save(string) 足够
                    bitmap.Save(Path.Combine(OutputDirectory, $"{module.InstanceId}.png"));
#pragma warning restore CS0618
                }
            },
            CancellationToken.None);

        foreach (var name in new[] { "sender", "m1", "m2", "lonely" })
        {
            var file = Path.Combine(OutputDirectory, name + ".png");
            File.Exists(file).ShouldBeTrue($"应留下快照：{file}");
            new FileInfo(file).Length.ShouldBeGreaterThan(1000, "快照不能是空白图");
        }
    }

    [Fact]
    public async Task 虚线偏移的符号约定_正偏移把图案推向起点_且单位是笔宽倍数()
    {
        // 虚线流动方向**输入/输出均为中心块向外**（流向取反是实测过的缺陷）：
        // 这条把 Avalonia/Skia 的 `DashStyle.Offset` 语义**实测出来**（不靠文档、不靠注释）：
        // ① **偏移增大 ⇒ 图案整体朝几何起点移动**（因此"沿绘制方向前进"必须取**负**相位）；
        // ② 偏移的**单位是笔宽的倍数**（不是 DIP）—— 见 `OverlayAnimationClock.DashOffset` 里为什么要除以线宽。
        using var session = HeadlessUnitTestSession.StartNew(typeof(SkiaHeadlessEntryPoint));

        const double thickness = 2;
        var starts = new Dictionary<double, int>();

        await session.Dispatch(
            () =>
            {
                foreach (var offset in new[] { 2.0, 0.0, -2.0 })
                {
                    starts[offset] = SecondDashStart(offset, thickness);
                }
            },
            CancellationToken.None);

        starts[2.0].ShouldBeLessThan(starts[0.0], "偏移增大 ⇒ 图案朝**起点**移动（第二段起点左移）");
        starts[-2.0].ShouldBeGreaterThan(starts[0.0], "偏移减小 ⇒ 图案朝**终点**移动");

        // 单位 = 笔宽倍数：δ=2、笔宽 2 ⇒ 位移 4 DIP（而不是 2 DIP）
        (starts[0.0] - starts[2.0]).ShouldBe((int)(2 * thickness), "偏移以**笔宽**为单位（WPF/Skia 口径）");
    }

    [Fact]
    public async Task 虚线相位走满一个周期_画面必须与零相位逐像素相同()
    {
        // 【用户第三十四轮实测反馈："一段平滑的动画后突然跳一下，就像一根匀速旋转的指针转到 270° 突然跳回 0°"】
        // 根因：图案周期与偏移的**单位族**没对齐（旧实现按 `段长+间隙` 个 DIP 环绕，却把偏移换算成"笔宽倍数"
        // ⇒ 每轮只推进 10÷2.5 = 4 个图案单位就回绕 ⇒ 每次回绕倒跳 6 个图案单位 = 15 DIP）。
        // 本用例是**唯一能证明"没有跳"的方式**：把相位推满**一个真实图案周期**后，
        // 渲染出来的像素必须与零相位**完全一致**（环绕点视觉上不可分辨）。
        using var session = HeadlessUnitTestSession.StartNew(typeof(SkiaHeadlessEntryPoint));

        const double thickness = ConnectionOverlay.LineThickness;
        var pixels = new Dictionary<double, string>();

        await session.Dispatch(
            () =>
            {
                // 零相位 vs 恰好一个周期后的偏移（`Period` = (段长+间隙) × 线宽）
                pixels[0] = DashRow(OverlayAnimationClock.DashOffsetAt(0), thickness);
                pixels[1] = DashRow(OverlayAnimationClock.DashOffsetAt(OverlayAnimationClock.Period), thickness);

                // 对照：只推进"段长+间隙"个 DIP（旧口径的错误环绕点）⇒ 必须**不一样**（否则本用例没有意义）
                pixels[2] = DashRow(
                    OverlayAnimationClock.DashOffsetAt(ConnectionOverlay.DashLength + ConnectionOverlay.DashGap),
                    thickness);
            },
            CancellationToken.None);

        pixels[1].ShouldBe(pixels[0], "一个**真实图案周期**之后必须逐像素相同 ⇒ 环绕处看不出跳变");
        pixels[2].ShouldNotBe(pixels[0], "对照：按旧口径（少算线宽）环绕会错位 —— 这正是用户看到的'跳一下'");
    }

    /// <summary>把一条水平虚线（含负号偏移，8 位采样）画出来，返回"每一列是否有墨"的字符串。</summary>
    private static string DashRow(double dashOffset, double thickness)
    {
        const int width = 90;
        const int height = 8;

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));

        using (var context = bitmap.CreateDrawingContext())
        {
            var pen = new Pen(Brushes.Black, thickness)
            {
                DashStyle = new DashStyle([ConnectionOverlay.DashLength, ConnectionOverlay.DashGap], dashOffset),
                LineCap = PenLineCap.Flat,
            };

            context.DrawLine(pen, new Point(0, height / 2.0), new Point(width, height / 2.0));
        }

        var stride = width * 4;
        var size = stride * height;
        var native = Marshal.AllocHGlobal(size);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), native, size, stride);

            var buffer = new byte[size];
            Marshal.Copy(native, buffer, 0, size);

            var row = height / 2;
            var text = new System.Text.StringBuilder(width);

            for (var x = 0; x < width; x++)
            {
                var index = ((row * width) + x) * 4;
                var inked = buffer[index] > 0 || buffer[index + 1] > 0 || buffer[index + 2] > 0 || buffer[index + 3] > 0;
                _ = text.Append(inked ? '#' : '.');
            }

            return text.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    /// <summary>画一条水平虚线（给定偏移与笔宽），返回**第二段虚线**的起始列。</summary>
    /// <remarks>
    /// 用"第二段的起点"而不是"第一段"：第一段可能被偏移吃掉或被推到线外，起点位置不单调；
    /// 第二段起点随偏移**单调移动** ⇒ 能直接读出图案的位移方向与位移量。
    /// </remarks>
    private static int SecondDashStart(double dashOffset, double thickness)
    {
        const int width = 90;
        const int height = 8;

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));

        using (var context = bitmap.CreateDrawingContext())
        {
            var pen = new Pen(Brushes.Black, thickness)
            {
                DashStyle = new DashStyle([ConnectionOverlay.DashLength, ConnectionOverlay.DashGap], dashOffset),
                LineCap = PenLineCap.Flat,
            };

            context.DrawLine(pen, new Point(0, height / 2.0), new Point(width, height / 2.0));
        }

        var stride = width * 4;
        var size = stride * height;
        var native = Marshal.AllocHGlobal(size);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), native, size, stride);

            var pixels = new byte[size];
            Marshal.Copy(native, pixels, 0, size);

            var row = height / 2;
            var runs = 0;
            var previousInked = false;

            for (var x = 0; x < width; x++)
            {
                var index = ((row * width) + x) * 4;

                // 像素格式不写死：BGRA 预乘下 alpha 在末字节，非预乘在前 ⇒ 四个字节里**任一非零**即"有墨"
                var inked = pixels[index] > 0 || pixels[index + 1] > 0 || pixels[index + 2] > 0 || pixels[index + 3] > 0;

                if (inked && !previousInked)
                {
                    runs++;

                    if (runs == 2)
                    {
                        return x;
                    }
                }

                previousInked = inked;
            }

            return width;
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    /// <summary>快照用发送模块（一个发送口）：**声明一个"发送"角色**（纯源分块，无输入口）。</summary>
    [ModuleDefinition("test", "snapshot-sender", "1.0.0")]
    public sealed class SnapshotSenderModule : IModule
    {
        /// <summary>快照用数据类型标识。</summary>
        public const string TypeId = "test/snapshot-sample";

        /// <summary>快照用数据类型（`{seq:u64, value:f32}` = 12B）。</summary>
        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, 1, Shape));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape)
            {
                Type = new DataTypeRef(TypeId, 1),
            });

            context.Declaration.DeclareRole(RoleDeclaration.Output(
                "send", new DataTypeRef(TypeId, 1), "out", "发送"));
        }
    }

    /// <summary>快照用接收模块（一个接收口）：**声明一个"显示"角色**（纯汇分块，无输出口）。</summary>
    [ModuleDefinition("test", "snapshot-sink", "1.0.0")]
    public sealed class SnapshotSinkModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(
                new DataTypeDeclaration(SnapshotSenderModule.TypeId, 1, SnapshotSenderModule.Shape));
            context.Declaration.DeclarePort(new PortDeclaration(
                "in", PortDirection.Receive, SnapshotSenderModule.Shape)
            {
                Type = new DataTypeRef(SnapshotSenderModule.TypeId, 1),
            });

            context.Declaration.DeclareRole(RoleDeclaration.Input(
                "show", new DataTypeRef(SnapshotSenderModule.TypeId, 1), "in", "显示"));

            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }

    /// <summary>
    /// 快照用"多页标签页"模块（**G1**）：3 页，**每页内容文字不同** ⇒ 切页后画面必须真的变。
    /// </summary>
    /// <remarks>
    /// 视图在**包内 `view.json`**（`SnapshotViewPackages.Tabs`）：`container` 根 → `tabs` → 3 个 `group`，
    /// 每个 `group` 里一个 `textBox`。模块代码只声明数据面 —— 与正式模块同构。
    /// 无端口、无角色 —— 标签页与叠加层无关，本条只为"真出像素"服务。
    /// </remarks>
    [ModuleDefinition("test", "snapshot-tabs", "1.0.0")]
    public sealed class SnapshotTabsModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            _ = context;
        }
    }

    /// <summary>
    /// 快照用"一块内容面板"模块：**每种控件都真出像素**（新内容机制的自检夹具）。
    /// </summary>
    /// <remarks>
    /// 视图在包内 `view.json`（<c>SnapshotViewPackages.Panel</c>）：可折叠区里一块日志、一个数值读数、
    /// 一个文本框、一个命令按钮；旁边一个声明了但未渲染的 `statusLight`（走占位控件）。
    /// 无端口、无角色 —— 本条只为"声明面必须变成控件"服务。
    /// </remarks>
    [ModuleDefinition("test", "snapshot-panel", "1.0.0")]
    public sealed class SnapshotPanelModule : IModule, IActivitySource
    {
        /// <summary>夹具发布的数据源名（与包内 `view.json` 的 `value` 同名）。</summary>
        public const string ValueSource = "value";

        /// <inheritdoc />
        public ActivityKind Kind => ActivityKind.Periodic;

        /// <inheritdoc />
        public TimeSpan Interval => TimeSpan.FromMilliseconds(20);

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) => _ = context;

        /// <inheritdoc />
        public void RunActivity(IModuleContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            while (!context.StopSignal.IsStopRequested)
            {
                context.Ports.Publish(ValueSource, BitConverter.GetBytes((double)42.5));
                _ = context.StopSignal.Wait(Interval);
            }
        }
    }

    /// <summary>
    /// 让无头会话使用**真实渲染栈**（`UseHeadlessDrawing = false` + Skia）——否则 `RenderTargetBitmap` 出不了像素。
    /// </summary>
    public static class SkiaHeadlessEntryPoint
    {
        /// <summary>会话入口：按 Avalonia 约定提供 <c>BuildAvaloniaApp</c>。</summary>
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<SnapshotApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia();
    }

    /// <summary>快照现场（复用生产 VM 与真实模块窗口）。</summary>
    /// <remarks>`internal`（非 private）：**G1 的标签条快照**在 `TabStripSnapshotTests` 里复用它，避免复制第二份现场。</remarks>
    internal sealed class SnapshotHarness : IDisposable
    {
        public SnapshotHarness()
        {
            Runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
            Services = new HYWDebugAssistant.Core.Services.RuntimeServices(Runtime);
            ViewState = new HYWDebugAssistant.Core.Services.ViewStateStore();
            Windows = new ModuleWindowsViewModel(Services, Services, ViewState);

            // 视图（含端口位置）只有一个来源 = 包内 `view.json`；代码里已无视图声明 API。
            Register("snapshot-sender", typeof(SnapshotSenderModule), SnapshotViewPackages.Sender);
            Register("snapshot-sink", typeof(SnapshotSinkModule), SnapshotViewPackages.Sink);
            Register("snapshot-tabs", typeof(SnapshotTabsModule), SnapshotViewPackages.Tabs);
            Register("snapshot-panel", typeof(SnapshotPanelModule), SnapshotViewPackages.Panel);
        }

        public HYWDebugAssistant.Core.Runtime.ModuleRuntime Runtime { get; }

        public HYWDebugAssistant.Core.Services.RuntimeServices Services { get; }

        /// <summary>视图状态（显示缓冲 / 未提交文本 / 折叠与选页）——与窗口视图模型共用同一个实例。</summary>
        public HYWDebugAssistant.Core.Services.ViewStateStore ViewState { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void AddSender(string instanceId, double x, double y) => Add(instanceId, "test/snapshot-sender/1.0.0", x, y);

        public void AddSink(string instanceId, double x, double y) => Add(instanceId, "test/snapshot-sink/1.0.0", x, y);

        /// <summary>加入一个"多页标签页"模块（G1 快照用）。</summary>
        public void AddTabs(string instanceId, double x, double y) => Add(instanceId, "test/snapshot-tabs/1.0.0", x, y);

        /// <summary>加入一个"每种控件都来一份"模块（新内容机制的自检夹具）。</summary>
        public void AddPanel(string instanceId, double x, double y) => Add(instanceId, "test/snapshot-panel/1.0.0", x, y);

        /// <summary>往某实例的显示缓冲里追加一行（走模块侧同一条"请求 → 落地"通道）。</summary>
        public void AppendDisplayLine(string instanceId, string sourceName, string text) =>
            ViewState.RequestAppend(instanceId, sourceName, text, lineBreak: true);

        public ModuleWindow OpenWindow(ModuleViewViewModel module)
        {
            var window = new ModuleWindow { DataContext = module, Controller = Windows };
            window.Show();
            return window;
        }

        public void Dispose() => Runtime.Dispose();

        /// <summary>布局用的初始尺寸（这些夹具包没有在 `frame.window` 里声明尺寸 ⇒ 用 schema 缺省 320×220）。</summary>
        private const double InitialWindowWidth = 320;

        /// <summary>布局用的初始高度（同 <see cref="InitialWindowWidth"/>）。</summary>
        private const double InitialWindowHeight = 220;

        private void Add(string instanceId, string moduleIdLiteral, double x, double y)
        {
            Services.AddModule(instanceId, moduleIdLiteral).Succeeded.ShouldBeTrue();
            Windows.Refresh();

            Services.UpdateModuleLayout(new ModuleLayoutView(
                instanceId, x, y, InitialWindowWidth, InitialWindowHeight))
                .Succeeded.ShouldBeTrue();

            Windows.Refresh();
        }

        internal void Register(string name, Type entryType, string packageDirectory)
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
                packageDirectory,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }
    }

    /// <summary>夹具用的临时包目录（**视图只有一个来源 = 包内 `view.json`**，与正式模块同构）。</summary>
    /// <remarks>`frame.ports[].edge` 必填（标记几何与可拖动性都是框架固定项，声明面没有开关）。</remarks>
    internal static class SnapshotViewPackages
    {
        /// <summary>发送模块的包（一个 `numeric` 显示 `value`）。</summary>
        public static string Sender { get; } = Write(
            "hyw-snapshot-sender-package",
            """
            {
              "version": 1,
              "frame": { "ports": [ { "name": "out", "edge": "right", "offset": 0.5 } ] },
              "sources": [ { "name": "value", "kind": "Value", "valueType": "F64" } ],
              "views": [
                { "root": { "kind": "container", "children": [ { "kind": "numeric", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "data": "value", "width": { "fill": true }, "height": { "pixels": 24 } } ] } }
              ]
            }
            """);

        /// <summary>接收模块的包（一个 `numeric` 显示 `value`）。</summary>
        public static string Sink { get; } = Write(
            "hyw-snapshot-sink-package",
            """
            {
              "version": 1,
              "frame": { "ports": [ { "name": "in", "edge": "left", "offset": 0.5 } ] },
              "sources": [ { "name": "value", "kind": "Value", "valueType": "F64" } ],
              "views": [
                { "root": { "kind": "container", "children": [ { "kind": "numeric", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "data": "value", "width": { "fill": true }, "height": { "pixels": 24 } } ] } }
              ]
            }
            """);

        /// <summary>标签页模块的包（`tabs` 3 页，每页一个 `textBox`，内容文字不同）。</summary>
        public static string Tabs { get; } = Write(
            "hyw-snapshot-tabs-package",
            """
            {
              "version": 1,
              "sources": [
                { "name": "page0", "kind": "Text" },
                { "name": "page1", "kind": "Text" },
                { "name": "page2", "kind": "Text" }
              ],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "children": [
                      {
                        "kind": "tabs",
                        "title": "消息",
                        "children": [
                          { "kind": "group", "title": "第 1 页", "children": [ { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "title": "第一页输入", "input": "page0", "width": { "fill": true }, "height": { "pixels": 92 } } ] },
                          { "kind": "group", "title": "第 2 页", "children": [ { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "title": "第二页输入", "input": "page1", "width": { "fill": true }, "height": { "pixels": 92 } } ] },
                          { "kind": "group", "title": "第 3 页", "children": [ { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "title": "第三页输入", "input": "page2", "width": { "fill": true }, "height": { "pixels": 92 } } ] }
                        ]
                      }
                    ]
                  }
                }
              ]
            }
            """);

        /// <summary>
        /// 内容面板模块的包：**可折叠区 + 文本面 + 数值读数 + 文本框 + 命令按钮 + 一个未渲染的 statusLight**。
        /// </summary>
        /// <remarks>按钮与文本框都绑了名字 ⇒ 快照用例可以真点、真打字，验证"声明 → 控件 → 提交"整条链。</remarks>
        public static string Panel { get; } = Write(
            "hyw-snapshot-panel-package",
            """
            {
              "version": 1,
              "sources": [
                { "name": "value", "kind": "Value", "valueType": "F64" },
                { "name": "log", "kind": "Text" },
                { "name": "text1", "kind": "Text" }
              ],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "children": [
                      {
                        "kind": "collapsible",
                        "title": "详情",
                        "stateKey": "detail",
                        "children": [
                          { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "readonly": true, "title": "日志", "data": "log", "width": { "fill": true }, "height": { "fill": true, "min": 88 } }
                        ]
                      },
                      { "kind": "numeric", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "data": "value", "width": { "fill": true }, "height": { "pixels": 24 } },
                      { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "title": "输入", "input": "text1", "multiline": false, "width": { "fill": true }, "height": { "pixels": 28 } },
                      { "kind": "commandButton", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "text": "执行", "command": "noop", "width": { "pixels": 88 }, "height": { "pixels": 28 } },
                      { "kind": "statusLight", "title": "灯", "data": "value", "width": { "fill": true }, "height": { "pixels": 24 } }
                    ]
                  }
                }
              ]
            }
            """);

        internal static string Write(string directoryName, string document)        {
            var directory = Path.Combine(Path.GetTempPath(), directoryName);
            _ = System.IO.Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "view.json"), document);
            return directory;
        }
    }
}
