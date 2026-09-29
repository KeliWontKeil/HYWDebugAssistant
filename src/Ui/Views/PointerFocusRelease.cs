using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **点空白/非可聚焦处 = 主动放弃键盘焦点**〔2026-09 用户裁定："鼠标必须点其他控件/窗口才能离开焦点，
/// **点其他空白不行**"〕——全局一处，主窗口与模块窗口同时受益。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么挂在 `Window` 的类处理器上</b>：所有窗口（外壳、模块窗口、弹窗）都是 `Window`
///     ⇒ 一处注册、处处生效，且**不碰任何窗口自己的输入处理**（模块窗口的拖动/缩放手柄照常工作）；</item>
///   <item><b>为什么是「隧道」阶段</b>：要在**目标控件处理之前**决定"这一下点到的算不算可聚焦处"；
///     隧道从根往下走，我们只是**观察**（除"释放焦点"外不改任何状态、不置 `Handled`）；</item>
///   <item><b>判据 = 命中的那个可视元素到窗口之间有没有"可聚焦且可用"的祖先</b>：
///     按钮 / 开关 / 文本框 / 滚动条都有自己的可聚焦部件 ⇒ **行为与今天完全一致**；
///     而空白背景、标签文字、边框这些都不可聚焦 ⇒ 释放焦点；</item>
///   <item><b>释放的是"这一下点击所属窗口"的焦点</b>（`FocusManager` 按 `TopLevel` 分家）——
///     正是用户描述的场景："点别的窗口"本来就会切窗口，这里补的是"点本窗口的空白"。</item>
/// </list>
/// </remarks>
public static class PointerFocusRelease
{
    private static bool _installed;

    /// <summary>
    /// 安装全局规则（**幂等**；由应用启动时调用一次 —— 见 <c>App.Initialize</c>）。
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;

        InputElement.PointerPressedEvent.AddClassHandler<Window>(
            static (window, args) => OnPointerPressed(window, args),
            RoutingStrategies.Tunnel);
    }

    /// <summary>一次指针按下：命中的地方**没有任何可聚焦祖先** ⇒ 释放焦点。</summary>
    /// <param name="window">事件所属窗口。</param>
    /// <param name="args">指针事件参数。</param>
    private static void OnPointerPressed(Window window, PointerPressedEventArgs args)
    {
        if (IsFocusableHit(args.Source as Visual, window))
        {
            return;
        }

        // 已经没焦点 ⇒ 不做任何事（避免无谓的状态变更，也让"点空白"在无焦点时完全无副作用）
        // `Focus(null, ...)` = 本窗口不再有焦点元素（与"把焦点转给别的控件"是两件事）
        _ = window.FocusManager?.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
    }

    /// <summary>
    /// 这次命中的东西里（含各级祖先，直到窗口）**有没有可聚焦的控件**。
    /// </summary>
    /// <param name="source">命中的可视元素。</param>
    /// <param name="window">窗口（走到它就停：窗口本身不算"点了别的东西"）。</param>
    /// <returns>有可聚焦祖先 ⇒ <see langword="true"/>（这一下点击照今天的规矩交给控件自己处理）。</returns>
    /// <remarks>
    /// **判据只有"框架说的可聚焦 + 可用"这一条**：不列举控件类型（列类型就一定会漏，
    /// 而且第三方/将来新增的控件会静默掉队）—— `Focusable` 是框架的既有事实，`IsEffectivelyEnabled`
    /// 保证"禁用控件不吃点击"。
    /// </remarks>
    private static bool IsFocusableHit(Visual? source, Window window)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, window); current = current.GetVisualParent())
        {
            if (current is InputElement { Focusable: true, IsEffectivelyEnabled: true })
            {
                return true;
            }
        }

        return false;
    }
}
