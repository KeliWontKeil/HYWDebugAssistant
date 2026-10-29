using Xunit;

// **本程序集内的用例必须串行**：Avalonia 的 headless 会话与应用级样式装载是**进程级**的。
// 会话由 `SnapshotSession` 在进程内**只建一次且不释放**——per-test `StartNew` + `using` 会在
// `HeadlessUnitTestSession.Dispose` 抛 `NullReferenceException`（Avalonia 已知问题，栈里没有本工程代码，
// 与断言无关，表现为"每次门禁掷骰子"）。测试进程退出即回收会话。
//
// xUnit 默认**按测试类并行**；把它收进一个 collection、并禁用集合并行 ⇒ 全程序集串行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
