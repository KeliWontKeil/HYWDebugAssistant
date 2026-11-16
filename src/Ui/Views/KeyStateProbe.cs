using System.Runtime.InteropServices;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **当前键盘修饰键的即时状态**：用于解释系统标题栏拖动的外壳移动事件。
/// </summary>
/// <remarks>
/// 外壳拖动走非客户区，Avalonia 拿不到指针/键盘事件；判断"这次拖动是否按着 Ctrl"
/// 只能问平台。非 Windows 平台返回 <see langword="false"/>（退化为没有 Ctrl 拖开）；
/// 其它平台若将来要支持，在这里加一个平台分支即可，调用方不需要认识平台。
/// </remarks>
internal static class KeyStateProbe
{
    private const int VkControl = 0x11;
    private const int PressedMask = 0x8000;

    /// <summary>Ctrl 当前是否按下。</summary>
    public static bool IsControlDown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return (GetAsyncKeyState(VkControl) & PressedMask) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
