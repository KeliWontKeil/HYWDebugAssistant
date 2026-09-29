using System.Text;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.HexStringConverter;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **四个基础模块的业务行为测试**（阶段 4 · T1 层）。
/// </summary>
/// <remarks>
/// 用 <see cref="TestModuleContext"/> 驱动：不拉线程、不起框架 —— 直接调模块的**公开单步入口**
/// （`Send` / `SendCurrentPage` / `PumpOnce` / `HandlePacket` / `Convert`），断言"发出去的字节"、
/// "显示出来的行"、"回报的失败"。
/// </remarks>
public class BusinessModulesBehaviorTests
{
    // ---------------------------------------------------------------- 文本发送器

    /// <summary>
    /// **连续发送的节拍 = 配置的间隔**（2026-09 用户实测两个真缺陷的回归网）。
    /// </summary>
    /// <remarks>
    /// 旧实现的调度是"**每轮循环都 `nextTick += 间隔`**"（活动循环每 20 ms 一轮）⇒
    /// 间隔大于 20 ms 时 `nextTick` 每轮往前跳、**永远追不上** ⇒ **一条都不发**（"配置了值后静默不发送"）；
    /// 间隔 = 0（未配置时的默认）⇒ 每轮都到期 ⇒ **每 20 ms 发一条**（"未知频率"）。
    /// 现在：只在真正发出去之后才推进 `nextDue`；**没设置间隔则显式报错、绝不硬发**（用户 2026-09 裁定）。
    /// </remarks>
    [Fact]
    public void 连续发送_应按配置的间隔发送_未配置间隔时应报错且不发送()
    {
        // ① 配置 100 ms：跑 ~600 ms ⇒ 约 6 条（旧实现：0 条）
        var configured = new TextSenderModule();
        var context = new TestModuleContext();
        configured.OnRegistered(context);
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "tick");
        context.EnqueueInput(TextSenderModule.ContinuousSource, "true");
        context.EnqueueInput(TextSenderModule.IntervalSource, "100");

        RunActivityFor(configured, context, TimeSpan.FromMilliseconds(600));

        var sent = OutPortSends(context);
        sent.ShouldBeInRange(
            3,
            12,
            $"间隔 100 ms、跑 600 ms ⇒ 约 6 条（旧实现每轮都推进 `nextTick` ⇒ 0 条；实测 {sent} 条）");

        // ② **未配置间隔 ⇒ 显式报错且一条都不发**（"留空应该显式报错而不是硬发"）
        var byDefault = new TextSenderModule();
        var defaultContext = new TestModuleContext();
        byDefault.OnRegistered(defaultContext);
        defaultContext.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "tick");
        defaultContext.EnqueueInput(TextSenderModule.ContinuousSource, "true");

        RunActivityFor(byDefault, defaultContext, TimeSpan.FromMilliseconds(300));

        OutPortSends(defaultContext).ShouldBe(
            0, "**没设置间隔就绝不能硬发**（旧实现：每 20 ms 一条 ⇒ 约 15 条）");

        var intervalMissingReported = defaultContext.InputFeedbackReports.Any(r =>
            !r.Succeeded && string.Equals(r.SourceName, TextSenderModule.ContinuousSource, StringComparison.Ordinal));

        intervalMissingReported.ShouldBeTrue("打开「连续发送」时缺间隔 ⇒ 必须**当场显式报错**（而不是静默不发/硬发）");

        // ③ 间隔填**空** ⇒ 该输入框本身也要显式报错（另起上下文：上面那个已经 RequestStop 了）
        var blankInterval = new TextSenderModule();
        var blankContext = new TestModuleContext();
        blankInterval.OnRegistered(blankContext);
        blankContext.EnqueueInput(TextSenderModule.IntervalSource, "   ");

        RunActivityFor(blankInterval, blankContext, TimeSpan.FromMilliseconds(80));

        blankContext.InputFeedbackReports.Any(r =>
            !r.Succeeded && string.Equals(r.SourceName, TextSenderModule.IntervalSource, StringComparison.Ordinal))
            .ShouldBeTrue("间隔留空 ⇒ 该输入框必须报错（不静默）");
    }

    /// <summary>
    /// **文本里不再有任何标记**〔2026-09 用户裁定〕："文本框里看到的文本是什么样，保存就是什么样，并去除标记"
    /// —— `↵` 只由**显示层**画，模块产出的文本里一个都不能有；数据里真有的换行字符**原样保留**。
    /// </summary>
    /// <remarks>
    /// 这条钉住的是"标记从哪里来"：老实现把 `↵` **拼进文本**（`MonitorLineFormatter.MarkNewlines`），
    /// 于是保存出来的 `.txt` 里也带着 `↵`。现在标记是显示层的装饰 ⇒ 文本层必须**一个标记都没有**。
    /// </remarks>
    [Fact]
    public void 数据显示器_产出的文本里不得含任何标记_数据里的换行字符原样保留()
    {
        var now = DateTimeOffset.Now;

        var lines = DataMonitorModule.FormatLines("第一行\n第二行"u8, hex: false, timestamp: false, perPacket: true, now);

        lines.ShouldHaveSingleItem().ShouldBe("第一行\n第二行", "数据里的换行符原样保留（它本来就是数据），但**不得附加任何标记**");

        foreach (var line in DataMonitorModule.FormatLines("甲\n\n乙"u8, hex: false, timestamp: false, perPacket: true, now))
        {
            line.ShouldNotContain("↵", Case.Sensitive, "`↵` 是**显示层**的装饰，绝不能进文本（否则保存出来的 .txt 也带着它）");
        }

        DataMonitorModule.FormatLines("没有换行符"u8, hex: false, timestamp: false, perPacket: true, now)
            .ShouldHaveSingleItem()
            .ShouldNotContain("↵", Case.Sensitive);
    }

    /// <summary>
    /// **「数据包换行」/「时间戳」都关时：追加数据直接接在后面，不做任何标记**（用户裁定原文）。
    /// </summary>
    /// <remarks>
    /// 这是"一行必须 96 字节"被删除之后的核心行为：**没有定宽、没有补齐、没有通道换行** ⇒
    /// 收到的两段数据在显示区里就是**同一条**（长度由数据自己决定），而不是"各占一条 96 字节的行"。
    /// </remarks>
    [Fact]
    public void 数据显示器_不开换行与时间戳时_追加应直接接在后面且不带通道换行()
    {
        DataMonitorModule.BreaksAfterPacket(timestamp: false, perPacket: false)
            .ShouldBeFalse("两个开关都关 ⇒ 换行不由通道补（数据到达即接在后面）");

        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        module.HandlePacket(context, DataMonitorModule.BuildPayload("AB"u8)).ShouldBe(1);
        module.HandlePacket(context, DataMonitorModule.BuildPayload("CD"u8)).ShouldBe(1);

        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource)
            .ShouldHaveSingleItem()
            .ShouldBe("ABCD", "**同一条**：直接接在后面（旧实现会切成两条 96 字节的定宽行）");

        // 格式开关都关时，连"要不要换行"这件事都不该由文本表达
        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource)[0]
            .ShouldNotContain("↵", Case.Sensitive);
    }

    /// <summary>跑一段**真实活动体**（独立线程；到点请求停止并等它返回）。</summary>
    private static void RunActivityFor(TextSenderModule module, TestModuleContext context, TimeSpan duration)
    {
        var runner = new Thread(() => module.RunActivity(context));
        runner.Start();
        Thread.Sleep(duration);
        context.RequestStop();
        runner.Join(TimeSpan.FromSeconds(2)).ShouldBeTrue("活动体应能及时停止");
    }

    /// <summary>该上下文里从输出口发出的单元数。</summary>
    private static int OutPortSends(TestModuleContext context) =>
        context.Sent.Count(p => string.Equals(p.PortName, TextSenderModule.OutPort, StringComparison.Ordinal));

    [Fact]
    public void 文本发送器_应按UTF8编码并补零_且len是字节数()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        // "AB" ⇒ 2 字节；`data` 尾部补零（1472 字节的定长区）
        module.Send(context, "AB", out var packets).ShouldBeTrue();

        packets.ShouldBe(1);
        var payload = context.Sent.ShouldHaveSingleItem().Payload;
        payload.Length.ShouldBe(2 + TextSenderModule.TextCapacity);

        var len = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload);
        len.ShouldBe(2, "**`len` = 字节数**（不是字符数）");
        payload[2].ShouldBe((byte)'A');
        payload[3].ShouldBe((byte)'B');
        payload[4].ShouldBe((byte)0, "尾部补零只是定长填充");
        payload[^1].ShouldBe((byte)0);
    }

    [Fact]
    public void 文本发送器_中文应按UTF8占3字节_且拆包只切在字符边界()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        // 500 个"中"= 1500 字节 > 1472 ⇒ 必须拆成 2 包，且**不能把某个字的 3 字节切开**
        var text = new string('中', 500);
        module.Send(context, text, out var packets).ShouldBeTrue();

        packets.ShouldBe(2);

        var total = 0;

        foreach (var sent in context.Sent)
        {
            var payload = sent.Payload;
            var len = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload);
            len.ShouldBeLessThanOrEqualTo(TextSenderModule.TextCapacity);
            (len % 3).ShouldBe(0, "每个 chunk 的字节数必须是 3 的整数倍（中文 3 字节 ⇒ 没有切碎字符）");

            var bytes = payload.AsSpan(2, len);
            var roundTrip = Encoding.UTF8.GetString(bytes);
            roundTrip.Length.ShouldBe(len / 3);
            total += len;
        }

        total.ShouldBe(1500, "两包合起来正好是原文的字节数（一个字节都不丢）");
    }

    [Fact]
    public void 文本发送器_空文本应静默不发()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        module.Send(context, string.Empty, out var packets).ShouldBeFalse();

        packets.ShouldBe(0);
        context.Sent.ShouldBeEmpty("空 = 不发（不报错、不提示 —— 用户裁定 T11）");
        context.InputFeedbackReports.ShouldBeEmpty();
    }

    [Fact]
    public void 文本发送器_间隔解析应覆盖范围边界_越界要回报失败()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        // 0 = 每个活动周期都投递（用户裁定"不预设用户需求"）；u32 最大值 = 上限
        context.EnqueueInput(TextSenderModule.IntervalSource, "0");
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(0);

        context.EnqueueInput(TextSenderModule.IntervalSource, uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(int.MaxValue, "上限按 int 表示（≈49.7 天，每周期只投一次 ⇒ 不占资源）");

        // 越界 ⇒ **回报失败**（⇒ 该输入框红框），**不改动当前值**
        context.EnqueueInput(TextSenderModule.IntervalSource, "4294967296");
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(int.MaxValue);
        context.InputFeedbackReports.ShouldContain(r => !r.Succeeded && r.SourceName == TextSenderModule.IntervalSource);

        // 合法值 ⇒ 成功回报（红框消失）
        context.EnqueueInput(TextSenderModule.IntervalSource, "42");
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(42);
        context.InputFeedbackReports.ShouldContain(r => r.Succeeded && r.SourceName == TextSenderModule.IntervalSource);
    }

    [Fact]
    public void 文本发送器_发送应回读当前页而不是别的页()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        // 三页各有内容；**当前选中第 2 页**（0 基 = 1）
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "第一页");
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(1), "第二页");
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(2), "第三页");
        context.ViewState.SetSelectedPage(TextSenderModule.PagesStateKey, 1);

        module.SendCurrentPage(context, out _).ShouldBeTrue();

        var payload = context.Sent.ShouldHaveSingleItem().Payload;
        var len = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload);
        Encoding.UTF8.GetString(payload.AsSpan(2, len)).ShouldBe("第二页", "**所见即所发**：发的是当前页那一份");
    }

    [Fact]
    public void 文本发送器_回车提交也应等价于发送一次()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        context.EnqueueInput(TextSenderModule.TextSourceOf(0), "回车提交的内容");
        module.PumpOnce(context).ShouldBeFalse();

        context.Sent.Count.ShouldBe(1, "回车（输入通道）与点按钮（命令通道）都发一次");
    }

    // ---------------------------------------------------------------- HEX 发送器

    [Fact]
    public void 十六进制发送器_应解析字节_忽略空白且大小写都收()
    {
        var context = new TestModuleContext();
        var module = new HexSenderModule();
        module.OnRegistered(context);

        module.Send(context, "48 65 6c 6C\n6F", HexSenderModule.HexSourceOf(0), out var packets).ShouldBeTrue();

        packets.ShouldBe(1);
        var payload = context.Sent.ShouldHaveSingleItem().Payload;
        ((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload)).ShouldBe(5);

        var bytes = payload.AsSpan(2, 5).ToArray();
        Encoding.ASCII.GetString(bytes).ShouldBe("Hello", "空白只是排版、大小写都收");
    }

    [Fact]
    public void 十六进制发送器_奇数位或非法字符应失败并回报()
    {
        var context = new TestModuleContext();
        var module = new HexSenderModule();
        module.OnRegistered(context);

        // 奇数个半字节 ⇒ 失败（用户裁定"每字节两位" ⇒ 奇数写法不合法）
        module.Send(context, "0D 0", HexSenderModule.HexSourceOf(0), out _).ShouldBeFalse();
        context.Sent.ShouldBeEmpty();

        // `0x` 前缀不支持（'x' 不是十六进制字符）
        module.Send(context, "0x41", HexSenderModule.HexSourceOf(0), out _).ShouldBeFalse();

        context.InputFeedbackReports.Count.ShouldBe(2, "两次失败都要回报（⇒ 该输入框红框）");
        context.InputFeedbackReports.ShouldAllBe(r => !r.Succeeded);
    }

    [Fact]
    public void 十六进制发送器_空输入应静默不发且不报错()
    {
        var context = new TestModuleContext();
        var module = new HexSenderModule();
        module.OnRegistered(context);

        module.Send(context, "   \n  ", HexSenderModule.HexSourceOf(0), out _).ShouldBeFalse();

        context.Sent.ShouldBeEmpty();
        context.InputFeedbackReports.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- 数据显示器

    [Fact]
    public void 数据显示器_应按len切片且不因0x00截断()
    {
        // 载荷：len = 5，data = 41 00 42 00 43 …（中间夹 0x00 —— 它们是**合法数据**）
        var payload = new byte[2 + DataMonitorModule.TextCapacity];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload, 5);
        payload[2] = 0x41;
        payload[3] = 0x00;
        payload[4] = 0x42;
        payload[5] = 0x00;
        payload[6] = 0x43;

        DataMonitorModule.TrySlicePayload(payload, out var content).ShouldBeTrue();
        content.Length.ShouldBe(5, "按 `len` 切片 —— **0x00 是数据、不是终止符**");
        content.ToArray().ShouldBe([0x41, 0x00, 0x42, 0x00, 0x43]);
    }

    [Fact]
    public void 数据显示器_收到定长载荷时必须按len切片_不能把填充当数据()
    {
        // 【回归网 · 端到端用例抓出的真缺陷】接收路径原先把**整个定长载荷**（2 + 1472）喂给格式化器
        // ⇒ 1472 字节的填充被当成数据。正确行为：**先按 `len` 切片**，只显示真实内容。
        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        var payload = new byte[2 + DataMonitorModule.TextCapacity];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload, 5);
        "Hello"u8.CopyTo(payload.AsSpan(2));

        // 「数据包换行」开 ⇒ 一个包一条；切片正确时应**正好一条**（而不是把 1472 字节填充也显示出来）
        context.EnqueueInput(DataMonitorModule.LineBreakSource, "true");
        module.PumpOnce(context);

        module.HandlePacket(context, payload).ShouldBe(1, "只显示 `len` 指定的那 5 个字节 ⇒ 一条");

        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource)
            .ShouldHaveSingleItem()
            .ShouldBe("Hello", "尾部填充既不能显示、也不能落盘（切片之后它根本不在文本里）");
    }

    /// <summary>
    /// 渲染形态（文本 / 十六进制 / 时间戳）——**不再有"定宽 96 字节"这一条**。
    /// </summary>
    /// <remarks>
    /// 2026-09 用户裁定："一行必须 96 字节：不应该规定一行多少字节，**一行多少是由文本框的宽度决定的**" ⇒
    /// 断言从"每行 96 字节"改成"文本就是数据本身的样子"（模板产出的文本长度 = 数据决定的长度）。
    /// </remarks>
    [Fact]
    public void 数据显示器_应产出数据本身的形态_不再有定宽与补齐()
    {
        var bytes = new byte[] { 0x41, 0x42, 0x43 };
        var at = new DateTimeOffset(2026, 9, 27, 21, 53, 0, TimeSpan.Zero);

        var textLines = DataMonitorModule.FormatLines(bytes, hex: false, timestamp: false, perPacket: true, at);
        textLines.ShouldHaveSingleItem().ShouldBe("ABC", "**不做任何补齐**：文本就是这三个字符");

        var hexLines = DataMonitorModule.FormatLines(bytes, hex: true, timestamp: false, perPacket: true, at);
        hexLines.ShouldHaveSingleItem().ShouldBe("41 42 43", "每字节两位大写十六进制，末尾不留悬空空格");

        var stamped = DataMonitorModule.FormatLines(bytes, hex: true, timestamp: true, perPacket: true, at);
        stamped.ShouldHaveSingleItem().ShouldStartWith("21:53:00.000  ");

        // **没有定宽这回事**：三段文本的字节数各自等于数据本身的长度（旧实现一律 96）
        Encoding.UTF8.GetByteCount(textLines[0]).ShouldBe(3);
        Encoding.UTF8.GetByteCount(stamped[0]).ShouldBe(22, "时间戳 12 字符 + 两空格（14）+ `41 42 43`（8）");
        textLines.Concat(hexLines).Concat(stamped)
            .ShouldAllBe(line => Encoding.UTF8.GetByteCount(line) != 96, "不再有 96 字节的定宽行（那是被裁定删除的口径）");
    }

    /// <summary>
    /// **通道换行 = 换行由调用表达，文本里没有标记**〔2026-09 用户裁定〕。
    /// </summary>
    /// <remarks>
    /// 「数据包换行」开着时，换行来自 `IViewDisplayAccess.AppendLine`（**通道**），不是文本里的字符 ⇒
    /// 文本里既没有 `↵`、也不会有多余的换行字符；显示层据此**不给它画 `↵`**。
    /// </remarks>
    [Fact]
    public void 数据显示器_开数据包换行时_换行来自通道且文本里没有标记()
    {
        DataMonitorModule.BreaksAfterPacket(timestamp: false, perPacket: true)
            .ShouldBeTrue("「数据包换行」开 ⇒ 换行由**通道调用**表达");

        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        context.EnqueueInput(DataMonitorModule.LineBreakSource, "true");
        module.PumpOnce(context);

        module.HandlePacket(context, DataMonitorModule.BuildPayload("AA"u8)).ShouldBe(1);
        module.HandlePacket(context, DataMonitorModule.BuildPayload("BB"u8)).ShouldBe(1);

        var lines = context.ViewState.GetDisplayLines(DataMonitorModule.LogSource);

        lines.ShouldBe(["AA", "BB"], "两个包 ⇒ 通道换行给出两条；文本分别是各自的原始内容");
        lines.ShouldAllBe(line => !line.Contains('↵'), "通道换行不是字符 ⇒ 文本里一个标记都不能有");
        lines.ShouldAllBe(line => !line.Contains('\n'), "换行也不该被塞进文本（它由通道表达）");
    }

    /// <summary>**保存出来的 .txt 里既没有 `↵` 标记、也没有任何补齐**（用户裁定"并去除标记"）。</summary>
    /// <remarks>
    /// 断言写的是**磁盘上的字节**（不是内存里的字符串）：`↵` 只有这一个来源会漏出去，
    /// 而定宽补齐/`0x00` 填充同样是"所见即所存"的破口 —— 三者一起钉住。
    /// </remarks>
    [Fact]
    public void 数据显示器_保存的文件既没有标记也没有补齐()
    {
        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        // 混着来：数据里真有的换行字符 + 通道换行 + 一个短包（旧实现会给它补空格到 96 字节）
        context.EnqueueInput(DataMonitorModule.LineBreakSource, "true");
        module.PumpOnce(context);

        module.HandlePacket(context, DataMonitorModule.BuildPayload("甲\n乙"u8)).ShouldBe(1);
        module.HandlePacket(context, DataMonitorModule.BuildPayload("丙"u8)).ShouldBe(1);

        var path = Path.Combine(Path.GetTempPath(), $"hyw-monitor-save-{Guid.NewGuid():N}.txt");

        try
        {
            context.EnqueueInput(DataMonitorModule.SavePathSource, path);
            module.PumpOnce(context);

            module.Save(context).ShouldBeTrue();
            module.LastSaveFailure.ShouldBeNull();

            var bytes = File.ReadAllBytes(path);
            var text = Encoding.UTF8.GetString(bytes);

            text.ShouldNotContain("↵", Case.Sensitive, "标记是**显示层**的装饰：保存出来的文件里绝不能有它");
            text.ShouldNotContain('\0', "定长样本的补零/补齐都不该出现在保存结果里");
            text.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(
                "甲\n乙\n丙\n",
                "所见即所存：数据里的换行字符保留，通道换行成为真正的换行，且**没有任何尾部补齐**"
                + "（末尾那一个换行是 `File.WriteAllLines` 的收尾，不是数据内容）");
            text.ShouldNotContain("  \n", Case.Sensitive, "行尾不得有补齐产生的空格");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// **保存改一步**（2026-09 用户裁定）：用户选完位置就**当场写盘**，并把落点/失败**回报给界面**。
    /// </summary>
    /// <remarks>
    /// 用户原话："更好的方案为只提供一个保存按钮，点击后弹出窗口选择位置 → **选择完成自动保存在此处**"。
    /// 本用例**只投路径、不发 `save` 命令** —— 这正是界面上那个按钮做的事；旧实现此时 `SaveCount` 会是 0。
    /// 落点确认必须由**模块**给出（写盘成没成功只有模块知道），界面只负责显示 ⇒ 断言回报内容。
    /// </remarks>
    [Fact]
    public void 数据显示器_选完保存位置应当场写盘并回报落点()
    {
        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        context.ViewState.SetDisplayLines(DataMonitorModule.LogSource, "第一行", "第二行");

        var path = Path.Combine(Path.GetTempPath(), $"hyw-monitor-oneclick-{Guid.NewGuid():N}.txt");

        try
        {
            // 界面上那个「保存」按钮：弹窗 → 用户选完 → 框架把**路径**投进来（没有第二个按钮、没有 save 命令）
            context.EnqueueInput(DataMonitorModule.SavePathSource, path);
            module.PumpOnce(context);

            module.SaveCount.ShouldBe(1, "选完位置就该存了（不再等第二个按钮）");
            File.ReadAllLines(path).ShouldBe(["第一行", "第二行"], "落盘的必须是屏幕上那份（所见即所存）");

            var report = context.InputFeedbackReports.ShouldHaveSingleItem();
            report.SourceName.ShouldBe(DataMonitorModule.SavePathSource);
            report.Succeeded.ShouldBeTrue();
            report.Message.ShouldNotBeNullOrEmpty();
            var message = report.Message!;
            message.ShouldContain("已保存");
            message.ShouldContain(Path.GetFileName(path));   // 落点确认要说清存到哪个文件
            message.ShouldContain("2 行");                   // 并且说清存了多少行（界面那一行要能读）
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // 失败路径：目录不存在 ⇒ **失败也必须回报**（界面据此把按钮与那一行标红，不许静默）
        var bad = Path.Combine(Path.GetTempPath(), $"hyw-无此目录-{Guid.NewGuid():N}", "x.txt");
        context.EnqueueInput(DataMonitorModule.SavePathSource, bad);
        module.PumpOnce(context);

        module.SaveCount.ShouldBe(1, "失败不得计为一次成功保存");

        var failure = context.InputFeedbackReports[^1];
        failure.SourceName.ShouldBe(DataMonitorModule.SavePathSource);
        failure.Succeeded.ShouldBeFalse();
        failure.Message.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void 数据显示器_暂停应收下即弃_恢复后继续显示()
    {
        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        // 勾上「暂停显示」⇒ 包被丢弃（不是"存在内存里"）
        context.EnqueueInput(DataMonitorModule.PausedSource, "true");
        module.PumpOnce(context);
        module.IsPaused.ShouldBeTrue();

        module.HandlePacket(context, DataMonitorModule.BuildPayload("丢弃我"u8)).ShouldBe(0);
        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource).ShouldBeEmpty("暂停期间不应有任何显示");

        // 取消暂停 ⇒ 恢复正常显示。**两个开关都关**时：立刻接在后面（不再有"定宽行没填满就看不见"这回事）
        context.EnqueueInput(DataMonitorModule.PausedSource, "false");
        module.PumpOnce(context);

        module.HandlePacket(context, DataMonitorModule.BuildPayload("显示我"u8)).ShouldBe(1);
        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource)
            .ShouldHaveSingleItem()
            .ShouldBe("显示我", "数据到达即落地 —— 不存在'半行/pending tail'");

        // 「数据包换行」开 ⇒ 每个包各自一条（换行由通道表达）
        context.EnqueueInput(DataMonitorModule.LineBreakSource, "true");
        module.PumpOnce(context);
        module.HandlePacket(context, DataMonitorModule.BuildPayload("再来"u8)).ShouldBe(1);
        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource).ShouldBe(["显示我", "再来"]);
    }

    [Fact]
    public void 数据显示器_清空应请求框架清缓冲_保存应写出显示区文本()
    {
        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        // 清空 = 请求框架（模块不留副本 ⇒ 只能请求）
        context.EnqueueCommand(DataMonitorModule.ClearCommand);
        module.PumpOnce(context);
        context.ClearedDisplays.ShouldBe([DataMonitorModule.LogSource]);

        // 没选路径 ⇒ 明确失败（不静默）
        module.Save(context).ShouldBeFalse();
        module.LastSaveFailure.ShouldNotBeNull();

        // 选了路径 ⇒ 把**显示缓冲的内容**写成 .txt
        var path = Path.Combine(Path.GetTempPath(), $"hyw-monitor-{Guid.NewGuid():N}.txt");

        try
        {
            context.ViewState.SetDisplayLines(DataMonitorModule.LogSource, "第一行", "第二行");
            context.EnqueueInput(DataMonitorModule.SavePathSource, path);
            module.PumpOnce(context);

            module.Save(context).ShouldBeTrue();
            File.ReadAllLines(path).ShouldBe(["第一行", "第二行"]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // ---------------------------------------------------------------- 转换器

    [Fact]
    public void 转换器_字符转HEX应两位大写空格分隔()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        module.Convert(context, "AB", toHex: true, HexStringConverterModule.CharSourceOf(0)).ShouldBeTrue();
        module.LastResult.ShouldBe("41 42");

        // 中文 ⇒ UTF-8 多字节
        module.Convert(context, "中", toHex: true, HexStringConverterModule.CharSourceOf(0)).ShouldBeTrue();
        module.LastResult.ShouldBe("E4 B8 AD");

        context.Published[HexStringConverterModule.HexResultSource].Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 转换器_HEX转字符应忽略空白_非法序列应报错()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        module.Convert(context, "41 42\n43", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeTrue();
        module.LastResult.ShouldBe("ABC");

        // 奇数位 ⇒ 失败
        module.Convert(context, "41 4", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeFalse();
        module.LastFailure.ShouldNotBeNull();

        // 非法 UTF-8（FF 不是合法起始字节）⇒ **明确报错**（不静默替换成 �）
        module.Convert(context, "FF", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeFalse();
        module.LastFailure.ShouldNotBeNull();

        context.InputFeedbackReports.ShouldAllBe(r => r.SourceName == HexStringConverterModule.HexSourceOf(0));
        context.InputFeedbackReports.Count(r => !r.Succeeded).ShouldBe(2);
    }

    [Fact]
    public void 转换器_空输入应静默无动作()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        module.Convert(context, string.Empty, toHex: true, HexStringConverterModule.CharSourceOf(0)).ShouldBeFalse();
        module.Convert(context, "   ", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeFalse();

        context.Published.ShouldBeEmpty("空输入不改结果区");
        context.InputFeedbackReports.ShouldBeEmpty("空输入不报错");
    }

    [Fact]
    public void 转换器_按钮应转换当前选中页()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(HexStringConverterModule.CharSourceOf(0), "AAA");
        context.ViewState.SetInputText(HexStringConverterModule.CharSourceOf(1), "BBB");
        context.ViewState.SetSelectedPage(HexStringConverterModule.CharPagesKey, 1);

        context.EnqueueCommand(HexStringConverterModule.ToHexCommand);
        module.PumpOnce(context);

        module.LastResult.ShouldBe("42 42 42", "「转成 HEX」按钮转换的是**当前选中页**（回读视图状态）");
    }

    [Fact]
    public void 转换器_结果样本也应是定宽的()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        module.Convert(context, "AB", toHex: true, HexStringConverterModule.CharSourceOf(0)).ShouldBeTrue();

        var samples = context.Published[HexStringConverterModule.HexResultSource];
        samples.ShouldNotBeEmpty();
        samples.ShouldAllBe(s => s.Length == HexStringConverterModule.ResultLineBytes,
            "A5 显示源要求**同一数据源的样本长度一致** ⇒ 结果行也是定宽样本");
    }
}
