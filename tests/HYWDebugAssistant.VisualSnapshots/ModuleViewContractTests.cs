using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
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
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **八个模块视图的契约判据**：真实载入 XAML，再按控件上的键逐条对代码声明。
/// </summary>
/// <remarks>
/// <para>
/// 这是 <c>view.json</c> 时代"声明与配置逐字段对账"的替代物。判据有三条：
/// ① 视图能真的造出来（XAML 资源、模板、控件都存在）；② 每个 <c>Source</c> / <c>Field</c> /
/// <c>CommandName</c> 都能在模块的代码声明里找到；③ 每个配置字段恰好有一个编辑器
/// （漏了 = 参数没地方改，重复 = 同一项两处可改）。
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

    /// <summary>每个模块：视图键必须对得上声明，配置字段必须一格不多一格不少。</summary>
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

                var fields = BuildFields(configType);
                var context = new ContractContext(probe, fields);

                var factory = (IModuleViewFactory)Activator.CreateInstance(factoryType)!;
                var host = new ModuleViewHost(context, factory.Create(context));

                // 用模块声明的真实窗口尺寸 + 应用的内容预留带，快照才代表真实窗口；
                // 否则 500×400 会把"默认窗口下已经挤爆"的布局掩盖掉。
                var definition = moduleType.GetCustomAttribute<ModuleDefinitionAttribute>();
                var width = definition is { WindowWidth: > 0 } ? definition.WindowWidth : 500;
                var height = definition is { WindowHeight: > 0 } ? definition.WindowHeight : 400;
                var window = new Window
                {
                    Width = width,
                    Height = height,
                    Content = new Border
                    {
                        Margin = new Thickness(20, 22, 20, 24),
                        Child = host,
                    },
                };

                window.Show();
                Pump();
                host.Refresh();
                Pump();

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

                foreach (var editor in editors)
                {
                    context.IsField(editor.Field).ShouldBeTrue(
                        $"{moduleType.Name} 的配置格「{editor.Field}」不是配置类型上的可写字段");
                }

                editors.Select(editor => editor.Field).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    .ShouldBe(fields.Keys.Order(StringComparer.Ordinal),
                        $"{moduleType.Name} 的配置字段必须恰好各有一格（漏了 = 参数没地方改；重复 = 同一项两处可改）");

                foreach (var button in window.GetVisualDescendants().OfType<ModuleButton>())
                {
                    context.IsCommand(button.CommandName).ShouldBeTrue(
                        $"{moduleType.Name} 的按钮命令「{button.CommandName}」没有在 OnRegistered 里声明");
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

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>从配置类型推出"每个字段应该长什么样"的替身（标题取自特性）。</summary>
    private static Dictionary<string, ConfigFieldView> BuildFields(Type configType)
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
                attribute.Title ?? property.Name,
                kind,
                value,
                choices,
                attribute.Unit,
                attribute.Description);
        }

        return fields;
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

