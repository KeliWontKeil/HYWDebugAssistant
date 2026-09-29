using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Design;
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

        var view = new ViewDeclaration(
            [
                new ViewElement(ViewElementKind.Numeric, "value", "待发送值"),
                new ViewElement(ViewElementKind.Numeric, "sent", "已发送"),
                new ViewElement(ViewElementKind.Curve, "wave", "波形"),
                new ViewElement(ViewElementKind.TextLog, "log", "接收日志"),
                new ViewElement(ViewElementKind.TextInput, "valueInput", "值"),
                new ViewElement(ViewElementKind.CommandButton, "send"),
                new ViewElement(ViewElementKind.ToggleSlider, "auto", "自动发送"),
                new ViewElement(ViewElementKind.ConfigurationEditor, title: "显示"),
            ],
            [
                new ViewDataSource("value", format: "F3", kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.F32),
                new ViewDataSource("sent", kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64),
                new ViewDataSource("wave", kind: ViewDataSourceKind.Sequence, valueType: ContractPrimitive.F32),
                new ViewDataSource("log", kind: ViewDataSourceKind.Text),
            ],
            [new ViewCommand("send", "发送", icon: "play", description: "按当前值发送一个数据单元")]);

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
                ModuleViewViewModel.DefaultWidth,
                ModuleViewViewModel.DefaultHeight),
            EmptyRuntime.Instance,
            EmptyRuntime.Instance,
            scale: 1.0,
            fallbackX: 0,
            fallbackY: 0);
    }
}
