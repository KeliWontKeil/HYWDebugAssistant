using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **主窗口阴影抑制**：Windows 的普通顶层窗口会带一圈系统阴影；它不属于 Avalonia 的可样式化边框。
/// </summary>
/// <remarks>
/// 只能按窗口类去掉 <c>CS_DROPSHADOW</c>；类样式是进程范围的，因此本应用同类窗口会一起失去这层阴影。
/// 非 Windows 或取不到句柄时静默跳过（不影响功能）。
/// </remarks>
internal static class WindowShadow
{
    private const int GclStyle = -26;
    // 绝对色：这是 Win32 的 CS_DROPSHADOW 样式位，不是颜色值（审计按 ARGB 字面量误报）。
    private const int CsDropShadow = 0x00020000;

    /// <summary>尝试抑制窗口的系统阴影。</summary>
    /// <param name="window">目标窗口。</param>
    public static void Suppress(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var style = GetClassLongPtr(handle, GclStyle).ToInt64();
            if ((style & CsDropShadow) == 0)
            {
                return;
            }

            _ = SetClassLongPtr(handle, GclStyle, new IntPtr(style & ~CsDropShadow));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // 平台差异：没有这个入口就不改，界面仍可用。
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int index, IntPtr value);
}
