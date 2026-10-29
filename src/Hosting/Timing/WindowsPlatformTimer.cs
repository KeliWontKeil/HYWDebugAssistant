using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HYWDebugAssistant.Hosting.Timing;

/// <summary>
/// Windows 高分辨率 waitable timer 后端（低 CPU 的 1 ms 级等待）。
/// </summary>
/// <remarks>
/// 只存在于框架计时层，模块层不引用任何 Windows API。创建失败（旧系统/权限）时回退到托管后端。
/// </remarks>
internal sealed class WindowsPlatformTimer : IPlatformTimer
{
    private const uint CreateWaitableTimerHighResolution = 2;
    private const uint TimerAllAccess = 2031619;
    private const uint Infinite = uint.MaxValue;
    private const uint WaitObject0 = 0;
    private const uint WaitFailed = uint.MaxValue;

    private readonly IPlatformTimer _fallback;
    private readonly IntPtr _timer;

    /// <summary>创建后端。</summary>
    /// <param name="fallback">高精度 API 不可用时的回退后端。</param>
    public WindowsPlatformTimer(IPlatformTimer fallback)
    {
        _fallback = fallback;
        _timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
    }

    /// <inheritdoc />
    public bool WaitUntil(long deadlineTicks, WaitHandle wake)
    {
        if (_timer == IntPtr.Zero)
        {
            return _fallback.WaitUntil(deadlineTicks, wake);
        }

        var remaining = deadlineTicks - Stopwatch.GetTimestamp();
        if (remaining <= 0)
        {
            return false;
        }

        var milliseconds = (long)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency);
        if (milliseconds <= 0)
        {
            return false;
        }

        var dueTime = -milliseconds * 10_000;   // relative, 100 ns units
        if (!SetWaitableTimer(_timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            return _fallback.WaitUntil(deadlineTicks, wake);
        }

        var handles = new[] { _timer, wake.SafeWaitHandle.DangerousGetHandle() };
        var result = WaitForMultipleObjects(2, handles, false, Infinite);

        return result switch
        {
            WaitObject0 => false,
            WaitObject0 + 1 => true,
            WaitFailed => _fallback.WaitUntil(deadlineTicks, wake),
            _ => false,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_timer != IntPtr.Zero)
        {
            _ = CloseHandle(_timer);
        }

        _fallback.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWaitableTimerExW(
        IntPtr lpTimerAttributes,
        string? lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        IntPtr hTimer,
        ref long lpDueTime,
        int lPeriod,
        IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine,
        [MarshalAs(UnmanagedType.Bool)] bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(
        uint nCount,
        IntPtr[] lpHandles,
        [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
        uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
