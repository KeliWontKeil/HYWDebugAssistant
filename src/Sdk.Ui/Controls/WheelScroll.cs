using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **滚轮滚动扩展**：只有横向滚动能力的 <see cref="ScrollViewer"/> 直接用纵向滚轮横向滚动。
/// </summary>
/// <remarks>
/// <para>
/// 默认 `ScrollViewer` 用纵向滚轮；只有横向滚动条时滚轮往往毫无反应（用户必须按 Shift）。
/// 这里只在“不能纵向滚、能横向滚”的交集里接管，其它情况完全不动，纵向滚动与 Shift+滚轮保持原行为。
/// </para>
/// <para>
/// 行为挂在 `ScrollViewer` 上；`Sdk.Ui/Themes/Controls.axaml` 默认给所有 `ScrollViewer` 打开，
/// 因此模块模板内部的滚动容器（例如 `ModuleSegmentedControl`）也自动获得。
/// </para>
/// </remarks>
public static class WheelScroll
{
    /// <summary>是否启用“纵向滚轮 → 横向滚动”的回退。</summary>
    public static readonly AttachedProperty<bool> HorizontalFallbackProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, ScrollViewer, bool>("WheelScrollHorizontalFallback");

    static WheelScroll()
    {
        HorizontalFallbackProperty.Changed.AddClassHandler<ScrollViewer>(static (viewer, args) =>
        {
            if (args.GetNewValue<bool>())
            {
                // 滚轮事件按冒泡路由；默认处理器先跑，但纵向不可滚时它不会产生效果，
                // 这里仍然接管（handledEventsToo），把纵向滚轮换成横向滚动。
                viewer.AddHandler(
                    InputElement.PointerWheelChangedEvent,
                    OnPointerWheelChanged,
                    RoutingStrategies.Bubble,
                    handledEventsToo: true);
            }
            else
            {
                viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
            }
        });
    }

    /// <summary>读取标注。</summary>
    /// <param name="viewer">滚动视口。</param>
    public static bool GetHorizontalFallback(ScrollViewer viewer) => viewer.GetValue(HorizontalFallbackProperty);

    /// <summary>写入标注。</summary>
    /// <param name="viewer">滚动视口。</param>
    /// <param name="value">是否启用。</param>
    public static void SetHorizontalFallback(ScrollViewer viewer, bool value) =>
        viewer.SetValue(HorizontalFallbackProperty, value);

    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        // 只有“纵向滚不动、横向滚得动”的视口才接管；其余交给默认滚动。
        var canScrollVertically = viewer.Extent.Height > viewer.Viewport.Height + 0.5;
        var canScrollHorizontally = viewer.Extent.Width > viewer.Viewport.Width + 0.5;
        if (canScrollVertically || !canScrollHorizontally)
        {
            return;
        }

        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (delta == 0)
        {
            return;
        }

        var line = viewer.SmallChange.Width > 0 ? viewer.SmallChange.Width : 16;
        var step = Math.Max(1, line) * 3;
        var maximum = viewer.ScrollBarMaximum.X;
        var next = Math.Clamp(viewer.Offset.X - (delta * step), 0, maximum);

        if (Math.Abs(next - viewer.Offset.X) < 0.01)
        {
            return;
        }

        viewer.Offset = new Vector(next, viewer.Offset.Y);
        e.Handled = true;
    }
}

