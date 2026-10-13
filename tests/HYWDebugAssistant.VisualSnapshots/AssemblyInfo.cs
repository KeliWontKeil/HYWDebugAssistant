using Xunit;

// **本程序集内的用例必须串行**：每一条快照用例都会 `HeadlessUnitTestSession.StartNew`
// 并装一份应用级资源/样式，而 Avalonia 的渲染会话与样式装载是**进程级**的 ——
// 两条会话并行时会互相踩（实测：`HeadlessUnitTestSession.Dispose` 抛 `NullReferenceException`、
// 或某条用例在启动期就以 50ms 失败，重跑又绿）。这类失败与断言无关，只会把真正的缺陷淹掉。
//
// xUnit 默认**按测试类并行**；把它收进一个 collection、并禁用集合并行 ⇒ 全程序集串行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
