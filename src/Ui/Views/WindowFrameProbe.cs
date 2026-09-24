using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **窗口可见外框**（屏幕物理像素）—— 外壳作为吸附参照时，用它而不是"客户区"。
/// </summary>
/// <remarks>
/// 【用户第十二轮反馈 · 裁决 D-4：直接改用可见外框右边缘】此前参照取 `PointToScreen(0,0) + ClientSize`
/// （= 客户区右边缘），而用户视觉上的"窗口边缘"是**可见外框**：两者相差"可见边框（约 1px）"，
/// 于是模块贴上去**总有 1px 的缝**。
/// <list type="bullet">
///   <item><c>DWMWA_EXTENDED_FRAME_BOUNDS</c>（= 9）= **可见外框**：不含 Win 的不可见缩放边，正是用户看到的边；</item>
///   <item>取不到时回退 <c>GetWindowRect</c>（含不可见缩放边，会略宽），再取不到则由调用方回退客户区。</item>
/// </list>
/// 返回的是**物理像素**（本进程按显示器 DPI 感知），与模型几何同一坐标系，不需要再换算缩放。
/// </remarks>
internal static class WindowFrameProbe
{
    /// <summary>取窗口的可见外框（物理像素）；非 Windows 或调用失败时返回 false。</summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="frame">可见外框（X/Y/宽/高，物理像素）。</param>
    /// <returns>是否取到。</returns>
    public static bool TryGetVisibleFrame(Window window, out (double X, double Y, double Width, double Height) frame)
    {
        ArgumentNullException.ThrowIfNull(window);

        frame = default;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (DwmGetWindowAttribute(handle, DwmExtendedFrameBounds, out var visible, Marshal.SizeOf<Rect>()) == 0
            && visible.Right > visible.Left
            && visible.Bottom > visible.Top)
        {
            frame = (visible.Left, visible.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);
            return true;
        }

        if (GetWindowRect(handle, out var outer) && outer.Right > outer.Left && outer.Bottom > outer.Top)
        {
            frame = (outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top);
            return true;
        }

        return false;
    }

    /// <summary>窗口矩形（Win32 RECT，物理像素）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int DwmExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
}
