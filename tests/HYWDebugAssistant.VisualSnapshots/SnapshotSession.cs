using Avalonia.Headless;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **截图工具共用的 headless 会话**：进程内只建一次、永不释放。
/// </summary>
/// <remarks>
/// 同一进程里反复创建/释放会话会在 `HeadlessUnitTestSession.Dispose` 抛 `NullReferenceException`
/// （Avalonia 已知问题，栈里没有本工程代码）。无头平台与 `Application` 也都是进程级的；
/// 测试进程退出即回收，不需要也不应该在入口之间拆装会话。
/// </remarks>
internal static class SnapshotSession
{
    /// <summary>真实 Skia 渲染栈 + <c>SnapshotApp</c> 资源。</summary>
    public static HeadlessUnitTestSession Shared { get; } =
        HeadlessUnitTestSession.StartNew(typeof(SkiaHeadlessEntryPoint));
}
