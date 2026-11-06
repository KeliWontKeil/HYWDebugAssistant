using Xunit;

// **本程序集是“截图工具”，不是判据**：三个生成入口把真实渲染栈下的窗口画成 PNG，
// 交给人眼看（输出在 `artifacts\snapshots\`）。这里不写任何“画得对不对”的断言。
//
// 入口必须串行：Avalonia 的 headless 会话与应用级样式装载是**进程级**的。
// 会话由 `SnapshotSession` 在进程内**只建一次且不释放**——per-test `StartNew` + `using` 会在
// `HeadlessUnitTestSession.Dispose` 抛 `NullReferenceException`（Avalonia 已知问题，栈里没有本工程代码）。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
