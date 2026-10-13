using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Design;

/// <summary>
/// **Ui 层的设计时样例数据**（XAML 预览器用）——【用户第九轮反馈】：
/// "axaml 预览有问题、很难直接调 UI"的根因是这些视图**没有设计时 DataContext**（预览是空壳）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>用法：视图根上写 <c>d:DataContext="{x:Static design:DesignData.ModuleWindow}"</c>
///     （需要 `xmlns:design="using:HYWDebugAssistant.Ui.Design"` 与 `xmlns:d="…blend/2008"`）；
///     `d:` 命名空间**只在设计器里生效**，运行时被忽略；</item>
///   <item>样例覆盖各类元素（数值/曲线/日志/配置表单/命令按钮/文本输入/开关），
///     因此改样式、间距、字号都能立刻在设计器里看到效果；</item>
///   <item>外壳与弹窗的样例在 `FrameworkUi/Design/ShellDesignData.cs`（依赖方向：FrameworkUi → Ui）。</item>
/// </list>
/// </remarks>
public static class DesignData
{
    /// <summary>模块窗口样例（2 个口 + 各类元素 + 已连接的网络标签）。</summary>
    public static ModuleViewViewModel ModuleWindow { get; } = BuildModuleWindow();

    private static ModuleViewViewModel BuildModuleWindow()
    {
        var ports = new List<PortView>
        {
            new("in", "Receive", 4, IsConnected: true, IsStalled: false, 0.35, false, "bus1", "f32"),
            new("out", "Send", 12, IsConnected: true, IsStalled: false, 0.6, false, "bus1", "{seq:u64, value:f32}"),
        };

        // 声明面样例：**结构、尺寸、显示口径全在文档里**（与包内 `view.json` 是同一套形状），
        // 这样设计器预览看到的排布/尺寸取值与真实模块一致（Ui 侧不再有第二份默认值）。
        var view = new ViewDocument
        {
            Version = 1,
            Frame = new ViewFrame
            {
                Window = new ViewWindowDeclaration { Width = 340, Height = 340, MinWidth = 180, MinHeight = 160 },
                Labels =
                [
                    new ViewNetworkLabel("in", PortEdge.Left, 0.35),
                    new ViewNetworkLabel("out", PortEdge.Right, 0.6),
                ],
            },
            Sources =
            [
                new ViewSourceDeclaration("value", ViewDataSourceKind.Value, ContractPrimitive.F32, "V"),
                new ViewSourceDeclaration("sent", ViewDataSourceKind.Value, ContractPrimitive.U64, null),
                new ViewSourceDeclaration("wave", ViewDataSourceKind.Sequence, ContractPrimitive.F32, null),
                new ViewSourceDeclaration("log", ViewDataSourceKind.Text, null, null),
            ],
            Views =
            [
                new ViewDefinition("文本发送器", Root()),
            ],
        };

        var instance = new ModuleInstanceView(
            "sender-1",
            "hyw/text-sender/1.0.0",
            "文本发送器",
            "Active",
            HasView: true,
            ports,
            IsPlaceholder: false,
            view);

        return new ModuleViewViewModel(
            instance,
            layout: new ModuleLayoutView(
                "sender-1",
                0,
                0,
                view.Frame.Window.Width,
                view.Frame.Window.Height),
            EmptyRuntime.Instance,
            EmptyRuntime.Instance,
            fallbackX: 0,
            fallbackY: 0);
    }

    /// <summary>样例视图的根（流式排布、随窗口填满 —— 与真实模块声明的缺省一致）。</summary>
    private static ViewNode Root() => new()
    {
        Kind = ViewElementKind.Container.DeclarationName(),
        Title = "文本发送器",
        Width = new AxisLayout { Fill = true },
        Height = new AxisLayout { Fill = true },
        Layout = "flow",
        Overflow = "scroll",
        Columns = [],
        Rows = [],
        Unit = string.Empty,
        TextAlign = "default",
        Warnings = [],
        Children =
        [
            Element(ViewElementKind.Numeric, "待发送值", source: "value"),
            Element(ViewElementKind.Numeric, "已发送", source: "sent"),
            Element(ViewElementKind.Curve, "波形", source: "wave"),
            Element(ViewElementKind.TextLog, "接收日志", source: "log", height: new AxisLayout { Fill = true }),
            Element(ViewElementKind.TextInput, "值", input: "value"),
            Element(ViewElementKind.CommandButton, "发送", command: "send"),
            Element(ViewElementKind.ToggleSlider, "自动发送", input: "auto"),
            Element(ViewElementKind.ConfigurationEditor, "显示"),
        ],
    };

    /// <summary>样例元素节点（把与示例无关的必填键一次填好，只留真正要区分的角色键）。</summary>
    private static ViewNode Element(
        ViewElementKind kind,
        string title,
        string? source = null,
        string? command = null,
        string? input = null,
        AxisLayout? height = null) => new()
    {
        Kind = kind.DeclarationName(),
        Title = title,
        Width = new AxisLayout { Fill = true },
        Height = height ?? new AxisLayout { Adaptive = true },
        Layout = "flow",
        Overflow = "scroll",
        Columns = [],
        Rows = [],
        Unit = string.Empty,
        TextAlign = "default",
        Warnings = [],
        Children = [],
        DataSource = source,
        Command = command,
        Input = input,
    };
}
