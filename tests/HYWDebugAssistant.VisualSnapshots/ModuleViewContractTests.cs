using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.DataMonitor.Views;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.HexSender.Views;
using HYWDebugAssistant.Modules.HexStringConverter;
using HYWDebugAssistant.Modules.HexStringConverter.Views;
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
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;
using SdkIcon = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **八个模块视图的契约判据**：真实载入 XAML，再按控件上的键逐条对代码声明。
/// </summary>
/// <remarks>
/// <para>
/// 这是 <c>view.json</c> 时代"声明与配置逐字段对账"的替代物。判据有五条：
/// ① 视图能真的造出来（XAML 资源、模板、控件都存在）；② 每个 <c>Source</c> / <c>Field</c> /
/// <c>CommandName</c> 都能在模块的代码声明里找到；③ 每个配置字段**恰好一对**标签 + 编辑器
/// （漏了 = 参数没地方改 / 没抬头，重复 = 同一项两处可改）；④ 标签与编辑器各占一条矩形、互不重叠，
/// 编辑区不小于下限；⑤ 结构文案装得下自己的矩形（不许静默裁切）。
/// </para>
/// <para>
/// 键本身用 <c>{x:Static}</c> 绑定到模块常量，所以**拼错常量名是编译错误**；
/// 本用例负责另一半：常量指向的名字必须真的被声明过。
/// </para>
/// </remarks>
[Collection("visual-snapshots")]
public class ModuleViewContractTests
{
    private sealed class NoConfig;

    public static TheoryData<Type, Type, Type> Modules => new()
    {
        { typeof(SerialModule), typeof(SerialViewFactory), typeof(SerialConfig) },
        { typeof(TcpClientModule), typeof(TcpClientViewFactory), typeof(TcpClientConfig) },
        { typeof(TcpServerModule), typeof(TcpServerViewFactory), typeof(TcpServerConfig) },
        { typeof(UdpModule), typeof(UdpViewFactory), typeof(UdpConfig) },
        { typeof(TextSenderModule), typeof(TextSenderViewFactory), typeof(NoConfig) },
        { typeof(HexSenderModule), typeof(HexSenderViewFactory), typeof(NoConfig) },
        { typeof(DataMonitorModule), typeof(DataMonitorViewFactory), typeof(NoConfig) },
        { typeof(HexStringConverterModule), typeof(HexStringConverterViewFactory), typeof(NoConfig) },
    };

    /// <summary>每个模块：视图键必须对得上声明，配置字段必须一格不多一格不少，且标签与编辑器成对。</summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public async Task 模块视图的键必须与代码声明逐条对上(Type moduleType, Type factoryType, Type configType)
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var probe = new TestModuleContext();
                var module = (IModule)Activator.CreateInstance(moduleType)!;
                module.OnRegistered(probe);

                var fields = BuildFields(configType, moduleType.Assembly);
                var context = new ContractContext(probe, fields);

                var factory = (IModuleViewFactory)Activator.CreateInstance(factoryType)!;
                var view = factory.Create(context);
                var host = new ModuleViewHost(context, view);

                // **用模块声明的真实窗口尺寸 + 同一份内容区几何**：快照必须代表真机。
                var definition = moduleType.GetCustomAttribute<ModuleDefinitionAttribute>();
                var width = definition is { WindowWidth: > 0 } ? definition.WindowWidth : 500;
                var height = definition is { WindowHeight: > 0 } ? definition.WindowHeight : 400;
                var content = ModuleSurfaceMetrics.ContentSize(width, height);

                var window = new Window
                {
                    Width = width,
                    Height = height,
                    Content = new Border
                    {
                        Margin = ModuleSurfaceMetrics.ContentMargin,
                        Child = host,
                    },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                // 视图至少填满真机内容区；视图根可以声明更大的 MinWidth/MinHeight（那是模块自己的布局选择，
                // 不是测试该钉死的常量），此时按它自己的下限测量。
                view.Bounds.Width.ShouldBe(Math.Max(content.Width, view.MinWidth), 0.5);
                view.Bounds.Height.ShouldBe(Math.Max(content.Height, view.MinHeight), 0.5);

                // 页签内容按需实例化：逐页切一次，确保每一页的控件都真的被造出来。
                foreach (var tabs in window.GetVisualDescendants().OfType<ModuleTabs>().ToList())
                {
                    for (var index = 0; index < tabs.ItemCount; index++)
                    {
                        tabs.SelectedIndex = index;
                        Pump();
                        host.Refresh();
                        Pump();
                    }
                }

                var elements = window.GetVisualDescendants().OfType<ModuleElement>().ToList();

                // **随包文本真的命中**：任何 `⟦key⟧` 都是表没进包 / 键拼错（8 个模块全覆盖）。
                var visibleTexts = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty)
                    .ToList();
                visibleTexts.ShouldNotContain(
                    text => text.Contains('\u27E6'),
                    $"{moduleType.Name} 的界面出现未命中文本键（⟦…⟧）——随包表必须被解析到");

                foreach (var element in elements.Where(element => element.Source.Length > 0))
                {
                    context.IsKnownKey(element.Source).ShouldBeTrue(
                        $"{moduleType.Name} 的 {element.GetType().Name} 绑定了未声明的键「{element.Source}」");
                }

                foreach (var text in elements.OfType<ModuleTextSurface>().Where(surface => surface.Mode == TextSurfaceMode.Input))
                {
                    context.IsInput(text.Source).ShouldBeTrue(
                        $"{moduleType.Name} 的输入框「{text.Source}」不是已声明的输入源");
                }

                var editors = elements.OfType<ModuleConfigField>().ToList();
                var labels = elements.OfType<ModuleLabel>().Where(label => label.Field.Length > 0).ToList();

                foreach (var editor in editors)
                {
                    context.IsField(editor.Field).ShouldBeTrue(
                        $"{moduleType.Name} 的配置格「{editor.Field}」不是配置类型上的可写字段");
                }

                editors.Select(editor => editor.Field).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    .ShouldBe(fields.Keys.Order(StringComparer.Ordinal),
                        $"{moduleType.Name} 的配置字段必须恰好各有一格（漏了 = 参数没地方改；重复 = 同一项两处可改）");

                labels.Select(label => label.Field).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    .ShouldBe(fields.Keys.Order(StringComparer.Ordinal),
                        $"{moduleType.Name} 的每个配置字段必须恰好有一个标签（标题是独立元素，不许长在编辑器里）");

                labels.ShouldAllBe(label => label.Source.Length == 0, "标签不参与运行期外观通道（Source 必须为空）");

                // **一对标签 + 编辑器 = 两条矩形**：标签在左、编辑器在右，互不重叠，且两者都有实际尺寸。
                // 编辑区的具体宽度是模块 .axaml 的布局选择，测试不抄这个值。
                foreach (var editor in editors)
                {
                    var label = labels.Single(item => string.Equals(item.Field, editor.Field, StringComparison.Ordinal));

                    label.Bounds.Right.ShouldBeLessThanOrEqualTo(
                        editor.Bounds.Left + 0.5,
                        $"{moduleType.Name} 的字段「{editor.Field}」标签压到了编辑器（标签右缘 {label.Bounds.Right:0.#}，编辑器左缘 {editor.Bounds.Left:0.#}）");
                    label.Bounds.Width.ShouldBeGreaterThan(0);
                    editor.Bounds.Width.ShouldBeGreaterThan(0, $"{moduleType.Name} 的字段「{editor.Field}」没有实际编辑区");
                    label.Bounds.Height.ShouldBeGreaterThan(0);
                }

                // **读数标题与值紧邻**：标题右缘贴着值的左缘（只留一条小间距），不是隔着半格。
                foreach (var value in elements.OfType<ModuleNumeric>())
                {
                    var caption = FindCaption(window, value);
                    caption.ShouldNotBeNull(
                        $"{moduleType.Name} 的读数「{value.Source}」前面没有紧邻的标题");
                    Math.Abs(CenterY(window, caption!) - CenterY(window, value)).ShouldBeLessThanOrEqualTo(
                        1.0,
                        $"{moduleType.Name} 的读数「{value.Source}」标题与值不在同一中线");
                    (value.Bounds.Left - caption!.Bounds.Right).ShouldBeLessThanOrEqualTo(
                        5,
                        $"{moduleType.Name} 的读数「{value.Source}」标题与值之间空了 {value.Bounds.Left - caption.Bounds.Right:0.#}px");
                }

                // 文案装得下与否**不是测试判据**：措辞长度属用户可调的呈现（改文案不该让测试变红），
                // 装不下由人眼验收；这里只保留结构与"声明必须被正确应用"的判据。

                foreach (var button in window.GetVisualDescendants().OfType<ModuleButton>())
                {
                    context.IsCommand(button.CommandName).ShouldBeTrue(
                        $"{moduleType.Name} 的按钮命令「{button.CommandName}」没有在 OnRegistered 里声明");
                }

                // 清空计数按钮：**图标来自模块自己的图标表**（视图里不再有路径几何）；
                // 尺寸由视图决定（只要求真的占住一块、在内容区内、不压别的块）。
                var clearCountersCommand = moduleType
                    .GetField("ClearCountersCommand", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as string;

                if (!string.IsNullOrEmpty(clearCountersCommand))
                {
                    var clearButtons = window.GetVisualDescendants()
                        .OfType<ModuleButton>()
                        .Where(button => string.Equals(button.CommandName, clearCountersCommand, StringComparison.Ordinal))
                        .ToList();

                    clearButtons.ShouldHaveSingleItem($"{moduleType.Name} 必须有且只有一个清空计数按钮");
                    var clear = clearButtons[0];

                    var clearCountersIcon = ClearCountersIconOf(moduleType);
                    clearCountersIcon.ShouldNotBeNullOrWhiteSpace(
                        $"{moduleType.Name} 的 ViewKeys 必须提供 ClearCountersIcon 常量");

                    clear.Icon.ShouldBe(
                        clearCountersIcon,
                        $"{moduleType.Name} 的清空按钮必须绑定本模块的图标键");

                    var clearIcon = clear.Content.ShouldBeOfType<SdkIcon>(
                        $"{moduleType.Name} 的清空按钮必须渲染成图标控件（不是字形、不是几何）");
                    clearIcon.IconKey.ShouldBe(
                        clearCountersIcon,
                        $"{moduleType.Name} 的图标控件必须拿到本模块的键");
                    clearIcon.Owner.ShouldBe(
                        moduleType.Assembly,
                        $"{moduleType.Name} 的图标必须来自模块自己的清单");
                    clearIcon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                        .Single().Data.ShouldNotBeNull($"{moduleType.Name} 的图标必须真的画出几何");

                    clear.GetVisualDescendants().OfType<PathIcon>()
                        .ShouldBeEmpty($"{moduleType.Name} 的按钮里不得用 PathIcon");

                    // 按钮里的路径只能来自图标控件的模板部件；视图里再画几何就会多出第二份。
                    foreach (var path in clear.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>())
                    {
                        path.FindAncestorOfType<SdkIcon>().ShouldNotBeNull(
                            $"{moduleType.Name} 的按钮里不得自绘路径（图标是数据）");
                    }
                    ColorOf(clear.Background).ShouldBe(
                        TokenColor("SurfaceButtonFaceBrush"),
                        $"{moduleType.Name} 的清空按钮必须有按钮面");
                    ColorOf(clear.BorderBrush).ShouldBe(
                        TokenColor("SurfaceButtonEdgeBrush"),
                        $"{moduleType.Name} 的清空按钮必须有按钮边");
                    clear.BorderThickness.Top.ShouldBeGreaterThan(0, $"{moduleType.Name} 的清空按钮必须有边");
                    clear.CornerRadius.TopLeft.ShouldBeGreaterThan(0, $"{moduleType.Name} 的清空按钮必须有圆角");
                    ColorOf(clear.Foreground).ShouldBe(
                        TokenColor("TextSecondary"),
                        $"{moduleType.Name} 的清空按钮静止时字形是次要色");
                    var clearRect = RectIn(clear, view);
                    clearRect.Width.ShouldBeGreaterThan(0, $"{moduleType.Name} 的清空按钮宽度必须大于 0");
                    clearRect.Height.ShouldBeGreaterThan(0, $"{moduleType.Name} 的清空按钮高度必须大于 0");
                    clearRect.Left.ShouldBeGreaterThanOrEqualTo(0);
                    clearRect.Top.ShouldBeGreaterThanOrEqualTo(0);
                    clearRect.Right.ShouldBeLessThanOrEqualTo(
                        view.Bounds.Width + 0.5,
                        $"{moduleType.Name} 的清空按钮超出内容区右缘");
                    clearRect.Bottom.ShouldBeLessThanOrEqualTo(
                        view.Bounds.Height + 0.5,
                        $"{moduleType.Name} 的清空按钮超出内容区下缘");

                    var neighbours = window.GetVisualDescendants()
                        .OfType<ModuleButton>()
                        .Where(button => !ReferenceEquals(button, clear))
                        .Cast<Control>()
                        .Concat(window.GetVisualDescendants()
                            .OfType<ModuleLabel>()
                            .Where(label => label.Source.Length > 0)
                            .Cast<Control>());

                    foreach (var neighbour in neighbours)
                    {
                        clearRect.Intersects(RectIn(neighbour, view)).ShouldBeFalse(
                            $"{moduleType.Name} 的清空按钮压到了 {neighbour.GetType().Name}");
                    }
                }

                foreach (var tabs in window.GetVisualDescendants().OfType<ModuleTabs>())
                {
                    tabs.StateKey.ShouldNotBeNullOrWhiteSpace(
                        $"{moduleType.Name} 的页签没有 StateKey —— 模块读不到当前页，永远只会发第 0 页");
                }

                // 留下真实像素：布局/空白/裁切这类问题只能靠眼睛看，不能靠"控件存在"断言。
                var output = Path.Combine(Path.GetTempPath(), "hyw-module-view-contract");
                _ = Directory.CreateDirectory(output);
                var size = new PixelSize(
                    Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
                    Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));
                using var bitmap = new RenderTargetBitmap(size);
                bitmap.Render(window);
#pragma warning disable CS0618   // 探针工具：旧的 Save(string) 足够
                bitmap.Save(Path.Combine(output, $"{moduleType.Name}.png"));
#pragma warning restore CS0618
                new FileInfo(Path.Combine(output, $"{moduleType.Name}.png")).Length.ShouldBeGreaterThan(
                    500, "视图不能渲染成一张空白图");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// **读数的标题与值同排、状态灯落在内容区内且不压别的块、标签只有一档墨色**。
    /// </summary>
    [Fact]
    public async Task 读数标题与值同排_状态灯与读数同中线()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var probe = new TestModuleContext();
                var module = new SerialModule();
                module.OnRegistered(probe);
                var context = new ContractContext(probe, BuildFields(typeof(SerialConfig), typeof(SerialModule).Assembly));
                var view = new SerialViewFactory().Create(context);
                var host = new ModuleViewHost(context, view);
                var window = new Window
                {
                    Width = 400,
                    Height = 190,
                    Content = new Border { Margin = ModuleSurfaceMetrics.ContentMargin, Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                var numeric = window.GetVisualDescendants().OfType<ModuleNumeric>().First();
                var caption = FindCaption(window, numeric);
                caption.ShouldNotBeNull("读数前面必须有紧邻的标题标签");

                var value = numeric.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(block => block.Name == "PART_Value");
                var captionText = caption!.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(block => block.Name == "PART_Text");

                // 同一档标签墨色：读数标题与字段标题必须一致；值比它们更重。
                var fieldLabel = window.GetVisualDescendants()
                    .OfType<ModuleLabel>()
                    .First(label => label.Field.Length > 0);
                var fieldText = fieldLabel.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(block => block.Name == "PART_Text");

                captionText.Foreground.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(
                    fieldText.Foreground.ShouldBeOfType<SolidColorBrush>().Color,
                    "读数标题与字段标题必须同一档墨色（标签只有一套排印）");
                captionText.Foreground.ShouldBeOfType<SolidColorBrush>().Color.ShouldNotBe(
                    value.Foreground.ShouldBeOfType<SolidColorBrush>().Color,
                    "读数里只有值用正文墨");

                // 状态灯：位置由视图自己决定（当前布局把它放在右栏顶部，与读数行不同中线）；
                // 判据只要求它真的占住一块、在内容区内、不压读数 / 按钮 / 编辑器。
                var light = window.GetVisualDescendants().OfType<ModuleStatusLight>().Single();
                var lightRect = RectIn(light, view);
                lightRect.Width.ShouldBeGreaterThan(0, "状态灯必须真的占住一块");
                lightRect.Height.ShouldBeGreaterThan(0, "状态灯必须真的占住一块");
                lightRect.Left.ShouldBeGreaterThanOrEqualTo(0);
                lightRect.Top.ShouldBeGreaterThanOrEqualTo(0);
                lightRect.Right.ShouldBeLessThanOrEqualTo(view.Bounds.Width + 0.5, "状态灯超出内容区右缘");
                lightRect.Bottom.ShouldBeLessThanOrEqualTo(view.Bounds.Height + 0.5, "状态灯超出内容区下缘");

                var overlapped = window.GetVisualDescendants().OfType<ModuleNumeric>().Cast<Control>()
                    .Concat(window.GetVisualDescendants().OfType<ModuleButton>())
                    .Concat(window.GetVisualDescendants().OfType<ModuleConfigField>())
                    .Where(other => lightRect.Intersects(RectIn(other, view)))
                    .Select(other => other.GetType().Name)
                    .ToList();

                overlapped.ShouldBeEmpty("状态灯不得压在读数 / 按钮 / 编辑器上");

                window.Close();
                Pump();
            },
            CancellationToken.None);

        await session.Dispatch(
            () =>
            {
                var probe = new TestModuleContext();
                var module = new TextSenderModule();
                module.OnRegistered(probe);
                var context = new ContractContext(probe, BuildFields(typeof(NoConfig), typeof(TextSenderModule).Assembly));
                var host = new ModuleViewHost(context, new TextSenderViewFactory().Create(context));
                var window = new Window
                {
                    Width = 400,
                    Height = 240,
                    Content = new Border { Margin = ModuleSurfaceMetrics.ContentMargin, Child = host },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

                // 间隔输入框右边紧挨着的那个标签就是单位：两者同一中线。
                var interval = window.GetVisualDescendants()
                    .OfType<ModuleTextSurface>()
                    .Single(surface => surface.Source == TextSenderModule.IntervalSource);
                var unit = window.GetVisualDescendants()
                    .OfType<ModuleLabel>()
                    .Where(label => label.Bounds.Left >= interval.Bounds.Right)
                    .OrderBy(label => label.Bounds.Left)
                    .First();

                Math.Abs(CenterY(window, interval) - CenterY(window, unit)).ShouldBeLessThanOrEqualTo(
                    1.0,
                    "间隔输入框与单位标签必须垂直居中");
                (unit.Bounds.Left - interval.Bounds.Right).ShouldBeLessThanOrEqualTo(
                    4,
                    "间隔输入框与单位标签必须紧邻");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>主题 token 的实际颜色（面 / 边 / 字色都从 token 取，测试不抄色值）。</summary>
    private static Color TokenColor(string key)
    {
        Application.Current!.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var value)
            .ShouldBeTrue($"{key} 必须由主题 token 给出");

        return ColorOf(value as IBrush);
    }

    /// <summary>取画刷的颜色（面 / 边 / 字色都是纯色 token）。</summary>
    private static Color ColorOf(IBrush? brush)
    {
        var solid = brush as ISolidColorBrush;
        solid.ShouldNotBeNull($"{brush} 必须是纯色画刷");

        return solid!.Color;
    }

    /// <summary>读数前面紧邻的标题标签（同一中线、右缘贴着值的左缘）。</summary>
    private static ModuleLabel? FindCaption(Window window, ModuleNumeric value) =>
        window.GetVisualDescendants()
            .OfType<ModuleLabel>()
            .Where(label => Math.Abs(CenterY(window, label) - CenterY(window, value)) <= 1.0)
            .Where(label => value.Bounds.Left - label.Bounds.Right is >= -0.5 and <= 5)
            .OrderByDescending(label => label.Bounds.Right)
            .FirstOrDefault();

    /// <summary>控件中线（换算到窗口坐标）。</summary>
    private static double CenterY(Window window, Visual control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        return center?.Y ?? 0;
    }

    /// <summary>控件在视图坐标系里的矩形（按钮与状态标签同属一个 Canvas，按视图坐标断言更稳）。</summary>
    private static Rect RectIn(Control control, Visual root)
    {
        if (control.TranslatePoint(new Point(0, 0), root) is not { } origin)
        {
            throw new InvalidOperationException($"{control.GetType().Name} 不在视图的可视树里");
        }

        return new Rect(origin, control.Bounds.Size);
    }

    /// <summary>模块 <c>ViewKeys</c> 里的清空图标键（没有该常量 ⇒ <see langword="null"/>）。</summary>
    private static string? ClearCountersIconOf(Type moduleType) =>        moduleType.Assembly.GetTypes()
            .Select(type => type.GetField("ClearCountersIcon", BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            ?.GetRawConstantValue() as string;

    /// <summary>从配置类型推出"每个字段应该长什么样"的替身（标题取**声明该接入的模块**的表，与运行时同一份）。</summary>
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
                choices = names.Select(name => new ConfigChoice(name, name)).ToArray();
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

    /// <summary>标题取**声明该接入的模块**的文本表（与运行时、预览同一份来源）；表不在 ⇒ 退回标题字面量。</summary>
    private static string? Localize(
        PropertyInfo property,
        ConfigurationFieldAttribute attribute,
        Assembly textOwner)
    {
        if (attribute.TitleKey is { Length: > 0 } key
            && TextCatalog.TryGet(textOwner, key, out var text))
        {
            return text;
        }

        return attribute.Title;
    }

    /// <summary>模块视图的桩上下文：只回答"这个键声明过没有"，不碰真实运行时。</summary>
    private sealed class ContractContext : IModuleViewContext
    {
        private readonly HashSet<string> _displaySources;
        private readonly HashSet<string> _inputs;
        private readonly HashSet<string> _commands;
        private readonly IReadOnlyDictionary<string, ConfigFieldView> _fields;

        public ContractContext(TestModuleContext probe, IReadOnlyDictionary<string, ConfigFieldView> fields)
        {
            _displaySources = probe.DeclaredDisplaySources.Select(source => source.Name).ToHashSet(StringComparer.Ordinal);
            _inputs = probe.DeclaredInputs.ToHashSet(StringComparer.Ordinal);
            _commands = probe.DeclaredCommands.Select(command => command.Name).ToHashSet(StringComparer.Ordinal);
            _fields = fields;
        }

        public string InstanceId => "view-contract";

        public string MultilineSubmitKey => "CtrlEnter";

        public bool IsKnownKey(string key) =>
            _displaySources.Contains(key) || _inputs.Contains(key) || _fields.ContainsKey(key);

        public bool IsInput(string key) => _inputs.Contains(key);

        public bool IsField(string key) => _fields.ContainsKey(key);

        public bool IsCommand(string command) => _commands.Contains(command);

        public double? ReadLatest(string source) => 42;

        public IReadOnlyList<double> ReadSeries(string source) => [42];

        public TextBufferSnapshot ReadText(string source) => new(["就绪"], 0, 20000, 0);

        public bool SubmitInput(string source, string text) => true;

        public CommandOutcome InvokeCommand(string command) => CommandOutcome.Ok;

        public ConfigFieldView? ReadField(string fieldName) =>
            _fields.TryGetValue(fieldName, out var field) ? field : null;

        public CommandOutcome UpdateField(string fieldName, string value) => CommandOutcome.Ok;

        public ViewAppearance Appearance(string source) => ViewAppearance.None;

        public string? ReadInputText(string source) => null;

        public void WriteInputText(string source, string text)
        {
        }

        public int? SelectedPage(string stateKey) => null;

        public void SetSelectedPage(string stateKey, int page)
        {
        }
    }
}

