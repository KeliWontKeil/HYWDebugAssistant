using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk.Contracts;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **人机操作链路判据**（判据载体 = 四个基础模块）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>覆盖"人来戳"的完整链路：**文本输入（B5）+ 命令按钮（B2）+ 开关（B3）+ 数值输入** →
///     投递到模块线程的收件箱 → 模块活动体消费 → **网络** → 数据显示器（A5 显示源）；</item>
///   <item>**链路载体**：**`hyw/text-sender` → `hyw/data-monitor`**（手工验收的参考模块不再是载体）；</item>
///   <item>**非法输入的可见性**：范围与业务合法性归模块 ⇒ 打到对应输入框上（红框）⇒
///     断言只需「**失败必须可见**」（G3 输入处理反馈，不要求上报 Issue）；</item>
///   <item>框架能力**一个都没删**（仲裁 / 阈值断言 / 文件记录等参考模块独有的能力，见本项目顶部"能力 → 载体"对照）。</item>
/// </list>
/// </remarks>
public class BusinessChainTests
{
    /// <summary>文本发送器 → 数据显示器：**输入文本 + 发送命令 ⇒ 显示器收到一条**。</summary>
    [Fact]
    public void 发送器_输入文本加发送命令应产生一个数据单元()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("sender", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        // 建网唯一机制：两侧写同名网络标签
        composition.Services.UpdatePortNetwork("sender", TextSenderModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

        composition.Services.GetNetworks().ShouldHaveSingleItem().Name.ShouldBe("bus");

        // 「数据包换行」开 ⇒ 每个包一条（换行由**通道调用**表达，见 `IViewDisplayAccess.AppendLine`）
        composition.Submit("monitor", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        // 用户动作：输入框里写字（像 UI 一样写进视图状态）+ 点「发送」（模块回读当前页文本）
        var payload = "hello 链路";
        composition.Type("sender", TextSenderModule.TextSourceOf(0), payload);
        composition.Services.SendCommand("sender", TextSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitForDisplayCount("monitor", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(Diagnose(composition, "monitor", DataMonitorModule.LogSource));

        composition.DisplayText("monitor", DataMonitorModule.LogSource).ShouldContain(payload);

        // 再发一次 ⇒ 又一条（"点一下发一次"）；发送器的「已发送」计数同步增长
        var baseline = composition.DisplayLines("monitor", DataMonitorModule.LogSource).Count;
        var sentBaseline = composition.Written("sender", TextSenderModule.SentSource);

        composition.Type("sender", TextSenderModule.TextSourceOf(0), "second");
        composition.Services.SendCommand("sender", TextSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitForDisplayCount("monitor", DataMonitorModule.LogSource, atLeast: baseline + 1)
            .ShouldBeTrue(Diagnose(composition, "monitor", DataMonitorModule.LogSource));
        composition.WaitForCount("sender", TextSenderModule.SentSource, atLeast: sentBaseline + 1).ShouldBeTrue();
    }

    /// <summary>
    /// **已连通的网络上，发送一次都不许被静默拒绝**（偶发静默丢单元的回归网）。
    /// </summary>
    /// <remarks>
    /// <b>被钉住的缺陷</b>：派生网络**每次变更提交**都会重算，而旧实现是"**先把该网络的绑定全部移除、再重建**"
    /// ⇒ 两步之间（含逐源重建契约布局的整个过程）有一个"该网络没有任何绑定的空窗"；
    /// 模块线程恰在此刻 `TrySend` ⇒ `Deliver` 遍历快照找不到绑定 ⇒ 返回 false ⇒ 单元**被静默丢弃**
    /// （既不计入"传输丢弃"——没有绑定可记，也没有 Issue）。现场取证：`多源链路` 用例偶发红，
    /// 报"发了但没到"，而绑定诊断全 0、Issue 为空、发送器的 `sent` 也没涨（**product 口径对不出来**）。
    /// <para>
    /// <b>本用例为什么抓得住</b>：① 用**计数桩**把"被拒绝的次数"变成可观测事实（产品模块只统计成功次数，
    /// 丢了也不会少一行——对账恒等）；② 一边让桩**在自己的线程上 1 ms 一条持续发**，一边在测试线程上
    /// **持续提交变更**（提交发生在**另一个实例**上 ⇒ 与发送线程真正并发，不是"提交顺带触发发送"）。
    /// 判据 = **被拒绝次数必须是 0**。旧实现下这条会红（窗口宽达"逐源重建绑定"的整个过程）；修好后恒绿。
    /// </para>
    /// </remarks>
    [Fact]
    public void 链路_已连通网络上发送不得被静默拒绝()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();
        composition.Register("reject-counting-sender", typeof(Stubs.RejectCountingSenderStubModule));
        composition.Register("rate-source", typeof(Stubs.RateSourceStubModule));

        composition.Services.AddModule("p", "test/reject-counting-sender").Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("quiet", "test/rate-source").Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("p", Stubs.RejectCountingSenderStubModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

        // 显示器：每包一行（不影响本判据，只把链路做成"真在用"的形态）
        composition.Submit("monitor", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        // 先确认"确实在发"。**连上之前** `TrySend` 被拒是**合法**的（没网络就没得发）⇒ 以此刻为基准量增量。
        composition.WaitForCount("p", Stubs.RejectCountingSenderStubModule.SentSource, atLeast: 5)
            .ShouldBeTrue(Diagnose(composition, "p", Stubs.RejectCountingSenderStubModule.SentSource));

        var rejectedBaseline = (long)composition.Latest("p", Stubs.RejectCountingSenderStubModule.RejectedSource);

        // **变更风暴**：每次提交都会重算派生网络（旧实现：移除 → 重建 ⇒ 空窗）。
        // 提交打在**第三个实例**上（与两个在跑的实例无关）⇒ 风暴既不占显示器队列、也不触发发送，
        // 于是"发送被拒"只可能来自框架自己的时间窗，不会与"目标队列满（**有计数**的丢弃）"混淆。
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);

        while (DateTime.UtcNow < deadline)
        {
            composition.Services.UpdateConfiguration("quiet", new Stubs.RateSourceStubConfig
            {
                IntervalMs = 5,
                Value = 0.5,
            }).Succeeded.ShouldBeTrue();
        }

        var sent = (long)composition.Latest("p", Stubs.RejectCountingSenderStubModule.SentSource);
        var rejected = (long)composition.Latest("p", Stubs.RejectCountingSenderStubModule.RejectedSource) - rejectedBaseline;

        sent.ShouldBeGreaterThanOrEqualTo(50, $"变更风暴期间桩应一直在发（发了 {sent} 条）——否则本用例测不出东西");
        rejected.ShouldBe(
            0,
            $"**已连通的网络上 `TrySend` 不得被拒**：变更风暴期间发了 {sent} 条，被框架拒绝 {rejected} 条（= 静默丢单元）");
    }

    /// <summary>「连续发送」开关 + 「间隔」：按周期反复发；关掉即停。</summary>
    [Fact]
    public void 发送器_连续发送开关应按间隔发送()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("sender", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("sender", TextSenderModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

        composition.Type("sender", TextSenderModule.TextSourceOf(0), "tick");
        // 间隔 **0 = 未设置** ⇒ 连续发送**显式报错且不发** ⇒ 本用例必须给一个真实间隔
        // （要测的是"按间隔发送 + 关掉即停"，不是"没设置也硬发"）。
        composition.Type("sender", TextSenderModule.IntervalSource, "20");
        composition.Submit("sender", TextSenderModule.ContinuousSource, "true").Succeeded.ShouldBeTrue();

        composition.WaitForCount("sender", TextSenderModule.SentSource, atLeast: 5)
            .ShouldBeTrue(Diagnose(composition, "sender", TextSenderModule.SentSource));

        // 关掉开关 ⇒ 停发（语义可预期：没人操作就不产生数据）
        composition.Submit("sender", TextSenderModule.ContinuousSource, "false").Succeeded.ShouldBeTrue();
        Thread.Sleep(150);

        var settled = composition.Written("sender", TextSenderModule.SentSource);
        Thread.Sleep(150);
        composition.Written("sender", TextSenderModule.SentSource)
            .ShouldBe(settled, "关掉连续发送后不应再产生数据");
    }

    /// <summary>
    /// **有效速率 = 真实业务字节**（截断 / 补零的字节不算有效数据；
    /// 整个数据包大小的速率是底层协议口径）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **端到端跑真实链路 + 真实模块声明**：文本发送器（契约 <c>{ len:u16, data:bytes[1472] }</c>，
    /// 尾部补零）→ 显示器；发 <b>8 字节</b>文本、间隔 100 ms。
    /// </para>
    /// <para>
    /// <b>判据为什么这么写</b>：有效速率应当只有**几十 B/s**（≈8 B × 10 条/s），而搬运速率是**整帧**
    /// （≈1474 B × 10 条/s）⇒ 两者必须差出**两个数量级**。
    /// <list type="bullet">
    ///   <item>界面若取搬运口径（旧实现）⇒ 本用例红；</item>
    ///   <item>模块若把 `PayloadLengthField = "len"` 丢了（框架退回静态尺寸 = 契约尺寸）⇒ 本用例也红；</item>
    ///   <item>两个数**都必须 > 0**（"查不到"与"查到 0"分开表达：速率计断了会是 0，那也该红）。</item>
    /// </list>
    /// </para>
    /// </remarks>
    [Fact]
    public void 有效速率_只算真实业务字节_不含补零()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("sender", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("sender", TextSenderModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

        composition.Type("sender", TextSenderModule.TextSourceOf(0), "12345678");   // 8 字节
        composition.Type("sender", TextSenderModule.IntervalSource, "100");
        composition.Submit("sender", TextSenderModule.ContinuousSource, "true").Succeeded.ShouldBeTrue();

        composition.WaitForCount("sender", TextSenderModule.SentSource, atLeast: 10)
            .ShouldBeTrue(Diagnose(composition, "sender", TextSenderModule.SentSource));

        // 速率口径 = **整秒桶**（上一个 ≥1 秒窗口的积累量）⇒ 先观察一次建基准，再等满一个窗口
        composition.Services.GetPortMetrics("sender", TextSenderModule.OutPort);
        Thread.Sleep(1200);

        var metrics = composition.Services.GetPortMetrics("sender", TextSenderModule.OutPort);

        metrics.PayloadBytesPerSecond.ShouldBeGreaterThan(0, "有效速率必须是个正数（速率计断了会是 0）");
        metrics.BytesPerSecond.ShouldBeGreaterThan(0, "搬运口径同样有数（对照组）");
        metrics.PayloadBytesPerSecond.ShouldBeLessThan(
            metrics.BytesPerSecond / 50,
            $"有效速率只算 8 字节业务数据（实际 {metrics.PayloadBytesPerSecond:0.0} B/s），"
            + $"而搬运速率是整帧 ≈1474 B（实际 {metrics.BytesPerSecond:0.0} B/s）⇒ 两者应差两个数量级");

        // 累计流量同口径（用户选 A：与速率一起改成有效字节）
        metrics.PayloadBytesDelivered.ShouldBeGreaterThan(0);
        metrics.PayloadBytesDelivered.ShouldBeLessThan(
            metrics.BytesDelivered / 50,
            "累计有效字节同样不该把尾部补零算进去");
    }

    /// <summary>
    /// **连续发送读的是「当前选中页」**（针对「输入了内容反而发不了」的回归网）。
    /// </summary>
    /// <remarks>
    /// 发送器的发送路径是**回读**：「当前选中页 + 该页当前文本」。本用例刻意**不用第 1 页**：
    /// 在第 2 页（索引 1）打字并**把选中页切到第 2 页**，再打开「连续发送」⇒ 必须真的发出且计数增长。
    /// 这样一旦"页回读"坏了（例如永远读第 1 页），本用例立刻红。
    /// </remarks>
    [Fact]
    public void 连续发送_应发送当前选中页而不是第一页()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("sender", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("sender", TextSenderModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();
        composition.Submit("monitor", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        // 第 2 页（索引 1）打字 + 把选中页切到它
        composition.Type("sender", TextSenderModule.TextSourceOf(1), "second-page");
        composition.SelectPage("sender", TextSenderModule.PagesStateKey, 1);

        composition.Type("sender", TextSenderModule.IntervalSource, "20");
        composition.Submit("sender", TextSenderModule.ContinuousSource, "true").Succeeded.ShouldBeTrue();

        composition.WaitForCount("sender", TextSenderModule.SentSource, atLeast: 2)
            .ShouldBeTrue(Diagnose(composition, "sender", TextSenderModule.SentSource));

        composition.WaitForDisplayCount("monitor", DataMonitorModule.LogSource, atLeast: 1)
            .ShouldBeTrue(Diagnose(composition, "monitor", DataMonitorModule.LogSource));
        composition.WaitUntilDisplayText("monitor", DataMonitorModule.LogSource, "second-page")
            .ShouldBeTrue("显示器上应看到**第 2 页**的内容（说明回读的是当前选中页，而不是永远第 1 页）");

        // 关掉开关 ⇒ 停发（语义可预期）
        composition.Submit("sender", TextSenderModule.ContinuousSource, "false").Succeeded.ShouldBeTrue();
        Thread.Sleep(120);
        var settled = composition.Written("sender", TextSenderModule.SentSource);
        Thread.Sleep(120);
        composition.Written("sender", TextSenderModule.SentSource).ShouldBe(settled, "关掉连续发送后不应再产生数据");
    }

    /// <summary>**非法输入不能静默**：越界的「间隔」必须回报失败（G3 ⇒ 对应输入框红框）。</summary>
    [Fact]
    public void 发送器_非法输入应回报失败而不是静默()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("sender", TextSenderModuleId).Succeeded.ShouldBeTrue();

        // 超出 u32 ⇒ 模块自己判定非法，并**把那一格标成失败色**（框架不再替它判字符级写法）；
        // 判据是**角色词**：具体哪个红归用户设置管（见 `ViewInkRoles`）。
        composition.Type("sender", TextSenderModule.IntervalSource, "4294967296");

        Composition.WaitUntil(
            () => composition.Services.GetAppearance("sender")
                .Any(f => string.Equals(f.SourceName, TextSenderModule.IntervalSource, StringComparison.Ordinal)
                    && string.Equals(
                        f.OutlineRole,
                        HYWDebugAssistant.Sdk.Views.ViewInkRoles.Danger,
                        StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3)).ShouldBeTrue("非法输入必须被模块识别并标出来（不静默丢值）");
    }

    /// <summary>未声明的命令 / 输入必须被**拒绝**（否则用户点了按钮却毫无反应）。</summary>
    [Fact]
    public void 命令与输入_未声明的名字应被拒绝()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("sender", TextSenderModuleId).Succeeded.ShouldBeTrue();

        var unknownCommand = composition.Services.SendCommand("sender", "no-such-command");
        unknownCommand.Succeeded.ShouldBeFalse("未声明的命令必须被拒绝：否则用户点了按钮却毫无反应（不静默）");


        var unknownInput = composition.Submit("sender", "no-such-input", "1");
        unknownInput.Succeeded.ShouldBeFalse("未声明的输入源同样必须被拒绝");
    }

    /// <summary>
    /// **数据显示器**：收到的内容进**显示区**，文本就是**数据本身**（不再有定宽 96 字节与补齐）。
    /// </summary>
    /// <remarks>
    /// 一行多少字节**由文本框的宽度决定**，不预设固定列宽 ⇒
    /// 判据 = 显示区里的文本就是数据本身（不切块、不补齐、无标记）。
    /// </remarks>
    [Fact]
    public void 数据显示器_应把收到的内容原样追加到显示区()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("sender", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("sender", TextSenderModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

        composition.Submit("monitor", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();
        composition.Type("sender", TextSenderModule.TextSourceOf(0), "-2.25");
        composition.Services.SendCommand("sender", TextSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitUntilDisplayText("monitor", DataMonitorModule.LogSource, "-2.25")
            .ShouldBeTrue(Diagnose(composition, "monitor", DataMonitorModule.LogSource));

        // **文本就是数据**：没有 96 字节补齐、没有截断、没有标记
        var line = composition.DisplayLines("monitor", DataMonitorModule.LogSource)
            .First(l => l.Contains("-2.25", StringComparison.Ordinal));

        line.ShouldBe("-2.25", "原样呈现（旧实现会补空格到 96 字节，界面再去尾）");
        line.ShouldNotContain("↵", Case.Sensitive, "标记只由显示层画 ⇒ 文本里不能有");

        // 【只增纪律】老路径（发布定长样本）仍然可用，只是显示区不再从它取数
        composition.Written("monitor", DataMonitorModule.LogSource)
            .ShouldBe(0, "显示区走**通道追加**这条路 ⇒ 该数据源的\"发布样本\"计数应保持 0");

        // **数值类数据源**（发送器的「已发送」计数）同样可用（原判据里的"数值 + 解码"路径）
        composition.WaitForCount("sender", TextSenderModule.SentSource, atLeast: 1).ShouldBeTrue();
        composition.Latest("sender", TextSenderModule.SentSource).ShouldBeGreaterThanOrEqualTo(1);
    }

    /// <summary>**两个发送器同形状** ⇒ 都能直连同一个显示器（原判据：不同模块同形状可直接互通）。</summary>
    [Fact]
    public void 两个发送器_应与显示器同形状可直接互通()
    {
        using var composition = new Composition();
        composition.InstallBusinessPackages();

        composition.Services.AddModule("text", TextSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("hex", HexSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        // 三个口写同一个标签 ⇒ 一条**多源网络**
        composition.Services.UpdatePortNetwork("text", TextSenderModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("hex", HexSenderModule.OutPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", DataMonitorModule.InPort, "bus").Succeeded.ShouldBeTrue();

        var network = composition.Services.GetNetworks().ShouldHaveSingleItem();
        network.Sources.Count.ShouldBe(2, "两个发送器同属一条多源网络 ⇒ 显示器两条都收");

        composition.Submit("monitor", DataMonitorModule.LineBreakSource, "true").Succeeded.ShouldBeTrue();

        // 文本发送器：形状 = { len:u16, data:bytes[1472] }
        composition.Type("text", TextSenderModule.TextSourceOf(0), "T");
        composition.Services.SendCommand("text", TextSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        // HEX 发送器：同一个标识、同一个形状 ⇒ 无需任何转换/强连
        composition.Type("hex", HexSenderModule.HexSourceOf(0), "48 58");
        composition.Services.SendCommand("hex", HexSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitForDisplayCount("monitor", DataMonitorModule.LogSource, atLeast: 2)
            .ShouldBeTrue(Diagnose(composition, "monitor", DataMonitorModule.LogSource));

        // 形状断言（原判据的等价物）：三者逐字段一致
        ContractShape.AreSame(TextSenderModule.RawBytesShape, HexSenderModule.RawBytesShape).ShouldBeTrue();
        ContractShape.AreSame(TextSenderModule.RawBytesShape, DataMonitorModule.RawBytesShape).ShouldBeTrue();
        TextSenderModule.RawBytesShape.SizeInBytes.ShouldBe(2 + 1472);
    }

    private const string TextSenderModuleId = "hyw/text-sender";

    private const string HexSenderModuleId = "hyw/hex-sender";

    private const string MonitorModuleId = "hyw/data-monitor";

    /// <summary>失败时的现场说明（数据源计数 / 网络 / Issue），用于定位链路断点。</summary>
    private static string Diagnose(Composition composition, string instanceId, string dataSourceName)
    {
        var networks = string.Join(
            "；",
            composition.Services.GetNetworks().Select(n => $"{n.Name}: {n.Source} → {string.Join(",", n.Targets)}（停流={n.IsStalled}）"));

        var issues = string.Join(
            "；",
            composition.Services.GetIssues(10).Select(i => $"[{i.Severity}] {i.ObjectPath}: {i.Message}"));

        return $"未达到预期样本数：{instanceId}.{dataSourceName} 已写入 {composition.Written(instanceId, dataSourceName)}；"
            + $"网络：{networks}；Issue：{issues}";
    }

    /// <summary>
    /// 测试现场：真实运行时 + 真实服务面 + **四个基础模块包**（经真实包加载路径）。
    /// </summary>
    /// <remarks>`internal` 以便同项目的其它链路测试（如多源链路）复用同一现场，避免两套夹具漂移。</remarks>
    internal sealed class Composition : IDisposable
    {
        public Composition()
        {
            // **必须带视图状态存储**：发送器的「点按钮发送」是**回读**「当前页 + 该页文本」，
            // 而无头测试里没有 UI 去写它 ⇒ 夹具要像 UI 那样**打字**（见 `Type` 助手），否则命令路径读不到内容。
            ViewState = new HYWDebugAssistant.Core.Services.ViewStateStore();
            Runtime = new ModuleRuntime(viewState: ViewState);
            Services = new RuntimeServices(Runtime);
        }

        public HYWDebugAssistant.Core.Services.ViewStateStore ViewState { get; }

        public ModuleRuntime Runtime { get; }

        /// <summary>像 UI 那样**打字**：把文本写进视图状态（模块据此回读；不提交）。</summary>
        public void Type(string instanceId, string source, string text) =>
            ViewState.SetInputText(instanceId, source, text);

        /// <summary>像 UI 那样**切页**：写当前选中页（发送器据此决定发哪一页）。</summary>
        public void SelectPage(string instanceId, string stateKey, int page) =>
            ViewState.SetSelectedPage(instanceId, stateKey, page);

        public RuntimeServices Services { get; }

        /// <summary>装入四个基础模块包（走真实包加载路径）。</summary>
        public void InstallBusinessPackages()
        {
            foreach (var module in new[] { "TextSender", "HexSender", "DataMonitor", "HexStringConverter" })
            {
                var directory = PackagePath(module);
                Directory.Exists(directory).ShouldBeTrue($"模块未构建：{directory}（请先 build）");
                Services.InstallModulePackage(directory).Succeeded.ShouldBeTrue($"{module} 包应能加载");
            }
        }

        /// <summary>注册一个**测试桩模块**（不经过包加载；用于判据项目内的本地载体）。</summary>
        /// <param name="name">模块名（构成 `test/<name>`）。</param>
        /// <param name="entryType">入口类型。</param>
        public void Register(string name, Type entryType)
        {
            var definition = new Hosting.Registry.ModuleDefinition(
                Sdk.ModuleId.Create("test", name),
                entryType.Name,
                string.Empty,
                [],
                Sdk.ModuleCapability.None,
                [], true,
                entryType,
                TestViewPackages.For(entryType),
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }

        /// <summary>
        /// 等待某文本数据源的最新样本包含期望片段。
        /// </summary>
        /// <remarks>
        /// **先探样本长度再读**：`SampleSizeInBytes` 在**首个样本写入之前是 0**（"先写入者定槽"），
        /// 而 <see cref="LatestText"/> 遇到 0 会直接断言失败 ⇒ 轮询里必须先判长度，否则这条等待会
        /// 在链路还没出数时**抛异常**而不是继续等（实测踩过）。
        /// </remarks>
        public bool WaitUntilText(string instanceId, string dataSourceName, string expected) =>
            WaitUntil(
                () =>
                {
                    var source = Source(instanceId, dataSourceName);
                    return source.SampleSizeInBytes > 0
                        && LatestText(instanceId, dataSourceName).Contains(expected, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(5));

        /// <summary>按字段写回实例配置（与 UI 的 Schema 表单同一条命令）。</summary>
        public Core.Api.CommandResult SetConfigurationField(string instanceId, string fieldName, string value) =>
            Services.UpdateConfigurationField(instanceId, fieldName, value);

        /// <summary>写入一个输入值（与 UI 输入框回车提交同一条命令）。</summary>
        public Core.Api.CommandResult Submit(string instanceId, string source, string text) =>
            Services.SubmitInput(instanceId, source, text);

        /// <summary>读某数据源的最新样本（按声明的原语解码）。</summary>
        public double Latest(string instanceId, string dataSourceName)
        {
            var source = Source(instanceId, dataSourceName);
            source.SampleSizeInBytes.ShouldBeGreaterThan(0, $"{instanceId}.{dataSourceName} 尚未写入过样本");
            return ReadLatest(source);
        }

        /// <summary>读某**文本类**数据源的最新样本（定长样本去掉尾部填充）。</summary>
        public string LatestText(string instanceId, string dataSourceName)
        {
            var source = Source(instanceId, dataSourceName);
            source.SampleSizeInBytes.ShouldBeGreaterThan(0, $"{instanceId}.{dataSourceName} 尚未写入过样本");

            var buffer = new byte[source.SampleSizeInBytes];

            if (!Services.TryReadLatest(instanceId, dataSourceName, buffer, out var length) || length <= 0)
            {
                return string.Empty;
            }

            return System.Text.Encoding.UTF8.GetString(buffer, 0, length).TrimEnd('\0', ' ');
        }

        /// <summary>某数据源已写入的样本数。</summary>
        public long Written(string instanceId, string dataSourceName) => Source(instanceId, dataSourceName).Written;

        /// <summary>某数据源的定长样本字节数。</summary>
        public int SubscriptionSampleSize(string instanceId, string dataSourceName) =>
            Source(instanceId, dataSourceName).SampleSizeInBytes;

        /// <summary>等待某数据源累计写入达到下限。</summary>
        public bool WaitForCount(string instanceId, string dataSourceName, long atLeast) =>
            WaitUntil(() => Written(instanceId, dataSourceName) >= atLeast, TimeSpan.FromSeconds(5));

        /// <summary>
        /// **显示区当前保留的文本条**（显示区由 `IViewDisplayAccess.AppendText/AppendLine` 追加）。
        /// </summary>
        /// <remarks>
        /// 读法与**模块保存**完全相同（`IViewStateReader.GetDisplayLines`）：框架持有的显示缓冲 + 还没被 UI
        /// 取走的追加，一起给快照 ⇒ 无头会话（没有 UI）里也读得到"屏幕上会显示什么"。
        /// 老的"发布定长样本"路径仍在（老模块零改动），但它**不再是显示区的来源**。
        /// </remarks>
        public IReadOnlyList<string> DisplayLines(string instanceId, string dataSourceName) =>
            ViewState.ReaderFor(instanceId).GetDisplayLines(dataSourceName);

        /// <summary>显示区的文本（条与条之间是换行 —— 屏幕上就是这个样子）。</summary>
        public string DisplayText(string instanceId, string dataSourceName) =>
            string.Join("\n", DisplayLines(instanceId, dataSourceName));

        /// <summary>等待显示区累计"条"数达到下限。</summary>
        public bool WaitForDisplayCount(string instanceId, string dataSourceName, int atLeast) =>
            WaitUntil(() => DisplayLines(instanceId, dataSourceName).Count >= atLeast, TimeSpan.FromSeconds(5));

        /// <summary>等待显示区的**任何一条**包含期望片段。</summary>
        public bool WaitUntilDisplayText(string instanceId, string dataSourceName, string expected) =>
            WaitUntil(
                () => DisplayLines(instanceId, dataSourceName)
                    .Any(line => line.Contains(expected, StringComparison.Ordinal)),
                TimeSpan.FromSeconds(5));

        /// <summary>等待某数据源的最新样本等于期望值（浮点容差内）。</summary>
        public bool WaitUntilLatest(string instanceId, string dataSourceName, double expected) =>
            WaitUntil(
                () =>
                {
                    var source = Source(instanceId, dataSourceName);
                    return source.SampleSizeInBytes > 0
                        && Math.Abs(ReadLatest(source) - expected) < 0.0001;
                },
                TimeSpan.FromSeconds(5));

        /// <summary>
        /// 读某数据源的最新样本：**优先走模块注册的解码器**，未提供时按原语走参考实现。
        /// </summary>
        /// <remarks>这样 T2 顺带覆盖"框架交出解码器句柄 → 呈现层回调"这条链路，而不只是原语回退路径。</remarks>
        private double ReadLatest(Core.Api.SubscriptionSourceView source)
        {
            var buffer = new byte[source.SampleSizeInBytes];
            if (!Services.TryReadLatest(source.InstanceId, source.DataSourceName, buffer, out var length)
                || length != source.SampleSizeInBytes)
            {
                return double.NaN;
            }

            if (source.Decoder is { } decoder)
            {
                return decoder(buffer, out var decoded) ? decoded : double.NaN;
            }

            return source.ValueType switch
            {
                ContractPrimitive.F32 => BitConverter.ToSingle(buffer, 0),
                ContractPrimitive.F64 => BitConverter.ToDouble(buffer, 0),
                ContractPrimitive.U64 => BitConverter.ToUInt64(buffer, 0),
                _ => double.NaN,
            };
        }

        public void Dispose() => Runtime.Dispose();

        private Core.Api.SubscriptionSourceView Source(string instanceId, string dataSourceName) =>
            Services.GetSubscriptionSources().Single(s =>
                string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal)
                && string.Equals(s.DataSourceName, dataSourceName, StringComparison.Ordinal));

        private static string PackagePath(string module)
        {
            var root = FindRepoRoot();
            return Path.Combine(root, "src", "Modules", "Business", module, "bin", BuildConfiguration(), "net10.0");
        }

        private static string BuildConfiguration() =>
#if DEBUG
            "Debug";
#else
            "Release";
#endif

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("未能定位解决方案根目录（未找到 *.slnx/*.sln）");
        }

        /// <summary>等待条件成立（测试体可用来断言 Issue 之类的异步效果）。</summary>
        public static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return condition();
        }
    }
}
