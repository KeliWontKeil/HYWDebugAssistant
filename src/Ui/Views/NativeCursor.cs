using System.Runtime.InteropServices;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **系统光标位置**（屏幕物理像素）——拖动/缩放时用它取指针位置。
/// </summary>
/// <remarks>
/// 【用户第十二轮第二轮反馈 · "还是振荡，而且左右抖、上下不抖"】
/// 原先的取法是 <c>window.Position + 指针在窗口内的局部坐标 × 缩放</c>：**两个来源不是同一次采样**——
/// 窗口位置是我们自己刚设进去的，而局部坐标来自系统在消息生成时刻算的"光标 − 窗口矩形"。
/// 拖动中窗口每帧都在动（吸附命中时还会整块跳几像素），两者一旦差一帧，算出来的"屏幕指针位置"就带着
/// **等于窗口位移量的误差** → 指针读数抖 → 模块位置抖 → 窗口再动 …… 形成**回授**。
/// 拖动通常是水平方向，所以表现为"**左右抖、上下不抖**"，且**与网络标签无关**。
/// 取系统光标是权威值：与本窗口自己的位置完全无关，从源头断掉回授。
/// </remarks>
internal static class NativeCursor
{
    /// <summary>取系统光标位置（屏幕物理像素）；非 Windows 或调用失败时返回 false（调用方回退到窗口换算）。</summary>
    /// <param name="x">光标 X（物理像素）。</param>
    /// <param name="y">光标 Y（物理像素）。</param>
    /// <returns>是否取到。</returns>
    public static bool TryGetScreenPosition(out double x, out double y)
    {
        x = 0;
        y = 0;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!GetCursorPos(out var point))
        {
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}
