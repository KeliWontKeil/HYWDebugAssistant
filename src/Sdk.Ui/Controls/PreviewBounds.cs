using Avalonia;
using Avalonia.Controls;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **预览范围框的开关**：默认开启；标在某个控件上设为 `False` 可让 <see cref="ModuleView"/> 不再为它画虚线框。
/// </summary>
/// <remarks>
/// 范围框本身由 <see cref="ModuleView"/> 在预览环境统一附加，控件不需要自己画；
/// 这个附加属性只用于“某个元素太大/太密，预览里不想看它的框”的例外。
/// </remarks>
public static class PreviewBounds
{
    /// <summary>是否显示预览范围框（默认 <see langword="true"/>）。</summary>
    public static readonly AttachedProperty<bool> ShowProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("PreviewBoundsShow", defaultValue: true);

    /// <summary>读取标注。</summary>
    /// <param name="control">控件。</param>
    public static bool GetShow(Control control) => control.GetValue(ShowProperty);

    /// <summary>写入标注。</summary>
    /// <param name="control">控件。</param>
    /// <param name="value">是否显示范围框。</param>
    public static void SetShow(Control control, bool value) => control.SetValue(ShowProperty, value);
}
/// <summary>预览框的**角色**：控件框表达控件自身边界，容器框表达布局槽位。</summary>
public enum PreviewBoundsRole
{
    /// <summary>控件框（虚线）：框住控件的最终边界。</summary>
    Control = 0,

    /// <summary>容器框（点线）：框住布局容器/槽位，例如模块内容区、行、栏。</summary>
    Container = 1,
}

/// <summary>一个预览框目标：控件 + 角色（角色决定线型与浓度）。</summary>
/// <param name="Control">要画框的控件。</param>
/// <param name="Role">角色。</param>
public readonly record struct PreviewBoundsTarget(Control Control, PreviewBoundsRole Role);