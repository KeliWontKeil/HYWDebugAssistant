using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Modules.HexSender.Views;
using HYWDebugAssistant.Modules.HexStringConverter.Views;
using HYWDebugAssistant.Modules.Serial.Views;
using HYWDebugAssistant.Modules.TcpClient.Views;
using HYWDebugAssistant.Modules.TcpServer.Views;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Modules.Udp.Views;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **模块自带视图的"真出控件"判据**：串口的 XAML 视图必须真的画出参数与读数。
/// </summary>
/// <remarks>
/// <para>
/// 这条替换的是旧"声明面排布测量"里最值钱的那一句：**参数格真的存在、且带标题**。
/// 它验的不是某个像素，而是"视图被载入了、控件被造出来了、字段名对得上"——
/// 这三件事任何一件错了，窗口上就是一块空白或一格没有标签的框。
/// </para>
/// <para>
/// 它用一个**桩上下文**（不依赖运行时与服务）：视图与控件的接线是纯界面的事，
/// 数据真不真由模块级/端到端用例负责。
/// </para>
/// </remarks>
[Collection("visual-snapshots")]
public class ModuleViewSmokeTests
{
    /// <summary>串口的自带视图应当画出 5 个参数格（带标题）+ 3 格读数 + 状态灯 + 2 个按钮。</summary>
    [Fact]
    public async Task 串口的自带视图_应画出参数格与读数()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var host = new ModuleViewHost(context, new SerialViewFactory().Create(context));

                var window = new Window
                {
                    Width = 400,
                    Height = 190,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();

                // **刷新一次不抛**（取数、外观、配置三路都在这一帧里跑）。
                host.Refresh();
                Pump();

                var fields = window.GetVisualDescendants().OfType<ModuleConfigField>().ToList();
                fields.Count.ShouldBe(5, "串口有五个参数（端口号 / 波特率 / 数据位 / 停止位 / 校验位）");
                fields.Select(field => field.Field).ShouldBe(
                    ["PortName", "BaudRate", "DataBits", "StopBits", "Parity"],
                    ignoreOrder: true);

                // **标题回来了**：标题来自配置类型，不在视图里重复写。
                fields.ShouldAllBe(field => field.FieldTitle.Length > 0, "每个参数格都要显示出字段标题");
                fields.ShouldAllBe(field => field.Notice.Length == 0, "字段名对得上时不该有提示");

                window.GetVisualDescendants().OfType<ModuleNumeric>().Count().ShouldBe(3, "收 / 发 / 丢弃");
                window.GetVisualDescendants().OfType<ModuleStatusLight>().Count().ShouldBe(1);
                window.GetVisualDescendants().OfType<ModuleButton>().Count().ShouldBe(2, "打开 / 关闭");

                // 读数真的显示了值（桩上下文给 42）——"建了控件但从来不画数"是另一类失败，
                // 而"数在、标题不在"同样是一类（标题块造了却没进可视树）。
                var numerics = window.GetVisualDescendants().OfType<ModuleNumeric>().ToList();
                numerics.ShouldAllBe(numeric => numeric.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "42"));
                numerics.ShouldAllBe(numeric => numeric.Title.Length > 0, "读数要有标题（收 / 发 / 丢弃）");

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **布尔字段的编辑器**：一造出来、一挂树就必须能用。
    /// </summary>
    /// <remarks>
    /// 这条钉的是一个真实崩溃：布尔字段曾用 Fluent `ToggleSwitch`，它的模板带一个**必需部件**
    /// （`PART_MovingKnobs`），程序化创建时拿不到就抛 `KeyNotFoundException` ——
    /// 表现是"用到开关的模块一打开窗口就炸"（而不用开关的模块一切正常）。
    /// 现在布尔一律用模块自己的 `ModuleToggle`（轨道 + 圆钮的 `ControlTheme`），
    /// 不再在配置表单里用勾选框、在视图里用另一种开关。
    /// </remarks>
    [Fact]
    public async Task 布尔字段的编辑器_挂树即可用()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var field = new ModuleConfigField { Field = "NoDelay" };
                var host = new ModuleViewHost(context, field);

                var window = new Window { Width = 260, Height = 60, Content = new Border { Child = host } };
                window.Show();
                Pump();

                host.Refresh();
                Pump();

                field.FieldTitle.ShouldBe("禁用 Nagle", "标题来自配置类型");
                field.Notice.ShouldBeEmpty("字段存在时不该有提示");
                var toggle = field.GetVisualDescendants().OfType<ModuleToggle>().ShouldHaveSingleItem();
                toggle.IsChecked.ShouldBeTrue("初值来自配置（true），且布尔一律用统一开关");

                toggle.IsChecked = false;
                Pump();
                context.UpdatedFields.ShouldContain(("NoDelay", "false"), "拨动开关必须走配置写回，而不是只改变本地外观");

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **多行输入逐键写状态**：模块每拍回读的是"用户此刻编辑的内容"（含未提交的编辑）；
    /// 视图重建后这份内容必须回来，不能退回空白。
    /// </summary>
    [Fact]
    public async Task 多行输入_逐键写状态且重建后恢复()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var box = new ModuleTextSurface { Source = "text1", Multiline = true };
                var host = new ModuleViewHost(context, box);
                var window = new Window
                {
                    Width = 300,
                    Height = 200,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                box.Text = "hello";
                Pump();
                context.ReadInputText("text1").ShouldBe("hello", "逐键输入要写进视图状态");

                box.Commit();
                context.Submitted.ShouldHaveSingleItem().Text.ShouldBe("hello");

                // 模拟切页/重建：新控件必须从视图状态恢复用户打了一半的字。
                var rebuilt = new ModuleTextSurface { Source = "text1", Multiline = true };
                var rebuiltHost = new ModuleViewHost(context, rebuilt);
                window.Content = new Border { Child = rebuiltHost };
                Pump();
                rebuiltHost.Refresh();
                Pump();

                rebuilt.Text.ShouldBe("hello", "重建后未提交文本要回来");
                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **配置字段编辑中的文本不被每帧刷新覆盖**：字段值来自事实源，用户正在打的字是本地意图。
    /// </summary>
    [Fact]
    public async Task 配置字段_编辑中的文本不被刷新覆盖()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var field = new ModuleConfigField { Field = "PortName" };
                var host = new ModuleViewHost(context, field);
                var window = new Window
                {
                    Width = 260,
                    Height = 80,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var editor = field.GetVisualDescendants().OfType<TextBox>().ShouldHaveSingleItem();
                editor.Text = "COM9";
                Pump();

                host.Refresh();
                Pump();

                editor.Text.ShouldBe("COM9", "正在编辑的文本不能被字段的旧值拉回");
                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **页签选中页写回视图状态**：模块（发送器/转换器）靠它决定"发哪一页"；
    /// 只活在控件里的选中态会让模块永远只看到第 0 页。
    /// </summary>
    [Fact]
    public async Task 页签选中页_写回状态并在重建后恢复()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var tabs = MakeTabs();
                var host = new ModuleViewHost(context, tabs);
                var window = new Window
                {
                    Width = 300,
                    Height = 200,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                tabs.SelectedIndex = 1;
                Pump();
                context.SelectedPage("pages").ShouldBe(1, "用户切页要写回视图状态");
                window.Close(); Pump();

                // 用**新窗口 + 新控件**模拟"页签内容被重建"，避免依赖同一个窗口的换内容路径。
                var rebuilt = MakeTabs();
                var rebuiltHost = new ModuleViewHost(context, rebuilt);
                var rebuiltWindow = new Window
                {
                    Width = 300,
                    Height = 200,
                    Content = new Border { Child = rebuiltHost },
                };

                rebuiltWindow.Show();
                Pump();
                rebuiltHost.Refresh();
                Pump();

                rebuilt.SelectedIndex.ShouldBe(1, "重建后要回到用户上次选中的页");
                rebuiltWindow.Close();
            },
            CancellationToken.None);

        static ModuleTabs MakeTabs()
        {
            var tabs = new ModuleTabs { StateKey = "pages" };
            tabs.Items.Add(new TabItem { Header = "1", Content = new TextBlock { Text = "1" } });
            tabs.Items.Add(new TabItem { Header = "2", Content = new TextBlock { Text = "2" } });
            return tabs;
        }
    }

    /// <summary>
    /// **运行期外观通道落到看得见的那一块**：模块按源名设的边框色必须出现在输入框自身，
    /// 而不是外层容器上（外层画了内层会再多一圈）。
    /// </summary>
    [Fact]
    public async Task 外观通道_描红落到输入框本身()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var box = new ModuleTextSurface { Source = "text1" };
                var host = new ModuleViewHost(context, box);
                var window = new Window
                {
                    Width = 300,
                    Height = 120,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var editor = box.GetVisualDescendants().OfType<TextBox>().ShouldHaveSingleItem();
                editor.BorderThickness.ShouldBe(default, "内层编辑器的边框固定为 0，普通边框由 PART_Frame 画");

                var ring = box.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_ErrorRing");
                ring.IsVisible.ShouldBeTrue("错误圈必须在焦点/选择边框之上");
                ring.BorderBrush.ShouldBeOfType<SolidColorBrush>().Color
                    .ShouldBe(Color.Parse("#DC2626"));
                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **XAML 预览环境**：没有 App、没有 `Sdk.Ui` 主题时，模块视图基类要自带控件主题
    /// （否则自定义控件没有模板，预览器看起来"什么都没有"）。
    /// </summary>
    [Fact]
    public async Task 模块视图_在无App主题的预览环境自包含控件主题()
    {
        var session = SnapshotSession.BarePreview;

        await session.Dispatch(
            () =>
            {
                var view = new HYWDebugAssistant.Modules.Serial.Views.SerialView
                {
                    Width = 400,
                    Height = 190,
                };
                var window = new Window
                {
                    Width = 400,
                    Height = 190,
                    Content = view,
                };

                window.Show();
                Pump();

                view.GetVisualDescendants().OfType<ModuleLabel>().ShouldNotBeEmpty();
                view.GetVisualDescendants().OfType<TextBlock>().Count()
                    .ShouldBeGreaterThan(0, "控件主题必须在没有 App 的预览环境里也能给出模板");
                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **设计时配置上下文**：四个带配置字段的参数模块，在没有宿主的情况下也要画得出左侧字段。
    /// </summary>
    [Theory]
    [InlineData(typeof(SerialView))]
    [InlineData(typeof(TcpClientView))]
    [InlineData(typeof(TcpServerView))]
    [InlineData(typeof(UdpView))]
    public async Task 设计时配置上下文_参数模块的字段都有编辑器(Type viewType)
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                ModuleView.IsPreviewHost = true;

                // 外部模块的参数类型在框架侧：模拟预览宿主先注册标准标识 → 配置类型映射。
                RegisterPreviewStandardConfigs();

                try
                {
                    // 不手工塞 DataContext：模拟预览宿主，让视图按自身 [ModuleViewDesignData] 建上下文。
                    var view = (Control)Activator.CreateInstance(viewType)!;

                    var window = new Window
                    {
                        Width = 400,
                        Height = 190,
                        Content = view,
                    };

                    window.Show();
                    Pump();

                    var fields = view.GetVisualDescendants().OfType<ModuleConfigField>().ToList();
                    fields.ShouldNotBeEmpty();

                    foreach (var field in fields)
                    {
                        field.FieldTitle.ShouldNotBeNullOrWhiteSpace("字段标题来自配置标注");
                        field.GetVisualDescendants().Any(control => control is TextBox or ComboBox or ModuleToggle)
                            .ShouldBeTrue("每个配置字段都要有可见编辑器");
                    }

                    window.Close();
                    Pump();
                }
                finally
                {
                    ModuleView.IsPreviewHost = false;
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **预览设计数据映射的测试夹具**：只注册生产同款映射。
    /// </summary>
    /// <remarks>
    /// `ModuleView` 的注册表是进程级、覆盖写：本方法可重复调用；VisualSnapshots 是独立测试进程，
    /// 其它用例不依赖"注册表为空"。需要不同映射的用例直接再次 <c>RegisterStandardConfig</c> 覆盖即可，
    /// 因此不需要清理（也不改产品 API）。
    /// </remarks>
    private static void RegisterPreviewStandardConfigs()
    {
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.Serial, typeof(SerialConfig));
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.TcpClient, typeof(TcpClientConfig));
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.TcpServer, typeof(TcpServerConfig));
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.Udp, typeof(UdpConfig));
    }

    /// <summary>
    /// **转换器上下两个输入框必须同宽同高**：页签内容如果按内容宽而不是拉伸，
    /// 上下两个 TabControl 的可用宽度会看起来不同。
    /// </summary>
    [Fact]
    public async Task 转换器上下输入框_同宽同高()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var view = new HexStringConverterView
                {
                    DataContext = new StubViewContext(),
                };
                var window = new Window
                {
                    Width = 400,
                    Height = 300,
                    Content = new Border
                    {
                        Margin = new Thickness(20, 22, 20, 24),
                        Child = view,
                    },
                };

                window.Show();
                Pump();

                var boxes = view.GetVisualDescendants()
                    .OfType<ModuleTextSurface>().Where(surface => surface.Mode == TextSurfaceMode.Input)
                    .Where(box => box.Bounds.Width > 0)
                    .ToList();

                boxes.Count.ShouldBe(2, "转换器上下各只应有一个可见的输入页");

                var hexSurfaces = view.GetVisualDescendants()
                    .OfType<ModuleTextSurface>()
                    .Where(surface => surface.Source.StartsWith("hex", StringComparison.Ordinal))
                    .ToList();
                hexSurfaces.ShouldNotBeEmpty("转换器的 HEX 输入/结果面必须存在");
                hexSurfaces.All(surface => !surface.ShowNewlineMarks).ShouldBeTrue(
                    "HEX 文本不显示换行标记，避免把格式换行误读成数据");

                var first = boxes[0].Bounds;
                var second = boxes[1].Bounds;
                Math.Abs(first.Width - second.Width).ShouldBeLessThan(1.0, "上下输入框必须同宽");
                Math.Abs(first.Height - second.Height).ShouldBeLessThan(1.0, "上下输入框必须同高");

                var logs = view.GetVisualDescendants()
                    .OfType<ModuleTextSurface>().Where(surface => surface.Mode == TextSurfaceMode.Display)
                    .Where(log => log.Bounds.Height > 0)
                    .ToList();
                logs.Count.ShouldBe(2, "转换器上下各只应有一个可见结果区");
                foreach (var log in logs)
                {
                    var bottom = log.TranslatePoint(new Point(0, log.Bounds.Height), view);
                    bottom.ShouldNotBeNull();
                    bottom!.Value.Y.ShouldBeLessThanOrEqualTo(view.Bounds.Height, "结果区不能溢出内容区");
                }

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **输入类型必须有文字标签**：文本发送 = 文本输入；HEX 发送 = HEX 输入；
    /// 转换器上下两半分别标明“文本 → HEX”和“HEX → 文本”。
    /// </summary>
    [Fact]
    public async Task 发送与转换模块_必须有输入类型标签()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var text = new TextSenderView { DataContext = context };
                var hex = new HexSenderView { DataContext = context };
                var converter = new HexStringConverterView { DataContext = context };

                var window = new Window
                {
                    Width = 420,
                    Height = 700,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new Border { Width = 358, Height = 192, Child = text },
                            new Border { Width = 358, Height = 192, Child = hex },
                            new Border { Width = 358, Height = 252, Child = converter },
                        },
                    },
                };

                window.Show();
                Pump();

                string.Join("|", FieldLabels(text)).ShouldBe("文本输入");
                string.Join("|", FieldLabels(hex)).ShouldBe("HEX 输入");
                string.Join("|", FieldLabels(converter)).ShouldBe("文本 → HEX|HEX → 文本");

                var size = new PixelSize(420, 700);
                using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(
                    Path.Combine(Path.GetTempPath(), "hyw-module-field-labels.png"),
                    PngBitmapEncoderOptions.Default);

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>取模块里带 `module-field-label` 类的文字（按视觉树顺序）。</summary>
    private static List<string> FieldLabels(Control view) =>
        view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(block => block.Classes.Contains("module-field-label"))
            .Select(block => block.Text ?? string.Empty)
            .ToList();

    /// <summary>
    /// **页签必须真的换成模块自己的容器主题**：Fluent 的 TabControl 会通过
    /// `ItemContainerTheme` 覆盖 TabItem，只在样式里写 Padding/模板不会生效。
    /// </summary>
    [Fact]
    public async Task 页签_使用模块自己的容器主题()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var view = new TextSenderView
                {
                    DataContext = new StubViewContext(),
                };
                var window = new Window
                {
                    Width = 400,
                    Height = 240,
                    Content = new Border
                    {
                        Margin = new Thickness(20, 22, 20, 24),
                        Child = view,
                    },
                };

                window.Show();
                Pump();

                // **随包文本**：视图的静态文案必须命中模块自己的表（不是 `⟦key⟧`）。
                var viewTexts = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty)
                    .ToList();
                viewTexts.ShouldContain("文本输入", "模块视图文案应命中随包文本表");
                viewTexts.ShouldContain("连续发送", "开关文案应命中随包文本表");
                viewTexts.ShouldNotContain(
                    text => text.Contains('\u27E6'),
                    "随包文本表命中时不得出现可见缺失标记");

                var tabs = view.GetVisualDescendants().OfType<ModuleTabs>().Single();
                tabs.ItemContainerTheme.ShouldNotBeNull("页签容器主题必须落到本地值");
                tabs.ItemContainerTheme!.TargetType.ShouldBe(typeof(TabItem));

                var item = tabs.GetVisualDescendants().OfType<TabItem>().First();
                item.Padding.ShouldBe(new Thickness(7, 1), "模块页签的紧凑内边距必须生效");
                item.MinHeight.ShouldBe(24, "模块页签的固定高度必须生效");

                var underline = item.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_Underline");
                underline.Bounds.Width.ShouldBeGreaterThan(10, "选中下沿必须横过整个页签，不是文字宽度");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>只装 Fluent、**不装 Sdk.Ui** 的预览宿主（模拟 XAML 预览器）。</summary>
    public sealed class BarePreviewApp : Application
    {
        /// <inheritdoc />
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    /// <summary>无 App 主题的预览会话入口。</summary>
    public static class BarePreviewEntryPoint
    {
        /// <summary>按 Avalonia 约定构造宿主。</summary>
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<BarePreviewApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia();
    }

    /// <summary>**输入开关**与布尔配置格共用同一个 `ModuleToggle`；用户拨动只提交一次。</summary>
    [Fact]
    public async Task 输入开关_拨动提交一次()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var toggle = new ModuleToggle { Source = "continuous", Text = "连续发送" };
                var host = new ModuleViewHost(context, toggle);
                var window = new Window
                {
                    Width = 260,
                    Height = 80,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                toggle.IsChecked.ShouldBeTrue("没有输入状态时，显示源作为兼容回退（桩返回 42 ⇒ true）");

                var knob = toggle.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_Knob");
                knob.Transitions.ShouldNotBeNull("滑块必须有拨动过渡");
                knob.Transitions!.Count.ShouldBeGreaterThan(0);

                // 模块把用户拨动后的值写回输入状态；界面必须读这一份，而不是显示源。
                context.WriteInputText("continuous", "false");
                host.Refresh();
                Pump();
                toggle.IsChecked.ShouldBeFalse("输入状态优先于显示源，不能弹回");

                context.WriteInputText("continuous", "true");
                host.Refresh();
                Pump();
                toggle.IsChecked.ShouldBeTrue();

                toggle.IsChecked = false;
                Pump();
                context.Submitted.ShouldContain(("continuous", "false"));
                context.Submitted.Count(entry => entry.Source == "continuous").ShouldBe(1, "程序化/用户拨动只允许提交一次");

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **只读显示面**：文本由 SelectableTextBlock 呈现，可选中/复制，但不是可编辑 TextBox；
    /// 行间分隔是显示层插入的换行，不能画 `↵`。
    /// </summary>
    [Fact]
    public async Task 只读显示面_文本可选中复制_但没有编辑器焦点()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext { TextLines = ["甲", "乙"] };
                var log = new ModuleTextSurface { Source = "log", Mode = TextSurfaceMode.Display };
                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 320,
                    Height = 160,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var input = log.GetVisualDescendants().OfType<TextBox>().ShouldHaveSingleItem();
                input.IsVisible.ShouldBeFalse("显示面不出现可编辑 TextBox");

                var text = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                text.IsVisible.ShouldBeTrue("显示面由只读文本叶子呈现");
                text.Text.ShouldBe($"甲{Environment.NewLine}乙");
                text.SelectionBrush.ShouldNotBe(Brushes.Transparent, "只读文本仍用普通淡蓝选择");

                text.SelectionStart = 0;
                text.SelectionEnd = 1;
                text.SelectedText.ShouldBe("甲", "只读文本仍可选中/复制");
                text.CanCopy.ShouldBeTrue("只读文本可复制");

                text.MarkCount.ShouldBe(0, "日志行间分隔不是数据里的换行字符，不能画 ↵");

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>只读显示叶子也必须按真实换行画 `↵`，自动折行不画。</summary>
    [Fact]
    public async Task 显示面_真实换行有标记_自动折行没有()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext { TextLines = ["甲\n乙"] };
                var log = new ModuleTextSurface { Source = "log", Mode = TextSurfaceMode.Display, Width = 100 };
                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 160,
                    Height = 140,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var marks = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                Pump();
                marks.MarkCount.ShouldBeGreaterThan(0, "显示面真实换行必须有 ↵");

                context.TextLines = [new string('a', 160)];
                host.Refresh();
                Pump();

                marks.MarkCount.ShouldBe(0, "显示面自动折行不能画 ↵");
                marks.VisualLineCount.ShouldBeGreaterThan(1, "160 字符在 100px 宽下必须自动折成多个视觉行");

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **增量性判据**：已有 1000 行时追加 1 行，只允许为新增行建布局，不得重排旧行。
    /// </summary>
    /// <remarks>用 `LayoutBuildCount` 这个可计数的内部量，不做时序敏感的性能断言。</remarks>
    [Fact]
    public async Task 显示面_追加只布局新增行()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var lines = Enumerable.Range(0, 1000)
                    .Select(i => $"{i:D6} " + new string('x', 56))
                    .ToList();

                var context = new StubViewContext { TextLines = lines };
                var log = new ModuleTextSurface { Source = "log", Mode = TextSurfaceMode.Display };
                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 320,
                    Height = 160,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                view.VisualLineCount.ShouldBeGreaterThan(1000, "1000 行应产生至少 1000 个视觉行");
                var before = view.LayoutBuildCount;

                lines.Add("1000 appended line");
                context.TextLines = lines;
                host.Refresh();
                Pump();

                (view.LayoutBuildCount - before).ShouldBeLessThanOrEqualTo(
                    3,
                    "追加一行只允许为新增行建布局，不能重排已有 1000 行");
                view.Text.ShouldContain("1000 appended line");

                // 容量到顶的“删头 + 追尾”不能退回全文重建。
                var beforeRoll = view.LayoutBuildCount;
                lines.RemoveAt(0);
                lines.Add("rolled line");
                context.TextLines = lines;
                host.Refresh();
                Pump();

                (view.LayoutBuildCount - beforeRoll).ShouldBeLessThanOrEqualTo(
                    3,
                    "删头+追尾只允许为新增行建布局，不能重排其余 1000 行");
                view.Text.ShouldNotContain("000000");
                view.Text.ShouldContain("rolled line");

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **真的出像素**：增量显示控件的正文、边框与选择必须画进真实渲染结果。
    /// </summary>
    /// <remarks>无头测试默认可能只布局不绘制；这里用真实 Skia 渲染成 PNG，并数不同颜色（空白窗口只有个位数）。</remarks>
    [Fact]
    public async Task 增量显示面_文本和选择必须真的出像素()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext { TextLines = ["甲\n乙", "第二行"] };
                var log = new ModuleTextSurface
                {
                    Source = "log",
                    Mode = TextSurfaceMode.Display,
                    Width = 280,
                    Height = 140,
                };

                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 320,
                    Height = 180,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                view.MarkCount.ShouldBe(1, "“甲\\n乙”里的真实换行必须有一个 ↵");
                view.SelectionStart = 0;
                view.SelectionEnd = 1;
                Pump();

                var size = new PixelSize(320, 180);
                using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                bitmap.Render(window);

                var path = Path.Combine(Path.GetTempPath(), "hyw-incremental-text.png");
                bitmap.Save(path, PngBitmapEncoderOptions.Default);

                CountDistinctColors(bitmap, size).ShouldBeGreaterThan(
                    20,
                    $"增量正文/选择必须真的出像素（快照：{path}）");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **鼠标拖选**：命中测试必须落在真实字符上；自动折行后的第二行也要落在正确的文本偏移。
    /// </summary>
    [Fact]
    public async Task 增量显示面_鼠标拖选应命中文本()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext { TextLines = [new string('a', 200)] };
                var log = new ModuleTextSurface
                {
                    Source = "log",
                    Mode = TextSurfaceMode.Display,
                    Width = 280,
                    Height = 160,
                };

                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 320,
                    Height = 180,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                view.VisualLineCount.ShouldBeGreaterThan(1, "200 字符在 280px 宽下必须折行");
                var origin = view.TranslatePoint(new Point(0, 0), window);
                origin.ShouldNotBeNull();

                // 直接在第 5 个视觉行（y=60）拖选，证明命中的是折行后的真实文本位置。
                var secondLineY = origin.Value.Y + 60;
                window.MouseDown(new Point(origin.Value.X + 6, secondLineY), MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(new Point(origin.Value.X + 120, secondLineY), RawInputModifiers.None);
                window.MouseUp(new Point(origin.Value.X + 120, secondLineY), MouseButton.Left, RawInputModifiers.None);
                Pump();

                view.SelectedText.ShouldNotBeEmpty("折行后的第二行拖选必须命中字符");
                (view.SelectionStart > 20).ShouldBeTrue(
                    $"第二行的选择偏移必须落在折行后的文本上（实际 {view.SelectionStart}）");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **滚到尾不能变空白**：追加触发自动跟尾后，真实渲染里仍必须看得到文本。
    /// </summary>
    [Fact]
    public async Task 显示面_滚动到末尾后文本仍可见()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var lines = Enumerable.Range(0, 200)
                    .Select(i => $"{i:D4} scroll line " + new string('y', 40))
                    .ToList();

                var context = new StubViewContext { TextLines = lines };
                var log = new ModuleTextSurface
                {
                    Source = "log",
                    Mode = TextSurfaceMode.Display,
                    Width = 280,
                    Height = 140,
                };

                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 320,
                    Height = 180,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var scroll = log.GetVisualDescendants().OfType<ScrollViewer>().ShouldHaveSingleItem();
                var view = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                scroll.Extent.Height.ShouldBeGreaterThan(scroll.Viewport.Height, "200 行必须产生可滚动内容");

                for (var i = 0; i < 20; i++)
                {
                    lines.Add($"tail marker {i}");
                    context.TextLines = lines;
                    host.Refresh();
                    Pump();
                }

                scroll.Offset.Y.ShouldBeGreaterThan(0, "追加后应自动滚到尾");

                var origin = scroll.TranslatePoint(new Point(0, 0), window);
                origin.ShouldNotBeNull();

                var crop = new PixelRect(
                    (int)origin.Value.X + 2,
                    (int)origin.Value.Y + 2,
                    Math.Max(1, (int)scroll.Bounds.Width - 4),
                    Math.Max(1, (int)scroll.Bounds.Height - 4));

                var size = new PixelSize(320, 180);
                using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                bitmap.Render(window);

                var path = Path.Combine(Path.GetTempPath(), "hyw-scrolled-display.png");
                bitmap.Save(path, PngBitmapEncoderOptions.Default);

                CountDistinctColors(bitmap, size, crop).ShouldBeGreaterThan(
                    10,
                    $"滚到尾后文本必须仍可见，不能只剩空白（快照：{path}）");

                var dragStart = new Point(origin.Value.X + 20, origin.Value.Y + 20);
                var dragEnd = new Point(origin.Value.X + 120, origin.Value.Y + 20);
                window.MouseDown(dragStart, MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(dragEnd, RawInputModifiers.None);
                window.MouseUp(dragEnd, MouseButton.Left, RawInputModifiers.None);
                Pump();

                view.SelectedText.ShouldNotBeEmpty("滚动后鼠标拖选仍必须落在真实文本上");

                // 滚到尾后再拉宽：内容高度变小，偏移必须被夹住，文本仍要可见。
                log.Width = 520;
                window.Width = 560;
                Pump();
                Pump();

                var widenedOrigin = scroll.TranslatePoint(new Point(0, 0), window);
                widenedOrigin.ShouldNotBeNull();
                var widenedCrop = new PixelRect(
                    (int)widenedOrigin.Value.X + 2,
                    (int)widenedOrigin.Value.Y + 2,
                    Math.Max(1, (int)scroll.Bounds.Width - 4),
                    Math.Max(1, (int)scroll.Bounds.Height - 4));
                var widenedSize = new PixelSize(560, 180);

                using (var widenedBitmap = new RenderTargetBitmap(widenedSize, new Vector(96, 96)))
                {
                    widenedBitmap.Render(window);
                    var widenedPath = Path.Combine(Path.GetTempPath(), "hyw-scrolled-widened.png");
                    widenedBitmap.Save(widenedPath, PngBitmapEncoderOptions.Default);

                    CountDistinctColors(widenedBitmap, widenedSize, widenedCrop).ShouldBeGreaterThan(
                        10,
                        $"滚尾后拉宽仍必须看得到文本（快照：{widenedPath}）");
                }

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **拉宽后必须按新宽度重排**：视觉行数应减少，文本不能还按旧宽度折行。
    /// </summary>
    [Fact]
    public async Task 显示面_拉宽后自动折行应跟随宽度()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext { TextLines = [new string('a', 240)] };
                var log = new ModuleTextSurface
                {
                    Source = "log",
                    Mode = TextSurfaceMode.Display,
                    Width = 280,
                    Height = 140,
                };

                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 320,
                    Height = 180,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                var narrowLines = view.VisualLineCount;
                narrowLines.ShouldBeGreaterThan(1, "240 字符在 280px 宽下必须折行");

                log.Width = 520;
                window.Width = 560;
                Pump();
                Pump();

                view.Bounds.Width.ShouldBeGreaterThan(480, "控件应真的变宽");
                view.VisualLineCount.ShouldBeLessThan(narrowLines, "拉宽后应减少视觉行，而不是继续按旧宽度折行");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **超长单行的折行必须与一次完整布局一致**：不能在每 512 字符的内部块边界硬断。
    /// </summary>
    [Fact]
    public async Task 显示面_超长单行折行应与一次完整布局一致()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var text = string.Concat(Enumerable.Repeat("1B2E!", 400));
                var context = new StubViewContext { TextLines = [text] };
                var log = new ModuleTextSurface
                {
                    Source = "log",
                    Mode = TextSurfaceMode.Display,
                    Width = 280,
                    Height = 140,
                };

                var host = new ModuleViewHost(context, log);
                var window = new Window
                {
                    Width = 320,
                    Height = 180,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var view = log.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                view.VisualLineCount.ShouldBeGreaterThan(1);

                AssertMatchesIdealWrap(view, text);

                // 继续往同一条超长行追加：也必须跨内部块连续折行。
                var appended = text + string.Concat(Enumerable.Repeat("1B2E!", 200));
                context.TextLines = [appended];
                host.Refresh();
                Pump();
                AssertMatchesIdealWrap(view, appended);

                log.Width = 520;
                window.Width = 560;
                Pump();
                Pump();

                AssertMatchesIdealWrap(view, appended);

                log.Width = 780;
                window.Width = 820;
                Pump();
                Pump();
                AssertMatchesIdealWrap(view, appended);

                var size = new PixelSize(820, 180);
                using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(
                    Path.Combine(Path.GetTempPath(), "hyw-long-line-layout.png"),
                    PngBitmapEncoderOptions.Default);

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>用同一字体/宽度做一次完整 TextLayout，逐行比较字符数。</summary>
    private static void AssertMatchesIdealWrap(IncrementalTextView view, string text)
    {
        var contentWidth = view.Bounds.Width - view.Padding.Left - view.Padding.Right;
        contentWidth.ShouldBeGreaterThan(0, "控件必须已经有真实宽度");

        var ideal = new TextLayout(
            text,
            new Typeface(view.FontFamily, view.FontStyle, view.FontWeight),
            view.FontSize,
            view.Foreground ?? Brushes.Black,
            TextAlignment.Left,
            TextWrapping.Wrap,
            TextTrimming.None,
            null,
            FlowDirection.LeftToRight,
            contentWidth,
            double.PositiveInfinity);

        var idealLengths = ideal.TextLines.Select(line => line.Length).ToArray();
        view.VisualLineLengths.ShouldBe(
            idealLengths,
            "超长单行的折行必须与一次完整布局一致，不能在 512 字符块边界硬断");
    }

    /// <summary>配置文本框只读时也换成文本叶子：可选中/复制，但不是可编辑 TextBox。</summary>
    [Fact]
    public async Task 只读配置文本框_文本可复制但没有编辑器焦点()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext { FieldReadOnly = true };
                var field = new ModuleConfigField { Field = "PortName" };
                var host = new ModuleViewHost(context, field);
                var window = new Window
                {
                    Width = 260,
                    Height = 60,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var input = field.GetVisualDescendants().OfType<TextBox>().ShouldHaveSingleItem();
                input.IsEffectivelyVisible.ShouldBeFalse("只读配置字段不显示可编辑 TextBox");
                input.Focusable.ShouldBeFalse();

                var readText = field.GetVisualDescendants().OfType<SelectableTextBlock>().ShouldHaveSingleItem();
                readText.IsVisible.ShouldBeTrue();
                readText.Text.ShouldBe("COM3");
                readText.SelectionBrush.ShouldNotBe(Brushes.Transparent);
                readText.SelectionStart = 0;
                readText.SelectionEnd = 1;
                readText.SelectedText.ShouldBe("C", "只读配置文本仍可选中/复制");
                readText.CanCopy.ShouldBeTrue();

                var frame = field.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_ReadFrame");
                frame.BorderBrush.ShouldBeOfType<SolidColorBrush>().Color
                    .ShouldBe(Color.Parse("#D4D4D8"));

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **同一个 ModuleTextSurface 的两种叶子**：只读可选中/复制但没有编辑器焦点；
    /// 可写用 TextBox 且可聚焦；两者共用同一模板与边框/错误层。
    /// </summary>
    [Fact]
    public async Task 只读与可写_同一控件不同叶子()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var readOnly = new ModuleTextSurface
                {
                    Source = "text1",
                    Multiline = true,
                    IsReadOnly = true,
                    Text = "甲\n乙",
                };
                var editable = new ModuleTextSurface
                {
                    Source = "text2",
                    Multiline = true,
                    Text = "丙\n丁",
                };
                var host = new ModuleViewHost(
                    context,
                    new StackPanel { Children = { readOnly, editable } });
                var window = new Window
                {
                    Width = 320,
                    Height = 180,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var readOnlyInput = readOnly.GetVisualDescendants().OfType<TextBox>().ShouldHaveSingleItem();
                var readOnlyText = readOnly.GetVisualDescendants().OfType<IncrementalTextView>().ShouldHaveSingleItem();
                var editableInput = editable.GetVisualDescendants().OfType<TextBox>().ShouldHaveSingleItem();

                readOnlyInput.IsVisible.ShouldBeFalse("只读不显示可编辑 TextBox");
                readOnlyInput.Focusable.ShouldBeFalse("只读不进编辑器焦点");
                readOnlyText.IsVisible.ShouldBeTrue("只读由文本叶子呈现");
                editableInput.IsVisible.ShouldBeTrue("可写由 TextBox 呈现");
                editableInput.Focusable.ShouldBeTrue("可写可聚焦");

                var readOnlyFrame = readOnly.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_Frame");
                var editableFrame = editable.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_Frame");

                readOnlyFrame.BorderBrush.ShouldBeOfType<SolidColorBrush>().Color
                    .ShouldBe(Color.Parse("#D4D4D8"), "只读边框走 BorderDefault");
                editableFrame.BorderBrush.ShouldBeOfType<SolidColorBrush>().Color
                    .ShouldBe(Color.Parse("#3F3F46"), "可编辑边框走 SurfaceTextFrameEditableBrush");
                readOnlyFrame.Classes.Contains("readonly").ShouldBeTrue();
                editableFrame.Classes.Contains("readonly").ShouldBeFalse();

                readOnlyText.SelectionBrush.ShouldBeOfType<SolidColorBrush>().Color
                    .ShouldBe(Color.Parse("#2563EB"), "只读文本选择是淡蓝，不是黑色");
                editableInput.SelectionBrush.ShouldBeOfType<SolidColorBrush>().Color
                    .ShouldBe(Color.Parse("#2563EB"), "可写选择也是淡蓝");

                readOnlyText.SelectionStart = 0;
                readOnlyText.SelectionEnd = 1;
                editableInput.SelectionStart = 0;
                editableInput.SelectionEnd = 1;

                readOnlyText.SelectedText.ShouldBe("甲", "只读仍可选中/复制");
                readOnlyText.CanCopy.ShouldBeTrue();
                editableInput.SelectedText.ShouldBe("丙", "可写选择行为不变");

                readOnlyFrame.BorderThickness.ShouldBe(new Thickness(1), "只读 frame 固定 1px");
                editableFrame.BorderThickness.ShouldBe(new Thickness(1), "可编辑 frame 固定 1px");
                editableInput.BorderThickness.ShouldBe(default, "内层 TextBox 不画边框");

                var readOnlyFocus = readOnly.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_FocusRing");
                var editableFocus = editable.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.Name == "PART_FocusRing");

                readOnlyFocus.IsVisible.ShouldBeFalse("只读没有编辑焦点圈");

                editableInput.Focus();
                Pump();
                editableFocus.IsVisible.ShouldBeTrue("可编辑聚焦显示 2px 焦点圈");
                editableFocus.BorderThickness.ShouldBe(new Thickness(2));

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **换行标记只标真实换行字符**：自动折行不画；数据里自己的 `\n` 才画。
    /// </summary>
    [Fact]
    public async Task 换行标记_自动折行不出现_真实换行才出现()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var wrapped = new ModuleTextSurface
                {
                    Source = "text1",
                    Multiline = true,
                    Width = 100,
                    Text = new string('a', 160),
                };
                var explicitBreak = new ModuleTextSurface
                {
                    Source = "text2",
                    Multiline = true,
                    Width = 100,
                    Text = "a\nb",
                };

                var host = new ModuleViewHost(
                    context,
                    new StackPanel { Children = { wrapped, explicitBreak } });
                var window = new Window
                {
                    Width = 220,
                    Height = 160,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var wrappedMarks = wrapped.GetVisualDescendants().OfType<NewlineMarkOverlay>().Single();
                var breakMarks = explicitBreak.GetVisualDescendants().OfType<NewlineMarkOverlay>().Single();
                Pump();

                wrappedMarks.Marks.Count.ShouldBe(0, "自动折行不是数据里的换行字符");
                breakMarks.Marks.Count.ShouldBeGreaterThan(0, "数据里真实的 \\n 必须画 ↵");

                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>**多行输入**同样有统一的换行标记层（输入/日志不是两套实现）。</summary>
    [Fact]
    public async Task 多行输入_显示换行标记()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var box = new ModuleTextSurface
                {
                    Source = "text1",
                    Multiline = true,
                    Text = $"甲{Environment.NewLine}乙",
                };
                var host = new ModuleViewHost(context, box);
                var window = new Window
                {
                    Width = 320,
                    Height = 160,
                    Content = new Border { Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var marks = box.GetVisualDescendants().OfType<NewlineMarkOverlay>().ShouldHaveSingleItem();
                Pump();
                marks.Marks.Count.ShouldBeGreaterThan(0, "输入面的 ↵ 与日志面同一套覆盖层");
                window.Close(); Pump();
            },
            CancellationToken.None);
    }

    /// <summary>**失焦不提交，回车才提交**：填充内容不等于发送动作。</summary>
    [Fact]
    public async Task 文本面_失焦不提交_回车提交一次()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var context = new StubViewContext();
                var box = new ModuleTextSurface { Source = "text1", Text = "abc" };
                var host = new ModuleViewHost(context, box);
                var other = new TextBox();
                var window = new Window
                {
                    Width = 320,
                    Height = 160,
                    Content = new StackPanel { Children = { host, other } },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var input = box.GetVisualDescendants().OfType<TextBox>().Single();
                input.Focus();
                Pump();
                other.Focus();
                Pump();

                context.Submitted.ShouldBeEmpty("失焦只是编辑结束，不是提交动作");

                input.Focus();
                Pump();
                input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Pump();

                context.Submitted.ShouldHaveSingleItem("回车恰好提交一次");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>数一数渲染结果里有多少种不同的颜色（空白窗口只有个位数）；可只统计一块区域。</summary>
    private static int CountDistinctColors(RenderTargetBitmap bitmap, PixelSize size, PixelRect? crop = null)
    {
        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var rect = crop ?? new PixelRect(0, 0, size.Width, size.Height);
        var left = Math.Clamp(rect.X, 0, size.Width);
        var top = Math.Clamp(rect.Y, 0, size.Height);
        var right = Math.Clamp(rect.X + rect.Width, left, size.Width);
        var bottom = Math.Clamp(rect.Y + rect.Height, top, size.Height);
        var seen = new HashSet<int>();

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var i = (y * stride) + (x * 4);
                _ = seen.Add((buffer[i] << 16) | (buffer[i + 1] << 8) | buffer[i + 2]);
            }
        }

        return seen.Count;
    }

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>桩上下文：字段与读数都是固定值（只验接线，不验数据来源）。</summary>
    private sealed class StubViewContext : IModuleViewContext
    {
        private readonly Dictionary<string, string> _inputTexts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _pages = new(StringComparer.Ordinal);

        /// <summary>被 <see cref="SubmitInput"/> 交出去的输入（按顺序）。</summary>
        public List<(string Source, string Text)> Submitted { get; } = [];

        /// <summary>被 <see cref="UpdateField"/> 写回的配置字段（按顺序）。</summary>
        public List<(string Field, string Value)> UpdatedFields { get; } = [];

        /// <summary>显示缓冲返回的行（测试可换；换一次变更号 +1，模拟缓冲真的变了）。</summary>
        public IReadOnlyList<string> TextLines
        {
            get => _textLines;
            set
            {
                _textLines = value;
                _revision++;
            }
        }

        private IReadOnlyList<string> _textLines = ["就绪"];
        private long _revision;

        /// <summary>让 PortName 字段走只读外观（测试用）。</summary>
        public bool FieldReadOnly { get; set; }

        private static readonly ConfigFieldView[] Fields =
        [
            new("PortName", "端口号", ConfigFieldKind.Text, "COM3"),
            new("BaudRate", "波特率", ConfigFieldKind.Enum, "115200",
            [
                new ConfigChoice("115200", "115200"),
                new ConfigChoice("9600", "9600"),
            ]),
            new("DataBits", "数据位", ConfigFieldKind.Enum, "8"),
            new("StopBits", "停止位", ConfigFieldKind.Enum, "1"),
            new("Parity", "校验位", ConfigFieldKind.Enum, "None"),
            new("NoDelay", "禁用 Nagle", ConfigFieldKind.Boolean, "true"),
        ];

        /// <inheritdoc />
        public string InstanceId => "smoke";

        /// <inheritdoc />
        public string MultilineSubmitKey => "CtrlEnter";

        /// <inheritdoc />
        public double? ReadLatest(string source) => 42;

        /// <inheritdoc />
        public IReadOnlyList<double> ReadSeries(string source) => [42];

        /// <inheritdoc />
        public TextBufferSnapshot ReadText(string source) => new(_textLines, 0, 20000, 0, _revision);

        /// <inheritdoc />
        public bool SubmitInput(string source, string text)
        {
            Submitted.Add((source, text));
            return true;
        }

        /// <inheritdoc />
        public CommandOutcome InvokeCommand(string command) => CommandOutcome.Ok;

        /// <inheritdoc />
        public ConfigFieldView? ReadField(string fieldName) =>
            Fields.FirstOrDefault(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal));

        /// <inheritdoc />
        public CommandOutcome UpdateField(string fieldName, string value)
        {
            UpdatedFields.Add((fieldName, value));
            return CommandOutcome.Ok;
        }

        /// <inheritdoc />
        public ViewAppearance Appearance(string source) =>
            string.Equals(source, "text1", StringComparison.Ordinal)
                ? new ViewAppearance { Outline = 0xFFDC2626 }
                : FieldReadOnly && string.Equals(source, "PortName", StringComparison.Ordinal)
                    ? new ViewAppearance { ReadOnly = true }
                    : ViewAppearance.None;

        /// <inheritdoc />
        public string? ReadInputText(string source) =>
            _inputTexts.TryGetValue(source, out var text) ? text : null;

        /// <inheritdoc />
        public void WriteInputText(string source, string text) => _inputTexts[source] = text;

        /// <inheritdoc />
        public int? SelectedPage(string stateKey) =>
            _pages.TryGetValue(stateKey, out var page) ? page : null;

        /// <inheritdoc />
        public void SetSelectedPage(string stateKey, int page) => _pages[stateKey] = page;
    }
}




