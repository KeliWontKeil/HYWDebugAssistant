using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using SkiaSharp;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Modules.AttitudeDisplay;
using HYWDebugAssistant.Modules.AttitudeDisplay.Views;
using HYWDebugAssistant.Modules.CommandSender;
using HYWDebugAssistant.Modules.CommandSender.Views;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.DataMonitor.Views;
using HYWDebugAssistant.Modules.GaugeDisplay;
using HYWDebugAssistant.Modules.GaugeDisplay.Views;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.HexSender.Views;
using HYWDebugAssistant.Modules.HexStringConverter;
using HYWDebugAssistant.Modules.HexStringConverter.Views;
using HYWDebugAssistant.Modules.ImageFrame;
using HYWDebugAssistant.Modules.ImageFrame.Views;
using HYWDebugAssistant.Modules.ScaleBarHorizontal;
using HYWDebugAssistant.Modules.ScaleBarHorizontal.Views;
using HYWDebugAssistant.Modules.ScaleBarVertical;
using HYWDebugAssistant.Modules.ScaleBarVertical.Views;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.Serial.Views;
using HYWDebugAssistant.Modules.TcpClient;
using HYWDebugAssistant.Modules.TcpClient.Views;
using HYWDebugAssistant.Modules.TcpServer;
using HYWDebugAssistant.Modules.TcpServer.Views;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Modules.Udp;
using HYWDebugAssistant.Modules.Udp.Views;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;
using HorizontalSettingsPanel = HYWDebugAssistant.Modules.ScaleBarHorizontal.Views.ScaleBarSettingsPanel;
using VerticalSettingsPanel = HYWDebugAssistant.Modules.ScaleBarVertical.Views.ScaleBarSettingsPanel;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **八个模块视图的截图**：真实载入 XAML、按模块声明的窗口尺寸渲染，每个模块一张（页签逐页再各一张）。
/// </summary>
/// <remarks>
/// 只负责“把它画出来”。视图好不好看、字段挤不挤、文案装不装得下，由人眼看
/// <c>artifacts\snapshots\modules\</c> 下的图判断。
/// </remarks>
[Collection("visual-snapshots")]
public class ModuleViewSnapshots
{
    private sealed class NoConfig;

    /// <summary>八个业务模块：入口类型 / 视图工厂 / 配置类型（没有配置给 <see cref="NoConfig"/>）。</summary>
    public static TheoryData<Type, Type, Type> Modules => new()
    {
        { typeof(SerialModule), typeof(SerialViewFactory), typeof(SerialConfig) },
        { typeof(TcpClientModule), typeof(TcpClientViewFactory), typeof(TcpClientConfig) },
        { typeof(TcpServerModule), typeof(TcpServerViewFactory), typeof(TcpServerConfig) },
        { typeof(UdpModule), typeof(UdpViewFactory), typeof(UdpConfig) },
        { typeof(TextSenderModule), typeof(TextSenderViewFactory), typeof(NoConfig) },
        { typeof(CommandSenderModule), typeof(CommandSenderViewFactory), typeof(NoConfig) },
        { typeof(HexSenderModule), typeof(HexSenderViewFactory), typeof(NoConfig) },
        { typeof(DataMonitorModule), typeof(DataMonitorViewFactory), typeof(NoConfig) },
        { typeof(HexStringConverterModule), typeof(HexStringConverterViewFactory), typeof(NoConfig) },
        { typeof(GaugeDisplayModule), typeof(GaugeDisplayViewFactory), typeof(NoConfig) },
        { typeof(ScaleBarHorizontalModule), typeof(ScaleBarHorizontalViewFactory), typeof(NoConfig) },
        { typeof(ScaleBarVerticalModule), typeof(ScaleBarVerticalViewFactory), typeof(NoConfig) },
        { typeof(AttitudeDisplayModule), typeof(AttitudeDisplayViewFactory), typeof(NoConfig) },
        { typeof(ImageFrameModule), typeof(ImageFrameViewFactory), typeof(NoConfig) },
    };

    /// <summary>四个终端显示模块的设置面板正文（独立视图，单独渲染成人眼可核对的图）。</summary>
    public static TheoryData<Type, string> SettingsPanels => new()
    {
        { typeof(GaugeSettingsPanel), "GaugeDisplay" },
        { typeof(HorizontalSettingsPanel), "ScaleBarHorizontal" },
        { typeof(VerticalSettingsPanel), "ScaleBarVertical" },
        { typeof(AttitudeSettingsPanel), "AttitudeDisplay" },
    };

    /// <summary>
    /// 生成设置面板正文的截图（<c>modules\&lt;模块&gt;-settings.png</c>）：面板不进模块视图，
    /// 因此单独按自己的声明尺寸渲染；标题行用运行期同一套 <c>ModulePanelSurface</c>。
    /// </summary>
    /// <param name="panelType">面板正文类型。</param>
    /// <param name="name">模块名（文件名用）。</param>
    [Theory]
    [MemberData(nameof(SettingsPanels))]
    public async Task 生成快照_设置面板正文(Type panelType, string name)
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                // 按预览环境渲染（分段控件的 PreviewOptions 才会出现），但不画逐控件范围框：
                // 这张图要看的是表单本身，不是预览器的定位框。
                var previous = ModuleView.IsPreviewHost;
                ModuleView.IsPreviewHost = true;

                try
                {
                    var panel = (Control)Activator.CreateInstance(panelType)!;
                    var view = (ModuleView)panel;
                    foreach (var control in panel.GetLogicalDescendants().OfType<Control>())
                    {
                        PreviewBounds.SetShow(control, false);
                    }

                    var width = view.InitialWidth > 0 ? view.InitialWidth : 320;
                    var height = view.InitialHeight > 0 ? view.InitialHeight : 360;

                    var surface = new ModulePanelSurface
                    {
                        Title = TextCatalog.Get(panelType.Assembly, "view.settings"),
                        IconOwner = panelType.Assembly,
                        Content = panel,
                    };

                    var window = new Window
                    {
                        Width = width + 2,
                        Height = height + ModulePanelSurface.HeaderHeight + 2,
                        Content = surface,
                    };

                    window.Show();
                    SnapshotTool.Pump();
                    SnapshotTool.Pump();
                    SnapshotTool.Save(window, "modules/" + name + "-settings.png");
                    window.Close();
                }
                finally
                {
                    ModuleView.IsPreviewHost = previous;
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 生成设置面板**预览**截图（<c>modules\&lt;模块&gt;-settings-preview.png</c>）：正文不套模块窗口预留带，
    /// 但必须保留控件虚线/容器点线范围框。
    /// </summary>
    /// <param name="panelType">面板正文类型。</param>
    /// <param name="name">模块名（文件名用）。</param>
    [Theory]
    [MemberData(nameof(SettingsPanels))]
    public async Task 生成快照_设置面板预览(Type panelType, string name)
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var previous = ModuleView.IsPreviewHost;
                ModuleView.IsPreviewHost = true;

                try
                {
                    var panel = (Control)Activator.CreateInstance(panelType)!;
                    var view = (ModuleView)panel;

                    var width = view.InitialWidth > 0 ? view.InitialWidth : 320;
                    var height = view.InitialHeight > 0 ? view.InitialHeight : 360;

                    var window = new Window
                    {
                        Width = width,
                        Height = height,
                        Content = panel,
                    };

                    window.Show();
                    SnapshotTool.Pump();
                    SnapshotTool.Pump();

                    SnapshotTool.Save(window, "modules/" + name + "-settings-preview.png");
                    window.Close();
                }
                finally
                {
                    ModuleView.IsPreviewHost = previous;
                }
            },
            CancellationToken.None);
    }
    /// <summary>生成某模块的视图截图（<c>modules\&lt;模块&gt;.png</c> + 逐页签）。</summary>
    /// <param name="moduleType">模块入口类型。</param>
    /// <param name="factoryType">视图工厂类型。</param>
    /// <param name="configType">配置类型。</param>
    [Theory]
    [MemberData(nameof(Modules))]
    public async Task 生成快照_模块视图(Type moduleType, Type factoryType, Type configType)
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var probe = new TestModuleContext();
                var module = (IModule)Activator.CreateInstance(moduleType)!;
                module.OnRegistered(probe);

                var fields = BuildFields(configType, moduleType.Assembly);
                var context = new SnapshotContext(probe, fields);

                var factory = (IModuleViewFactory)Activator.CreateInstance(factoryType)!;
                var view = factory.Create(context);
                var host = new ModuleViewHost(context, view);

                // 截图窗口 = 视图声明的实际窗口尺寸（含外框）；真实窗口按同一份尺寸。
                var (width, height) = WindowSize(view);

                var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                window.Show();
                SnapshotTool.Pump();
                host.Refresh();
                SnapshotTool.Pump();

                var name = moduleType.Name.Replace("Module", string.Empty, StringComparison.Ordinal);
                SnapshotTool.Save(window, "modules/" + name + ".png");

                // 页签内容按需实例化：逐页切一次并各留一张图。
                foreach (var tabs in window.GetVisualDescendants().OfType<ModuleTabs>().ToList())
                {
                    for (var index = 0; index < tabs.ItemCount; index++)
                    {
                        tabs.SelectedIndex = index;
                        SnapshotTool.Pump();
                        host.Refresh();
                        SnapshotTool.Pump();

                        SnapshotTool.Save(window, $"modules/{name}-tab{index}.png");
                    }
                }

                window.Close();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 生成指令发送器**带真实模块状态**的截图（<c>modules\CommandSender-filled.png</c>）：
    /// 两行、勾选顺序 2/1、HEX 行、状态栏，用来人眼复核行内控件与序号标记。
    /// </summary>
    [Fact]
    public async Task 生成快照_指令发送器列表()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var probe = new TestModuleContext();
                var module = new CommandSenderModule();
                module.OnRegistered(probe);
                module.OnStep(probe, StepReason.Start);

                probe.EnqueueCommand(CommandSenderModule.AddCommand);
                module.OnStep(probe, StepReason.Command);

                probe.ViewState.SetInputText(
                    CommandSenderModule.SlotValue(0, SlotField.Label),
                    "开机");
                probe.ViewState.SetInputText(
                    CommandSenderModule.SlotValue(0, SlotField.Content),
                    "AT+START");
                probe.ViewState.SetInputText(
                    CommandSenderModule.SlotValue(1, SlotField.Label),
                    "查询");
                probe.ViewState.SetInputText(
                    CommandSenderModule.SlotValue(1, SlotField.Content),
                    "41 54 2B 51 3F");
                probe.ViewState.SetInputText(
                    CommandSenderModule.SlotValue(1, SlotField.Hex),
                    "true");
                probe.ViewState.SetInputText(CommandSenderModule.SelectionValue, "[1,0]");
                probe.ViewState.SetInputText(CommandSenderModule.IntervalValue, "500");
                probe.ViewState.SetInputText(CommandSenderModule.MergeValue, "true");
                module.OnStep(probe, StepReason.Input);

                var context = new SnapshotContext(
                    probe,
                    BuildFields(typeof(NoConfig), typeof(CommandSenderModule).Assembly));
                var view = new CommandSenderViewFactory().Create(context);
                var host = new ModuleViewHost(context, view);
                var (width, height) = WindowSize(view);
                var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                window.Show();
                SnapshotTool.Pump();
                host.Refresh();
                SnapshotTool.Pump();

                SnapshotTool.Save(window, "modules/CommandSender-filled.png");
                window.Close();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 生成图片框**带真实图片**的截图（<c>modules\ImageFrame-filled.png</c>）：
    /// 临时写一张 64×64 PNG，等控件完成加载后按 360×240 窗口截图。
    /// </summary>
    [Fact]
    public async Task 生成快照_图片框填充图片()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    "hyw-image-frame-" + Guid.NewGuid().ToString("N") + ".png");
                WriteSamplePng(path);

                try
                {
                    var probe = new TestModuleContext();
                    var context = new SnapshotContext(
                        probe,
                        BuildFields(typeof(NoConfig), typeof(ImageFrameModule).Assembly),
                        viewImageStore: new SnapshotViewImageStore(File.ReadAllBytes(path), null));
                    var view = new ImageFrameViewFactory().Create(context);
                    var host = new ModuleViewHost(context, view);
                    var (width, height) = WindowSize(view);
                    var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                    window.Show();
                    SnapshotTool.Pump();
                    host.Refresh();
                    SnapshotTool.Pump();

                    var surface = window.GetVisualDescendants().OfType<ModuleImageSurface>().First();
                    for (var attempt = 0; attempt < 50 && !surface.HasImage; attempt++)
                    {
                        Thread.Sleep(10);
                        SnapshotTool.Pump();
                        host.Refresh();
                    }

                    SnapshotTool.Save(window, "modules/ImageFrame-filled.png");
                    window.Close();
                }
                finally
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 生成图片框**加载 GIF 动图**的截图（<c>modules\ImageFrame-gif.png</c>）：
    /// 验证文件路径 → GIF 解码 → WriteableBitmap 帧的完整接入。
    /// </summary>
    [Fact]
    public async Task 生成快照_图片框加载GIF()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                const string gif =
                    "R0lGODlhAwACAIEAAP8AAAD/AAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQJCgACACwAAAAAAwACAAAICAABBABAUEBAACH5BAgUAAAALAAAAAADAAIAgQAA/wAAAAAAAAAAAAgGAAEIHBgQADs=";

                var path = Path.Combine(
                    Path.GetTempPath(),
                    "hyw-image-frame-" + Guid.NewGuid().ToString("N") + ".gif");
                File.WriteAllBytes(path, Convert.FromBase64String(gif));

                try
                {
                    var probe = new TestModuleContext();
                    var context = new SnapshotContext(
                        probe,
                        BuildFields(typeof(NoConfig), typeof(ImageFrameModule).Assembly),
                        viewImageStore: new SnapshotViewImageStore(File.ReadAllBytes(path), null));
                    var view = new ImageFrameViewFactory().Create(context);
                    var host = new ModuleViewHost(context, view);
                    var (width, height) = WindowSize(view);
                    var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                    window.Show();
                    SnapshotTool.Pump();
                    host.Refresh();
                    SnapshotTool.Pump();

                    var surface = window.GetVisualDescendants().OfType<ModuleImageSurface>().First();
                    for (var attempt = 0; attempt < 50 && !surface.HasImage; attempt++)
                    {
                        Thread.Sleep(10);
                        SnapshotTool.Pump();
                        host.Refresh();
                    }

                    SnapshotTool.Save(window, "modules/ImageFrame-gif.png");
                    window.Close();
                }
                finally
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            },
            CancellationToken.None);
    }

    private static void WriteSamplePng(string path)
    {
        using var bitmap = new SKBitmap(64, 64);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(30, 90, 160));
            using var paint = new SKPaint { Color = new SKColor(220, 80, 60) };
            canvas.DrawRect(new SKRect(0, 0, 32, 32), paint);
            paint.Color = new SKColor(240, 190, 60);
            canvas.DrawRect(new SKRect(32, 32, 64, 64), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    /// <summary>
    /// 生成图片框**带裁切**的截图（<c>modules\ImageFrame-cropped.png</c>）：
    /// 写入归一化裁切值，验证源矩形裁切与铺满绘制。
    /// </summary>
    [Fact]
    public async Task 生成快照_图片框裁切()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    "hyw-image-frame-" + Guid.NewGuid().ToString("N") + ".png");
                WriteSamplePng(path);

                try
                {
                    var probe = new TestModuleContext();
                    var context = new SnapshotContext(
                        probe,
                        BuildFields(typeof(NoConfig), typeof(ImageFrameModule).Assembly),
                        viewImageStore: new SnapshotViewImageStore(
                            File.ReadAllBytes(path),
                            new Rect(0.1, 0.2, 0.5, 0.6)));
                    var view = new ImageFrameViewFactory().Create(context);
                    var host = new ModuleViewHost(context, view);
                    var (width, height) = WindowSize(view);
                    var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                    window.Show();
                    SnapshotTool.Pump();
                    host.Refresh();
                    SnapshotTool.Pump();

                    var surface = window.GetVisualDescendants().OfType<ModuleImageSurface>().First();
                    for (var attempt = 0; attempt < 50 && !surface.HasImage; attempt++)
                    {
                        Thread.Sleep(10);
                        SnapshotTool.Pump();
                        host.Refresh();
                    }

                    SnapshotTool.Save(window, "modules/ImageFrame-cropped.png");
                    window.Close();
                }
                finally
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 生成图片框**从控件图片状态直接解码**的截图（<c>modules\ImageFrame-asset.png</c>）。
    /// 这是纯视觉夹具；真实持久化由服务/组合件测试覆盖。
    /// </summary>
    [Fact]
    public async Task 生成快照_图片框资产字节()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    "hyw-image-frame-" + Guid.NewGuid().ToString("N") + ".png");
                WriteSamplePng(path);

                try
                {
                    var probe = new TestModuleContext();

                    var context = new SnapshotContext(
                        probe,
                        BuildFields(typeof(NoConfig), typeof(ImageFrameModule).Assembly),
                        viewImageStore: new SnapshotViewImageStore(File.ReadAllBytes(path), null));
                    var view = new ImageFrameViewFactory().Create(context);
                    var host = new ModuleViewHost(context, view);
                    var (width, height) = WindowSize(view);
                    var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                    window.Show();
                    SnapshotTool.Pump();
                    host.Refresh();
                    SnapshotTool.Pump();

                    var surface = window.GetVisualDescendants().OfType<ModuleImageSurface>().First();
                    for (var attempt = 0; attempt < 50 && !surface.HasImage; attempt++)
                    {
                        Thread.Sleep(10);
                        SnapshotTool.Pump();
                        host.Refresh();
                    }

                    SnapshotTool.Save(window, "modules/ImageFrame-asset.png");
                    window.Close();
                }
                finally
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            },
            CancellationToken.None);
    }

    /// <summary>生成姿态模块的**视角面板展开**截图（<c>modules\AttitudeDisplay-viewcube.png</c>）。</summary>
    [Fact]
    public async Task 生成快照_姿态视角面板()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var probe = new TestModuleContext();
                var module = new AttitudeDisplayModule();
                module.OnRegistered(probe);
                var context = new SnapshotContext(
                    probe,
                    BuildFields(typeof(NoConfig), typeof(AttitudeDisplayModule).Assembly));
                var view = new AttitudeDisplayViewFactory().Create(context);
                var host = new ModuleViewHost(context, view);
                var (width, height) = WindowSize(view);
                var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                window.Show();
                SnapshotTool.Pump();
                host.Refresh();
                SnapshotTool.Pump();

                var attitude = window.GetVisualDescendants().OfType<AttitudeView>().First();
                attitude.OpenViewSelector();
                SnapshotTool.Pump();
                host.Refresh();
                SnapshotTool.Pump();

                SnapshotTool.Save(window, "modules/AttitudeDisplay-viewcube.png");
                window.Close();
            },
            CancellationToken.None);
    }

    /// <summary>生成姿态模块在“数据单位=弧度”下的底栏截图（<c>modules\AttitudeDisplay-rad.png</c>）。</summary>
    [Fact]
    public async Task 生成快照_姿态单位弧度()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var probe = new TestModuleContext();
                var module = new AttitudeDisplayModule();
                module.OnRegistered(probe);
                const string meta =
                    "{\"revision\":1,\"settings\":{\"group\":0,\"roll\":0,\"pitch\":1,\"yaw\":2,\"style\":\"cube\",\"unit\":\"rad\",\"smooth\":true,\"smoothMs\":120,\"zoom\":1,\"grid\":true,\"labels\":true}}";
                var context = new SnapshotContext(
                    probe,
                    BuildFields(typeof(NoConfig), typeof(AttitudeDisplayModule).Assembly),
                    meta);
                var view = new AttitudeDisplayViewFactory().Create(context);
                var host = new ModuleViewHost(context, view);
                var (width, height) = WindowSize(view);
                var window = SnapshotWindow.CreateFramed(host, view as ModuleView, width, height);

                window.Show();
                SnapshotTool.Pump();
                host.Refresh();
                SnapshotTool.Pump();

                SnapshotTool.Save(window, "modules/AttitudeDisplay-rad.png");
                window.Close();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 生成某模块的**无刷新预览**截图：模拟预览器，只实例化 XAML、不调用 Refresh。
    /// 用来人眼复核预览初值、范围框与布局；不是判据。
    /// </summary>
    /// <param name="moduleType">模块入口类型。</param>
    /// <param name="factoryType">视图工厂类型。</param>
    /// <param name="configType">配置类型。</param>
    [Theory]
    [MemberData(nameof(Modules))]
    public async Task 生成快照_模块视图预览(Type moduleType, Type factoryType, Type configType)
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var previous = ModuleView.IsPreviewHost;
                ModuleView.IsPreviewHost = true;

                try
                {
                    var probe = new TestModuleContext();
                    var module = (IModule)Activator.CreateInstance(moduleType)!;
                    module.OnRegistered(probe);

                    var fields = BuildFields(configType, moduleType.Assembly);
                    var context = new SnapshotContext(probe, fields);
                    var factory = (IModuleViewFactory)Activator.CreateInstance(factoryType)!;
                    var view = factory.Create(context);
                    var host = new ModuleViewHost(context, view);
                    var (width, height) = WindowSize(view);

                    var window = SnapshotWindow.CreatePreview(host, width, height);

                    window.Show();
                    SnapshotTool.Pump();

                    // 故意不调用 host.Refresh()：模拟 Avalonia 预览器。

                    var name = moduleType.Name.Replace("Module", string.Empty, StringComparison.Ordinal);
                    SnapshotTool.Save(window, "modules/" + name + "-preview.png");
                    window.Close();
                }
                finally
                {
                    ModuleView.IsPreviewHost = previous;
                }
            },
            CancellationToken.None);
    }

    /// <summary>截图窗口尺寸 = 视图声明的实际窗口尺寸（含外框）。</summary>
    private static (double Width, double Height) WindowSize(Control view)
    {
        var width = view is ModuleView moduleView && moduleView.InitialWidth > 0
            ? moduleView.InitialWidth
            : view.MinWidth > 0 ? view.MinWidth : 500;
        var height = view is ModuleView moduleViewHeight && moduleViewHeight.InitialHeight > 0
            ? moduleViewHeight.InitialHeight
            : view.MinHeight > 0 ? view.MinHeight : 400;

        return (width, height);
    }

    /// <summary>从配置类型推出“每个字段长什么样”的替身（标题取声明该接入的模块的表，与运行时同一份）。</summary>
    private static Dictionary<string, ConfigFieldView> BuildFields(Type configType, Assembly textOwner)
    {
        var fields = new Dictionary<string, ConfigFieldView>(StringComparer.Ordinal);

        foreach (var property in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.CanWrite)
                     .Where(property => property.GetCustomAttribute<ConfigurationFieldAttribute>() is not null))
        {
            var attribute = property.GetCustomAttribute<ConfigurationFieldAttribute>()!;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            ConfigFieldKind kind;
            string value;
            IReadOnlyList<ConfigChoice>? choices = null;

            if (type.IsEnum)
            {
                var names = Enum.GetNames(type);
                kind = ConfigFieldKind.Enum;
                value = names[0];
                choices = names.Select(item => new ConfigChoice(item, item)).ToArray();
            }
            else if (type == typeof(bool))
            {
                kind = ConfigFieldKind.Boolean;
                value = "true";
            }
            else if (type == typeof(string))
            {
                kind = ConfigFieldKind.Text;
                value = string.Empty;
            }
            else
            {
                kind = ConfigFieldKind.Number;
                value = "0";
            }

            fields[property.Name] = new ConfigFieldView(
                property.Name,
                Localize(property, attribute, textOwner) ?? property.Name,
                kind,
                value,
                choices,
                attribute.Unit,
                attribute.Description);
        }

        return fields;
    }

    /// <summary>标题取声明该接入的模块的文本表；表不在 ⇒ 退回标题字面量。</summary>
    private static string? Localize(PropertyInfo property, ConfigurationFieldAttribute attribute, Assembly textOwner)
    {
        if (attribute.TitleKey is { Length: > 0 } key
            && TextCatalog.TryGet(textOwner, key, out var text))
        {
            return text;
        }

        return attribute.Title;
    }

    /// <summary>截图用的视图上下文：字段取替身，读数给固定值，其余不接线。</summary>
    private sealed class SnapshotContext : IModuleViewContext
    {
        private readonly IReadOnlyDictionary<string, ConfigFieldView> _fields;
        private readonly TestModuleContext _probe;
        private readonly string _text;

        public SnapshotContext(
            TestModuleContext probe,
            IReadOnlyDictionary<string, ConfigFieldView> fields,
            string text = "就绪",
            IModuleViewImageStore? viewImageStore = null)
        {
            _fields = fields;
            _probe = probe;
            _text = text;
            ViewImageStore = viewImageStore;
        }

        public string InstanceId => "snapshot";

        public IModuleColorPicker? ColorPicker => null;

        public IModuleViewImageStore? ViewImageStore { get; }

        public string MultilineSubmitKey => "CtrlEnter";

        public double WheelZoomStep => 1.2;

        public double? ReadLatest(string source) =>
            _probe.Published.TryGetValue(source, out var values)
                && values.Count > 0
                && values[^1].Length >= sizeof(ulong)
                ? BitConverter.ToUInt64(values[^1], 0)
                : 42;

        public IReadOnlyList<double> ReadSeries(string source) => [42];

        public SeriesWindowSnapshot? ReadSeriesWindow(string source) => null;

        public TextBufferSnapshot ReadText(string source)
        {
            for (var index = _probe.SetTexts.Count - 1; index >= 0; index--)
            {
                if (string.Equals(_probe.SetTexts[index].SourceName, source, StringComparison.Ordinal))
                {
                    return new([_probe.SetTexts[index].Text], 0, 20000);
                }
            }

            return new([_text], 0, 20000);
        }

        public long ReadTextRevision(string source) => 0;

        public bool SubmitInput(string source, string text) => true;

        public CommandOutcome InvokeCommand(string command) => CommandOutcome.Ok;

        public ConfigFieldView? ReadField(string fieldName) =>
            _fields.TryGetValue(fieldName, out var field) ? field : null;

        public CommandOutcome UpdateField(string fieldName, string value) => CommandOutcome.Ok;

        public ViewAppearance Appearance(string source)
        {
            for (var index = _probe.AppearanceReports.Count - 1; index >= 0; index--)
            {
                var report = _probe.AppearanceReports[index];
                if (string.Equals(report.SourceName, source, StringComparison.Ordinal))
                {
                    return new ViewAppearance { Enabled = report.Enabled };
                }
            }

            return ViewAppearance.None;
        }

        public string? ReadInputText(string source) => _probe.ViewState.GetInputText(source);

        public void WriteInputText(string source, string text) => _probe.ViewState.SetInputText(source, text);

        public ValueState ReadValue(string name)
        {
            var text = _probe.Values.ReadText(name);

            if (text is null)
            {
                return ValueState.Empty(name);
            }

            var declaration = _probe.DeclaredValues
                .FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));

            return new ValueState(
                name,
                declaration?.Editor ?? ValueEditor.Text,
                text,
                string.Empty,
                null,
                IsReadOnly: false,
                IsEnabled: true,
                _probe.Values.ReadValidity(name),
                0,
                IsKnown: true);
        }

        public void CommitValue(string name, string text) => _probe.Values.AcceptText(name, text);

        public void RegisterViewDefault(string name, string value)
        {
            if (_probe.Values.ReadText(name) is null)
            {
                _probe.Values.AcceptText(name, value);
            }
        }

        public int? SelectedPage(string stateKey) => null;

        public void SetSelectedPage(string stateKey, int page)
        {
        }
    }

    private sealed class SnapshotViewImageStore(byte[]? bytes, Rect? crop) : IModuleViewImageStore
    {
        private ModuleViewImage? _image = bytes is null ? null : new ModuleViewImage(bytes, crop);
        private long _revision = bytes is null ? 0 : 1;

        public long Revision => _revision;

        public ModuleViewImage? Read() => _image;

        public bool Write(byte[]? data, Rect? newCrop)
        {
            _image = data is { Length: > 0 } ? new ModuleViewImage(data, newCrop) : null;
            _revision++;
            return true;
        }
    }
}
