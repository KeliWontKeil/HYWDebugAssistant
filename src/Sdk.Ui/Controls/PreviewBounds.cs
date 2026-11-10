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

