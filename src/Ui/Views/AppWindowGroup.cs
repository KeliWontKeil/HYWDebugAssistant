using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **应用窗口组**（ADR-0036 D5）：把一个应用的全部顶层窗口当作**一个整体**来管理层级。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>应用内</b>：模块窗口以**外壳为 Owner**（外壳永在模块窗口之下；最小化/恢复随外壳），
///     弹窗同样以外壳为 Owner；</item>
///   <item><b>应用整体（相对其他应用）只有两种统一状态</b>：
///     <b>普通</b> —— 任一本应用窗口被激活时，把其余本应用窗口**一并抬到最前**（不抢焦点、保持内部次序）；
///     <b>全体置顶</b> —— 本组所有窗口一起 <see cref="Window.Topmost"/>（含之后新开的窗口），
///     **不提供"单窗口置顶"**，避免"一部分被覆盖、一部分置顶"的混合状态；</item>
///   <item><b>平台代价</b>：普通状态下的"组抬升"需要 <c>SetWindowPos(HWND_TOP, SWP_NOACTIVATE)</c>
///     （Avalonia 未提供"抬升但不激活"的 API）；非 Windows 平台自动降级为不做组抬升。</item>
/// </list>
/// </remarks>
public sealed class AppWindowGroup : IDisposable
{
    private readonly List<Window> _windows = [];
    private readonly Window _shell;
    private bool _disposed;

    /// <summary>创建窗口组（外壳窗口必须在首位）。</summary>
    /// <param name="shell">外壳窗口。</param>
    public AppWindowGroup(Window shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        Track(shell);
    }

    /// <summary>本组是否处于"全体置顶"状态。</summary>
    public bool Topmost { get; private set; }

    /// <summary>把窗口纳入本组（模块窗口与弹窗在创建后调用）。</summary>
    /// <param name="window">窗口。</param>
    public void Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_windows.Contains(window))
        {
            return;
        }

        _windows.Add(window);
        window.Activated += OnAnyWindowActivated;
        window.Closed += OnWindowClosed;
        window.Topmost = Topmost;
    }

    /// <summary>切换"全体置顶"（本组全部窗口一起，含弹窗）。</summary>
    /// <param name="topmost">是否置顶。</param>
    public void SetTopmost(bool topmost)
    {
        Topmost = topmost;

        foreach (var window in _windows)
        {
            window.Topmost = topmost;
        }
    }

    /// <summary>把本组全部窗口抬到最前（不改变焦点）。</summary>
    public void RaiseAll()
    {
        foreach (var window in _windows)
        {
            WindowZOrder.RaiseWithoutActivating(window);
        }
    }

    /// <summary>
    /// 任一本应用窗口被激活 → 把其余窗口一并抬升：对外表现为"整个应用一起进出"，
    /// 不会出现"应用的一部分被别的应用盖住"。
    /// </summary>
    private void OnAnyWindowActivated(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        foreach (var window in _windows)
        {
            if (!ReferenceEquals(window, sender))
            {
                WindowZOrder.RaiseWithoutActivating(window);
            }
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Activated -= OnAnyWindowActivated;
            window.Closed -= OnWindowClosed;
            _ = _windows.Remove(window);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var window in _windows)
        {
            window.Activated -= OnAnyWindowActivated;
            window.Closed -= OnWindowClosed;
        }

        _windows.Clear();
        _ = _shell;
    }
}

/// <summary>窗口 z 序的**平台适配**（仅 Windows 生效；其他平台为无操作）。</summary>
internal static class WindowZOrder
{
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>把窗口抬到最前，但**不激活**它（不抢焦点）。</summary>
    /// <param name="window">目标窗口。</param>
    internal static void RaiseWithoutActivating(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(handle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
