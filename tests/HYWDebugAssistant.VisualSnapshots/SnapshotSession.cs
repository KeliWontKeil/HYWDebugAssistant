using Avalonia.Headless;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **快照用例共用的 headless 会话**：进程内只建一次、永不释放。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么不再 per-test `StartNew` + `using`</b>：同一进程里反复创建/释放会话，会在
///     <see cref="HeadlessUnitTestSession.Dispose"/> 抛 <c>NullReferenceException</c>（Avalonia 已知问题，
///     栈里没有本工程代码），表现为"每次门禁掷骰子"；</item>
///   <item><b>为什么只建一次</b>：headless 平台与 <c>Application</c> 都是进程级的；测试进程退出即回收，
///     不需要也不应该在用例之间拆装会话；</item>
///   <item><b>两个入口</b>：常规快照用 <see cref="Shared"/>（带 SnapshotApp 资源）；
///     「预览环境自包含控件主题」那条用例需要**不带 Sdk.Ui 主题**的宿主，故单独用一个同样只建一次的
///     <see cref="BarePreview"/> 会话 —— 二者都在进程内各启动一次、都不释放。</item>
/// </list>
/// </remarks>
internal static class SnapshotSession
{
    /// <summary>常规快照会话（真实 Skia 渲染栈 + <c>SnapshotApp</c> 资源）。</summary>
    public static HeadlessUnitTestSession Shared { get; } =
        HeadlessUnitTestSession.StartNew(typeof(OverlaySnapshotTests.SkiaHeadlessEntryPoint));

    /// <summary>无 App 主题的预览会话（只服务「预览环境自包含控件主题」一条用例）。</summary>
    public static HeadlessUnitTestSession BarePreview { get; } =
        HeadlessUnitTestSession.StartNew(typeof(ModuleViewSmokeTests.BarePreviewEntryPoint));
}
