using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.Serial.Views;

/// <summary>
/// **串口窗口的视图**（XAML + 编译期绑定）：布局在 <c>SerialView.axaml</c> 里。
/// </summary>
/// <remarks>
/// 这里的代码只有一件事：把 Avalonia 的 XAML 载入（控件与几何全在 XAML 里，不在这里 new）。
/// </remarks>
[ModuleViewDesignData(typeof(SerialConfig))]
public sealed partial class SerialView : ModuleView
{
    /// <summary>构造视图。</summary>
    public SerialView() => AvaloniaXamlLoader.Load(this);
}

/// <summary>
/// **串口的视图工厂**：框架按实例调一次，造出 <see cref="SerialView"/>。
/// </summary>
/// <remarks>
/// 工厂必须无状态（它可能被多个实例复用）；实例相关的数据全在
/// <see cref="IModuleViewContext"/> 里，由控件按帧去取。
/// </remarks>
public sealed class SerialViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new SerialView();
}
