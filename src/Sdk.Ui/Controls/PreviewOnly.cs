using Avalonia;
using Avalonia.Controls;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **仅预览可见**：给只在设计/预览环境出现的装饰件用（例如图表槽位的布局外框）。
/// </summary>
/// <remarks>
/// 运行期把控件设为不可见；预览期保持 XAML 的可见状态。它只作用于显式标注的控件，
/// 普通模块内容不受影响。
/// </remarks>
public static class PreviewOnly
{
    /// <summary>是否仅预览可见。</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("PreviewOnlyIsEnabled");

    static PreviewOnly()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(static (control, args) =>
        {
            if (args.GetNewValue<bool>() && !ModuleView.IsDesignEnvironment)
            {
                control.IsVisible = false;
            }
        });
    }

    /// <summary>读取标注。</summary>
    /// <param name="control">控件。</param>
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    /// <summary>写入标注。</summary>
    /// <param name="control">控件。</param>
    /// <param name="value">是否仅预览可见。</param>
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);
}

