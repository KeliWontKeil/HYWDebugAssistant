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
/// **四个基础模块的数据/格式语义与错误回报**（T1 层）。
/// </summary>
/// <remarks>
/// 用 <see cref="TestModuleContext"/> 驱动：不拉线程、不起框架 —— 直接调模块的**公开单步入口**
/// （`Send` / `SendCurrentPage` / `HandlePacket` / `Convert`），断言"发出去的字节"、
/// "写进显示面的文本"、"回报的失败"。
/// <para>
/// 只保留**看不见的机制不变量**：编码与长度、拆包边界、`len` 切片、暂停与清空的落点、转换的严格性。
/// 呈现与观感（标红、提示色、切页复查、锁格、开关外观、保存对话框）由人眼验收，不放进常驻用例；
/// 连续发送的节拍由 `Tests.Reference` 的链路用例覆盖。
/// </para>
/// </remarks>
public class BusinessModulesBehaviorTests
{
    // ---------------------------------------------------------------- 文本发送器

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
        context.Sent.ShouldBeEmpty("空 = 不发（不报错、不提示）");
        context.SetTexts.ShouldBeEmpty();
        context.AppearanceReports.ShouldBeEmpty();
    }

    [Fact]
    public void 文本发送器_间隔解析应覆盖范围边界_越界要回报失败()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        // 0 是合法输入值，但不是连续发送周期；只输入不拨开关时不应报错。
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, "0");
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(0);
        Alerted(context, TextSenderModule.IntervalSource).ShouldBeFalse();

        // 上限：**接受范围 = 存储范围 = 文案里的范围**（同一个常量）
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, TextSenderModule.MaxIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(TextSenderModule.MaxIntervalMs);

        // 越界 ⇒ **报一条消息 + 标色**，**不改动当前值**
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, (TextSenderModule.MaxIntervalMs + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(TextSenderModule.MaxIntervalMs, "越界不改动当前值");
        Alerted(context, TextSenderModule.IntervalSource).ShouldBeTrue();
        Message(context).ShouldNotBeNullOrEmpty();

        // 合法值 ⇒ 消息清掉、标色清掉
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, "42");
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(42);
        Alerted(context, TextSenderModule.IntervalSource).ShouldBeFalse();
        Message(context).ShouldBeNullOrEmpty();
    }

    [Fact]
    public void 文本发送器_发送应回读当前页而不是别的页()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        // 三页各有内容；**当前选中第 2 页**（0 基 = 1）
        var pages = new[] { "第一页", "第二页", "第三页" };
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), pages[0]);
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(1), pages[1]);
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(2), pages[2]);
        context.ViewState.SetSelectedPage(TextSenderModule.PagesStateKey, 1);

        module.SendCurrentPage(context, out _).ShouldBeTrue();

        var payload = context.Sent.ShouldHaveSingleItem().Payload;
        var len = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload);
        Encoding.UTF8.GetString(payload.AsSpan(2, len)).ShouldBe(pages[1], "**所见即所发**：发的是当前页那一份");
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

        var subject = HexSenderModule.HexSourceOf(0);

        // 奇数个半字节 ⇒ 失败（一个字节两位 ⇒ 奇数写法不合法）
        context.ViewState.SetInputText(subject, "0D 0");
        module.PumpOnce(context);
        context.Sent.ShouldBeEmpty();
        Message(context).ShouldNotBeNullOrEmpty("转换失败必须给出原因（内容由文案表决定）");
        Alerted(context, subject).ShouldBeTrue("哪一格不对由模块标出来");

        // `0x` 前缀不支持（'x' 不是十六进制字符）
        context.ViewState.SetInputText(subject, "0x41");
        module.PumpOnce(context);
        context.Sent.ShouldBeEmpty();
        Message(context).ShouldNotBeNullOrEmpty("转换失败必须给出原因（内容由文案表决定）");
        Alerted(context, subject).ShouldBeTrue();

        // 修好 ⇒ 红与消息都清掉
        context.ViewState.SetInputText(subject, "0x41".Replace("0x", string.Empty, StringComparison.Ordinal));
        module.PumpOnce(context);
        Message(context).ShouldBeNullOrEmpty();
        Alerted(context, subject).ShouldBeFalse();
    }

    [Fact]
    public void 十六进制发送器_空输入应静默不发且不报错()
    {
        var context = new TestModuleContext();
        var module = new HexSenderModule();
        module.OnRegistered(context);

        module.Send(context, "   \n  ", HexSenderModule.HexSourceOf(0), out _).ShouldBeFalse();

        context.Sent.ShouldBeEmpty();
        context.SetTexts.ShouldBeEmpty();
        context.AppearanceReports.ShouldBeEmpty();
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

    /// <summary>**保存出来的 .txt 里既没有 `↵` 标记、也没有任何补齐**。</summary>
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

        // 混着来：数据里真有的换行字符 + 通道换行 + 一个短包（定宽补齐会把它补到 96 字节）
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
            var expected = "甲\n乙\n丙\n";
            text.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(
                expected,
                "所见即所存：数据里真实的换行在文件里仍是真实换行；"
                + "通道换行成为真正的换行；「数据包换行」开着 ⇒ 每包各自一行、不再补分隔空格；"
                + "**没有任何尾部补齐**（末尾那一个换行是 `File.WriteAllLines` 的收尾，不是数据内容）");
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

        var shown = "显示我";
        module.HandlePacket(context, DataMonitorModule.BuildPayload(Encoding.UTF8.GetBytes(shown))).ShouldBe(1);
        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource)
            .ShouldHaveSingleItem()
            .ShouldBe(shown, "数据到达即落地 —— 文本方向不加任何分隔字符");

        // 「数据包换行」开 ⇒ 每个包各自一条（换行由通道表达）
        context.EnqueueInput(DataMonitorModule.LineBreakSource, "true");
        module.PumpOnce(context);
        var next = "再来";
        module.HandlePacket(context, DataMonitorModule.BuildPayload(Encoding.UTF8.GetBytes(next))).ShouldBe(1);
        context.ViewState.GetDisplayLines(DataMonitorModule.LogSource).ShouldBe([shown, next]);
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
            var lines = new[] { "第一行", "第二行" };
            context.ViewState.SetDisplayLines(DataMonitorModule.LogSource, lines);
            context.EnqueueInput(DataMonitorModule.SavePathSource, path);
            module.PumpOnce(context);

            module.Save(context).ShouldBeTrue();
            File.ReadAllLines(path).ShouldBe(lines);
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

    /// <summary>
    /// **空白是分隔符，不是装饰**（最严判据）：每一段必须**恰好两位**。
    /// </summary>
    /// <remarks>
    /// 丢掉空白再按两位切，`123456 23 55` 会被解成 5 个字节，
    /// 于是"某一段少写了一个空格"在界面上表现为**一串莫名其妙的字符**，而不是一条报错。
    /// </remarks>
    [Fact]
    public void 转换器_十六进制必须两位一段_不许把空格当装饰()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);
        var source = HexStringConverterModule.HexSourceOf(0);

        // 两段一位、四段两位、两段三位 ⇒ 全部判错（这正是"最严"的判据）
        module.Convert(context, "123456 23 55", toHex: false, source).ShouldBeFalse();
        module.LastFailure.ShouldNotBeNull().ShouldContain("123456");

        module.Convert(context, "0D0A", toHex: false, source).ShouldBeFalse();
        module.LastFailure.ShouldNotBeNull().ShouldContain("0D0A");

        module.Convert(context, "41 4", toHex: false, source).ShouldBeFalse();
        module.LastFailure.ShouldNotBeNull().ShouldContain("4");

        // 严格两位一段 ⇒ 通过（`41 42 43` = ABC）
        module.Convert(context, "41 42 43", toHex: false, source).ShouldBeTrue();
        module.LastCharResult.ShouldBe("ABC");

        // 换行 / 制表同算空白分隔
        module.Convert(context, "41\t42\n43", toHex: false, source).ShouldBeTrue();
        module.LastCharResult.ShouldBe("ABC");
    }

    [Fact]
    public void 转换器_字符转HEX应两位大写空格分隔()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        module.Convert(context, "AB", toHex: true, HexStringConverterModule.CharSourceOf(0)).ShouldBeTrue();
        module.LastHexResult.ShouldBe("41 42");

        // 中文 ⇒ UTF-8 多字节
        module.Convert(context, "中", toHex: true, HexStringConverterModule.CharSourceOf(0)).ShouldBeTrue();
        module.LastHexResult.ShouldBe("E4 B8 AD");

        // **结果要真的出现在结果框读的那条源上**（不是内部缓冲里的样本）：
        // 结果框是只读文本面，它读的是**显示缓冲** —— 写别处等于界面上什么都没发生。
        ScreenOf(context, HexStringConverterModule.HexResultSource).ShouldBe("E4 B8 AD");
    }

    [Fact]
    public void 转换器_HEX转字符应忽略空白_非法序列应只标红不写字()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        module.Convert(context, "41 42\n43", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeTrue();
        module.LastCharResult.ShouldBe("ABC");
        ScreenOf(context, HexStringConverterModule.CharResultSource).ShouldBe("ABC");

        // 奇数位 ⇒ 失败
        module.Convert(context, "41 4", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeFalse();
        module.LastFailure.ShouldNotBeNull();

        // 非法 UTF-8（FF 不是合法起始字节）⇒ **明确失败**（不静默替换成 �）
        module.Convert(context, "FF", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeFalse();
        module.LastFailure.ShouldNotBeNull();

        // **失败只标红、不写任何文字**：这一格的错用边框说清就够。
        Alerted(context, HexStringConverterModule.HexSourceOf(0)).ShouldBeTrue();
        Message(context).ShouldBeNullOrEmpty("失败只标红，不写字");

        // **结果区被清掉**：留着旧结果会让屏幕上同时有"新输入 + 旧结果"，
        // 而旧结果看起来像是这次算出来的 —— 那是最容易骗到自己的一种状态。
        ScreenOf(context, HexStringConverterModule.CharResultSource).ShouldBeEmpty();
    }

    /// <summary>**界面上现在显示的那句话**（模块写的最后一条消息）。</summary>
    /// <param name="context">测试上下文。</param>
    /// <returns>消息文本；没写过 ⇒ <see langword="null"/>。</returns>
    /// <remarks>
    /// 判据取"最后一条 `SetText`"：消息是**替换**语义（只有当前值），所以最后一条就是屏幕上的那一句。
    /// </remarks>
    private static string? Message(TestModuleContext context) =>
        context.SetTexts.Count == 0 ? null : context.SetTexts[^1].Text;

    /// <summary>某个显示源**现在显示的内容**（只读文本面读的就是这一份）。</summary>
    /// <param name="context">测试上下文。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <returns>内容；没内容 ⇒ 空串。</returns>
    private static string ScreenOf(TestModuleContext context, string sourceName) =>
        string.Join(Environment.NewLine, context.ViewState.GetDisplayLines(sourceName));

    /// <summary>
    /// **这一格现在被标出来了没有**（模块说的"这里不对"）。
    /// </summary>
    /// <param name="context">测试上下文。</param>
    /// <param name="sourceName">数据源名。</param>
    /// <returns>当前被标色为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 判据是**角色词**（`danger`）而不是"某个色值非空"：模块说的是"这一格要配色表里的失败色"，
    /// 具体是哪个红归用户设置管（这样改主题色 / 改 `SemanticError` 时模块侧零改动）。
    /// **颜色是替换语义**：模块清色时也会发一条（<c>Outline = null</c>），"设过色"与"现在是这个色"
    /// 是两件事，后者才是界面上的事实；**动作复原**（<c>Reset</c>）不是颜色报告，要跳过。
    /// </remarks>
    private static bool Alerted(TestModuleContext context, string sourceName)
    {
        var last = context.AppearanceReports
            .LastOrDefault(r => !r.Reset && string.Equals(r.SourceName, sourceName, StringComparison.Ordinal));

        return string.Equals(last.OutlineRole, HYWDebugAssistant.Sdk.Views.ViewInkRoles.Danger, StringComparison.Ordinal);
    }
}
