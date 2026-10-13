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
    /// **连续发送的节拍 = 配置的间隔**（两个真缺陷的回归网）。
    /// </summary>
    /// <remarks>
    /// 活动循环每 20 ms 一轮：调度若按「**每轮循环都 `nextTick += 间隔`**」推进 ⇒
    /// 间隔大于 20 ms 时 `nextTick` 每轮往前跳、**永远追不上** ⇒ **一条都不发**（表现为「配置了值后静默不发送」）；
    /// 间隔 = 0（未配置时的默认）⇒ 每轮都到期 ⇒ **每 20 ms 发一条**（表现为「未知频率」）。
    /// 因此：只在真正发出去之后才推进 `nextDue`；**没设置间隔则显式报错、绝不硬发**。
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
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, "100");

        RunActivityFor(configured, context, TimeSpan.FromMilliseconds(600));

        var sent = OutPortSends(context);
        sent.ShouldBeInRange(
            3,
            12,
            $"间隔 100 ms、跑 600 ms ⇒ 约 6 条（旧实现每轮都推进 `nextTick` ⇒ 0 条；实测 {sent} 条）");

        // ② **间隔设成 0 ⇒ 每拍都发**（"0"与"没设置"是两件事：前者是明确的选择）
        //    这条判据是"配了 0 却一条都不发"那个缺陷的回归网 —— 那时 0 被当成"未设置"的哨兵。
        var zero = new TextSenderModule();
        var zeroContext = new TestModuleContext();
        zero.OnRegistered(zeroContext);
        zeroContext.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "tick");
        zeroContext.ViewState.SetInputText(TextSenderModule.IntervalSource, "0");
        zeroContext.EnqueueInput(TextSenderModule.ContinuousSource, "true");

        RunActivityFor(zero, zeroContext, TimeSpan.FromMilliseconds(200));

        OutPortSends(zeroContext).ShouldBeGreaterThan(
            1, "间隔 0 = 每个活动周期都发（不是\"没设置\"）");

        // ③ **未配置间隔 ⇒ 显式报错且一条都不发**（"留空应该显式报错而不是硬发"）
        var byDefault = new TextSenderModule();
        var defaultContext = new TestModuleContext();
        byDefault.OnRegistered(defaultContext);
        defaultContext.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "tick");
        defaultContext.EnqueueInput(TextSenderModule.ContinuousSource, "true");

        RunActivityFor(byDefault, defaultContext, TimeSpan.FromMilliseconds(300));

        OutPortSends(defaultContext).ShouldBe(
            0, "**没设置间隔就绝不能硬发**（旧实现：每 20 ms 一条 ⇒ 约 15 条）");

        // **红色落在"缺的那一格"上（间隔），不落在勾选框上**：勾选框身上没有"缺什么"这个信息，
        // 把它圈红用户仍不知道要去补哪一格。
        Alerted(defaultContext, TextSenderModule.IntervalSource)
            .ShouldBeTrue("缺间隔 ⇒ 标间隔那一格");
        Alerted(defaultContext, TextSenderModule.ContinuousSource)
            .ShouldBeFalse("勾选框不被圈红（它只被写回原值）");
        ContinuousValue(defaultContext).ShouldBe(
            "false", "开关那一格的值要被写回 false —— 界面照它画，所以这一格就是开关的位置");

        // ③ 间隔填**空**（只有空白）⇒ **不算错**：用户还没填完，不是填错了。
        //    报错发生在"用户尝试动作"那一刻（勾选框被拨动），而不是"这一格是空的"。
        var blankInterval = new TextSenderModule();
        var blankContext = new TestModuleContext();
        blankInterval.OnRegistered(blankContext);
        blankContext.ViewState.SetInputText(TextSenderModule.IntervalSource, "   ");

        RunActivityFor(blankInterval, blankContext, TimeSpan.FromMilliseconds(80));

        Alerted(blankContext, TextSenderModule.IntervalSource)
            .ShouldBeFalse("空 = 还没填，不是填错了（不该在这一格上标红）");

        // ④ 但"空着却去开连续发送"⇒ 那一格与勾选框都要说清楚：**动作不成立**
        var attempt = new TextSenderModule();
        var attemptContext = new TestModuleContext();
        attempt.OnRegistered(attemptContext);
        attemptContext.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "tick");
        attemptContext.EnqueueInput(TextSenderModule.ContinuousSource, "true");

        RunActivityFor(attempt, attemptContext, TimeSpan.FromMilliseconds(120));

        OutPortSends(attemptContext).ShouldBe(0, "缺间隔绝不硬发");
        Alerted(attemptContext, TextSenderModule.IntervalSource).ShouldBeTrue("红色落在缺的那一格（间隔）上");
        Alerted(attemptContext, TextSenderModule.ContinuousSource).ShouldBeFalse("勾选框不被圈红");
        attempt.IsContinuous.ShouldBeFalse("站不住的动作不留痕：开关状态回到关");

        ContinuousValue(attemptContext).ShouldBe(
            "false", "动作不成立 ⇒ 那一格的值被写回 false（界面照它画，就是「它自己退回去了」）");
    }

    /// <summary>
    /// **空内容不是"没事发生"**：点了发送就得说清楚发不出去，并把红色落在那一格上。
    /// </summary>
    /// <remarks>
    /// 不报的话界面上一点痕迹都没有 —— 用户只会以为"按钮坏了"。这是"静默失败"里最难查的一类：
    /// 代码、日志、单元测试**全都没有异常**，只有屏幕上"什么都没发生"。
    /// </remarks>
    [Fact]
    public void 文本发送器_按发送而当前页为空_应当说清楚并标那一格()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        var page = TextSenderModule.TextSourceOf(context.ViewState.GetSelectedPage("text-pages") ?? 0);

        context.EnqueueCommand(TextSenderModule.SendCommand);
        module.PumpOnce(context);

        OutPortSends(context).ShouldBe(0, "空内容绝不发");
        Message(context).ShouldNotBeNullOrEmpty("要说清楚为什么发不出去");
        Alerted(context, page).ShouldBeTrue("红色落在**那一格**上，不是落在按钮上");
    }

    /// <summary>
    /// **红一旦失去依据就必须自己消掉** —— 不能等到"下一次不相关的动作顺带帮忙"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用户报的路径：在第 2 页点发送（空）⇒ 那一格红；之后**去别的页操作**。
    /// 那条路径没有任何调用会回头清第 2 页，红于是成了**永久残留**（回到第 2 页也消不掉）。
    /// </para>
    /// <para>
    /// 判据贴着这一栏的语义：提示说的是"这一格现在是空的"，那它就该在"**它现在不空**"时消失。
    /// 所以这里给定"补上内容"这一刻 —— 不依赖任何别的动作。
    /// （**切页本身不消红**：切页不改内容，那一格确实还是空的，"这格不对"仍然成立。）
    /// </para>
    /// </remarks>
    [Fact]
    public void 文本发送器_那一格补上内容之后红必须自己消掉()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        // 第 2 页（0 基 = 1）为空 ⇒ 点发送 ⇒ 标在**第 2 页那一格**
        context.ViewState.SetSelectedPage(TextSenderModule.PagesStateKey, 1);
        context.EnqueueCommand(TextSenderModule.SendCommand);
        _ = module.PumpOnce(context);

        var blocked = TextSenderModule.TextSourceOf(1);
        Alerted(context, blocked).ShouldBeTrue("空内容发送失败要标在那一格上");

        // 还在同一页、那一格**仍然空着** ⇒ 红**粘住**（不是"提示一闪就没"）：
        // 用户看一眼就知道"刚才那次没发出去，因为这一格"。
        _ = module.PumpOnce(context);
        Alerted(context, blocked).ShouldBeTrue("原因还在（这格确实空着）⇒ 红该留着");

        // **补上内容** ⇒ 原因消失，模块下一拍自己清掉（不需要再点一次发送）
        context.ViewState.SetInputText(blocked, "第 2 页的内容");
        _ = module.PumpOnce(context);

        Alerted(context, blocked).ShouldBeFalse(
            "那一格补上内容之后红必须自己消掉 —— 让它挂着就是「再也消不掉」那条缺陷");
    }

    /// <summary>
    /// **没按过发送、只是切到一页空白页** ⇒ 不该有任何提示与红（那是噪音，不是提示）。
    /// </summary>
    /// <remarks>
    /// 与上一条互补：红与消息**同源于一次"被拦下的动作"**。没有动作就没有它们 ——
    /// 否则"提示"会退化成每拍都挂在空白页上的常驻噪音，用户就不再把它当回事了。
    /// </remarks>
    [Fact]
    public void 文本发送器_只是切到空白页不该冒出提示与红()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.ViewState.SetSelectedPage(TextSenderModule.PagesStateKey, 2);
        _ = module.PumpOnce(context);

        Message(context).ShouldBeNullOrEmpty("没按过发送 ⇒ 不该说任何话");
        Alerted(context, TextSenderModule.TextSourceOf(2)).ShouldBeFalse("没按过发送 ⇒ 不该有红");
    }

    /// <summary>
    /// **切页之后在别的页发送，也不能把上一页的红留成永久残留** —— 走的是活动体那条真路径。
    /// </summary>
    /// <remarks>
    /// 这一条与上一条互补：上一条测"补内容"，这条测"活动体每一拍都在自检"
    /// （模块的 `PumpOnce` 会在每一拍开头复查一次，活动体每 20 ms 就跑它一次）。
    /// </remarks>
    [Fact]
    public void 文本发送器_切页之后活动体每一拍都会复查上一页的红()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.ViewState.SetSelectedPage(TextSenderModule.PagesStateKey, 1);
        context.EnqueueCommand(TextSenderModule.SendCommand);
        _ = module.PumpOnce(context);

        var blocked = TextSenderModule.TextSourceOf(1);
        Alerted(context, blocked).ShouldBeTrue("空内容发送失败要标在那一格上");

        // 切到另一页、把上一页补上内容、再让活动体跑一拍（真实路径就是每拍复查）
        context.ViewState.SetSelectedPage(TextSenderModule.PagesStateKey, 2);
        context.ViewState.SetInputText(blocked, "补上的内容");
        _ = module.PumpOnce(context);

        Alerted(context, blocked).ShouldBeFalse("活动体每一拍都复查 ⇒ 上一页的红不该留成永久残留");
    }

    /// <summary>
    /// **开关只认那一格的值**：间隔为空时，无论按多少次都开不起来（永远不许出现"回弹后还能开"）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"单一事实源"那条纪律的落点：开关位置 == 那一格的值 == 判定结果。
    /// 之前"回弹一次之后还能再按开"的成因是**两份事实**（控件的本地位置 + 模块的意图字段）
    /// 靠一个"复位信号"去凑，而信号会漏、会重复、会与本地状态打架。
    /// </para>
    /// <para>
    /// 判据取"**按 N 次与按 1 次结果相同**"：这条能一次性抓出所有"靠次数/时序凑出来"的实现。
    /// </para>
    /// </remarks>
    [Fact]
    public void 连续发送_间隔为空时按多少次都开不起来()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "有内容");

        for (var i = 1; i <= 5; i++)
        {
            context.EnqueueInput(TextSenderModule.ContinuousSource, "true");
            _ = module.PumpOnce(context);

            module.IsContinuous.ShouldBeFalse($"第 {i} 次按下也不许开（间隔那一格是空的）");
            ContinuousValue(context).ShouldBe("false", $"第 {i} 次按下后，那一格的值必须回到 false");
        }

        Alerted(context, TextSenderModule.IntervalSource).ShouldBeTrue("红落在间隔那一格");
        Alerted(context, TextSenderModule.ContinuousSource).ShouldBeFalse("开关本身从不被圈红");

        // 没在跑 ⇒ 间隔那一格必须仍可编辑（锁上等于把出口也堵了）
        context.AppearanceReports
            .ShouldNotContain(r => r.SourceName == TextSenderModule.IntervalSource && r.ReadOnly == true,
                "没在跑就不该锁那一格");
    }

    /// <summary>
    /// **连续发送真的在跑时，间隔格必须锁上**（改了就恢复：它已经不该再让改）。
    /// </summary>
    [Fact]
    public void 连续发送_跑起来时锁住间隔格_停下来时解锁()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "有内容");
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, "50");
        context.EnqueueInput(TextSenderModule.ContinuousSource, "true");
        _ = module.PumpOnce(context);

        module.IsContinuous.ShouldBeTrue("内容与间隔都齐 ⇒ 站得住");
        ContinuousValue(context).ShouldBe("true", "开得起来 ⇒ 那一格的值就是 true（界面照它画）");
        context.AppearanceReports
            .ShouldContain(r => r.SourceName == TextSenderModule.IntervalSource && r.ReadOnly == true,
                "跑起来要锁住间隔格");

        // 关掉 ⇒ 解锁
        context.EnqueueInput(TextSenderModule.ContinuousSource, "false");
        _ = module.PumpOnce(context);

        module.IsContinuous.ShouldBeFalse();
        context.AppearanceReports
            .ShouldContain(r => r.SourceName == TextSenderModule.IntervalSource && r.ReadOnly == false,
                "停下来要解锁（否则用户改不了那一格）");
    }

    /// <summary>
    /// **间隔由非法改成合法之后，开关立得住**（红清掉、能真的开起来）。
    /// </summary>
    /// <remarks>
    /// 与"间隔为空时开不起来"互补：那条管"没有可用间隔不许开"，这条管"有了就必须能开"。
    /// </remarks>
    [Fact]
    public void 连续发送_间隔补上合法值之后可以开起来()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "tick");

        // 没有间隔就开 ⇒ 开不起来、标在间隔那格
        context.EnqueueInput(TextSenderModule.ContinuousSource, "true");
        _ = module.PumpOnce(context);
        module.IsContinuous.ShouldBeFalse();
        Alerted(context, TextSenderModule.IntervalSource).ShouldBeTrue();

        // 补上合法间隔，**再按一次**（新模型没有"意图记忆"，不自动重开）
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, "100");
        context.EnqueueInput(TextSenderModule.ContinuousSource, "true");
        _ = module.PumpOnce(context);

        module.IsContinuous.ShouldBeTrue("有可用间隔 ⇒ 站得住");
        Alerted(context, TextSenderModule.IntervalSource).ShouldBeFalse("红要清掉（不能停在『曾经不对过』）");
    }

    /// <summary>
    /// **先设间隔、再开连续发送 ⇒ 必须真的发**（用户实际操作顺序的回归网）。
    /// </summary>
    /// <remarks>
    /// 与"打开开关时当场校验"那条互补：那条管"缺间隔要报错"，这条管"**设过之后不能再报缺**"。
    /// 判据取"发条数 &gt; 0"，而不是"没报错" —— 报错文案清掉了却不发，同样是坏的。
    /// </remarks>
    [Fact]
    public void 连续发送_先设间隔再开开关_必须真的发()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);
        context.ViewState.SetInputText(TextSenderModule.TextSourceOf(0), "tick");

        // ① 先设间隔（用户先填那一格）
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, "50");
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(50, "设过就是设过了");

        // ② 再打开连续发送
        context.EnqueueInput(TextSenderModule.ContinuousSource, "true");
        module.PumpOnce(context);

        Alerted(context, TextSenderModule.ContinuousSource)
            .ShouldBeFalse("**间隔已经设过** ⇒ 不许再报\"还没有设置间隔\"");

        // ③ 跑一会儿：必须真的发出去了
        RunActivityFor(module, context, TimeSpan.FromMilliseconds(300));

        OutPortSends(context).ShouldBeGreaterThan(1, "设了 50 ms 就该按它发");
    }

    /// <summary>
    /// **不可显示的字符一律写成 `⟨XX⟩`** —— 与"非法字节"同一套标记，两个方向一致。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 统一标准：屏幕上**没有可见形状**的东西（控制字符、被删字符、格式字符、会跳行的分隔符、
    /// 非法/被截断的字节）都写成它**原始字节**的大写十六进制，包在 `⟨⟩` 里。
    /// </para>
    /// <para>
    /// <b>为什么不能按原样塞进去</b>：控制字符进到文本里，显示层要么不画（看起来像丢数据），
    /// 要么按它自己的语义走 —— `0x0C`（换页）/`0x0A`（换行）会让**显示自己跳行**，
    /// 而那个"换行"并不是数据里的记录边界，用户看到的行结构于是与数据对不上。
    /// </para>
    /// </remarks>
    [Fact]
    public void 数据显示器_不可显示的字符应写成尖括号标记()
    {
        var now = DateTimeOffset.Now;

        // 控制字符：换行 / 回车 / 制表 / 换页 / 退格
        DataMonitorModule.FormatLines("A\nB"u8, hex: false, timestamp: false, perPacket: false, now)
            .ShouldHaveSingleItem()
            .ShouldBe("A⟨0A⟩B", "换行符**不再**按原样塞进文本（它会自己跳行、那是显示行为不是数据边界）");

        DataMonitorModule.FormatLines(new byte[] { 0x41, 0x0C, 0x42 }, hex: false, timestamp: false, perPacket: false, now)
            .ShouldHaveSingleItem()
            .ShouldBe("A⟨0C⟩B", "换页符（截图里那个 `Ce`）也一样标出来");

        DataMonitorModule.FormatLines(new byte[] { 0x41, 0x7F, 0x42 }, hex: false, timestamp: false, perPacket: false, now)
            .ShouldHaveSingleItem()
            .ShouldBe("A⟨7F⟩B", "被删字符 DEL 没有可见形状 ⇒ 同样标出");

        // 可显示的内容（ASCII 可见字符 + 中文 + 空格）**一个字节都不动**
        DataMonitorModule.FormatLines("A B 中文"u8, hex: false, timestamp: false, perPacket: false, now)
            .ShouldHaveSingleItem()
            .ShouldBe("A B 中文", "可见字符（含空格与多字节汉字）原样保留，只有不可显示的才转义");

        // 非 ASCII 的不可显示码位 ⇒ 它的 UTF-8 原始字节（U+0085 = C2 85）
        DataMonitorModule.FormatLines(new byte[] { 0x41, 0xC2, 0x85, 0x42 }, hex: false, timestamp: false, perPacket: false, now)
            .ShouldHaveSingleItem()
            .ShouldBe("A⟨C2⟩⟨85⟩B", "非 ASCII 的不可显示码位按**原始字节**标出（与十六进制方向对得上）");

        // 非法 / 被截断的字节：与上面同一套标记
        DataMonitorModule.FormatLines(new byte[] { 0x41, 0xA1, 0x42 }, hex: false, timestamp: false, perPacket: false, now)
            .ShouldHaveSingleItem()
            .ShouldBe("A⟨A1⟩B", "非法字节沿用同一套标记（不再有第二套写法）");
    }

    /// <summary>
    /// **文本里不得有任何标记**：文本框里显示成什么样，存下来就是什么样
    /// —— `↵` 只由**显示层**画，模块产出的文本里一个都不能有。
    /// </summary>
    /// <remarks>
    /// 这条钉住的是「标记从哪里来」：若把 `↵` **拼进文本**（`MonitorLineFormatter.MarkNewlines`），
    /// 保存出来的 `.txt` 里也会带着 `↵`。标记只是显示层的装饰 ⇒ 文本层必须**一个标记都没有**。
    /// </remarks>
    [Fact]
    public void 数据显示器_产出的文本里不得含任何标记()
    {
        var now = DateTimeOffset.Now;

        foreach (var line in DataMonitorModule.FormatLines("甲\n\n乙"u8, hex: false, timestamp: false, perPacket: true, now))
        {
            line.ShouldNotContain("↵", Case.Sensitive, "`↵` 是**显示层**的装饰，绝不能进文本（否则保存出来的 .txt 也带着它）");
        }

        DataMonitorModule.FormatLines("没有换行符"u8, hex: false, timestamp: false, perPacket: true, now)
            .ShouldHaveSingleItem()
            .ShouldNotContain("↵", Case.Sensitive);
    }

    /// <summary>
    /// **「数据包换行」/「时间戳」都关时：追加数据直接接在后面，不做任何标记**。
    /// </summary>
    /// <remarks>
    /// 核心行为：**没有定宽、没有补齐、没有通道换行** ⇒
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
            .ShouldBe("ABCD", "**同一条**：文本方向直接接在后面，**一个字节都不加**（多一个空格就是无中生有）");

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
        context.SetTexts.ShouldBeEmpty();
        context.AppearanceReports.ShouldBeEmpty();
    }

    [Fact]
    public void 文本发送器_间隔解析应覆盖范围边界_越界要回报失败()
    {
        var context = new TestModuleContext();
        var module = new TextSenderModule();
        module.OnRegistered(context);

        // 0 = 每个活动周期都投递（不预设用户需求）；u32 最大值 = 上限
        context.ViewState.SetInputText(TextSenderModule.IntervalSource, "0");
        module.PumpOnce(context);
        module.IntervalMilliseconds.ShouldBe(0, "0 是一条**明确的选择**（每个活动周期都发），不是\"没设置\"");

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

        var subject = HexSenderModule.HexSourceOf(0);

        // 奇数个半字节 ⇒ 失败（一个字节两位 ⇒ 奇数写法不合法）
        context.ViewState.SetInputText(subject, "0D 0");
        module.PumpOnce(context);
        context.Sent.ShouldBeEmpty();
        Message(context).ShouldStartWith("HEX 转换错误");
        Alerted(context, subject).ShouldBeTrue("哪一格不对由模块标出来");

        // `0x` 前缀不支持（'x' 不是十六进制字符）
        context.ViewState.SetInputText(subject, "0x41");
        module.PumpOnce(context);
        context.Sent.ShouldBeEmpty();
        Message(context).ShouldStartWith("HEX 转换错误");
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

    /// <summary>
    /// **红一旦失去依据就必须自己消掉**（与文本发送器同一条缺陷、同一个判据）。
    /// </summary>
    /// <remarks>
    /// 这一个模块也有"提示 + 把红落在某一格"的一对动作，所以同一形状的缺陷会同时出现在两个模块里。
    /// </remarks>
    [Fact]
    public void 十六进制发送器_那一格补上内容之后红必须自己消掉()
    {
        var module = new HexSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        // 第 2 页（0 基 = 1）为空 ⇒ 点发送 ⇒ 标在**那一格**
        context.ViewState.SetSelectedPage(HexSenderModule.PagesStateKey, 1);
        context.EnqueueCommand(HexSenderModule.SendCommand);
        _ = module.PumpOnce(context);

        var blocked = HexSenderModule.HexSourceOf(1);
        Alerted(context, blocked).ShouldBeTrue("空内容发送失败要标在那一格上");

        // 还没补内容 ⇒ 这格确实不对，红留着（否则这条判据没有区分度）
        _ = module.PumpOnce(context);
        Alerted(context, blocked).ShouldBeTrue("还没补内容 ⇒ 红该留着");

        // 补上内容 ⇒ 依据没了，下一拍自己清掉
        context.ViewState.SetInputText(blocked, "41 42");
        _ = module.PumpOnce(context);

        Alerted(context, blocked).ShouldBeFalse(
            "那一格补上内容之后红必须自己消掉 —— 让它挂着就是「再也消不掉」那条缺陷");
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
        // 回归网：接收路径若把**整个定长载荷**（2 + 1472）喂给格式化器 ⇒ 1472 字节的填充被当成数据。
        // 正确行为：**先按 `len` 切片**，只显示真实内容。
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
    /// 一行多少字节**由文本框的宽度决定**，不预设固定列宽 ⇒
    /// 判据 = 文本就是数据本身的样子（模板产出的文本长度 = 数据决定的长度）。
    /// </remarks>
    [Fact]
    public void 数据显示器_应产出数据本身的形态_不再有定宽与补齐()
    {
        var bytes = new byte[] { 0x41, 0x42, 0x43 };
        var at = new DateTimeOffset(2026, 9, 27, 21, 53, 0, TimeSpan.Zero);

        var textLines = DataMonitorModule.FormatLines(bytes, hex: false, timestamp: false, perPacket: true, at);
        textLines.ShouldHaveSingleItem().ShouldBe("ABC", "**不做任何补齐**：文本就是这三个字符（通道换行 ⇒ 不补分隔空格）");

        var hexLines = DataMonitorModule.FormatLines(bytes, hex: true, timestamp: false, perPacket: true, at);
        hexLines.ShouldHaveSingleItem().ShouldBe("41 42 43 ", "每字节两位大写十六进制 + 每包末尾一个分隔空格");

        // **不开通道换行时也一样留分隔空格** ⇒ 两包的十六进制永远分得开
        DataMonitorModule.FormatLines(bytes, hex: true, timestamp: false, perPacket: false, at)
            .ShouldHaveSingleItem()
            .ShouldBe("41 42 43 ", "包与包分得开：`41 42 43` 后面接 `65 0C` 就是 `41 42 43 65 0C `");

        var stamped = DataMonitorModule.FormatLines(bytes, hex: true, timestamp: true, perPacket: true, at);
        stamped.ShouldHaveSingleItem().ShouldStartWith("21:53:00.000  ");

        // **没有定宽这回事**：三段文本的字节数各自等于数据本身的长度
        Encoding.UTF8.GetByteCount(textLines[0]).ShouldBe(3);
        Encoding.UTF8.GetByteCount(hexLines[0]).ShouldBe(9, "`41 42 43`（8）+ 分隔空格（1）");
        Encoding.UTF8.GetByteCount(stamped[0]).ShouldBe(23, "时间戳 12 字符 + 两空格（14）+ `41 42 43`（8）+ 分隔空格（1）");
        textLines.Concat(hexLines).Concat(stamped)
            .ShouldAllBe(line => Encoding.UTF8.GetByteCount(line) != 96, "不再有 96 字节的定宽行（那是被裁定删除的口径）");
    }

    /// <summary>
    /// **通道换行 = 换行由调用表达，文本里没有标记**。
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
            text.Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(
                "甲⟨0A⟩乙\n丙\n",
                "所见即所存：数据里那个**不可显示的**换行字符在文件里同样是 `⟨0A⟩`（屏幕上看到的行数 = 文件里的行数），"
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

    /// <summary>
    /// **点「保存」= 模块去请求一次保存对话框**（文件选择是能力，不是界面元素）。
    /// </summary>
    /// <remarks>
    /// 界面那一侧只是一个普通的 `commandButton`（`command: "save"`）—— 它不知道"文件对话框"这回事。
    /// 对话框由**模块**在命令处理里请求（<see cref="Sdk.Context.IFileAccess"/>），参数（过滤串 / 预填名）
    /// 也就跟着归模块所有。这条用例钉住的就是这层接线：**命令 ⇒ 一次请求**，且请求的目标输入源正确。
    /// </remarks>
    [Fact]
    public void 数据显示器_保存命令应当请求一次保存对话框()
    {
        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        var path = Path.Combine(Path.GetTempPath(), $"hyw-monitor-{Guid.NewGuid():N}.txt");

        try
        {
            // 用户点了「保存」（界面只发命令）
            context.EnqueueCommand(DataMonitorModule.SaveCommand);
            module.PumpOnce(context);

            var request = context.FilePickRequests.ShouldHaveSingleItem();
            request.SourceName.ShouldBe(
                DataMonitorModule.SavePathSource, "选定的路径要投回模块读的那个输入源");
            request.Mode.ShouldBe(Sdk.Context.FilePickMode.Save);
            request.SuggestedFileName.ShouldBe(DataMonitorModule.DefaultSaveFileName);
            request.Filter.ShouldBe(DataMonitorModule.SaveFileFilter);

            module.SaveCount.ShouldBe(0, "还没选位置 ⇒ 不写盘");

            // 框架把用户选定的路径投回来 ⇒ **当选就存**
            context.EnqueueInput(DataMonitorModule.SavePathSource, path);
            module.PumpOnce(context);
            module.SaveCount.ShouldBe(1, "选完位置就该存了（一步操作）");

            // 再点一次「保存」⇒ **再请求一次**（用户换存处时不该只能存回同一个文件）
            context.EnqueueCommand(DataMonitorModule.SaveCommand);
            module.PumpOnce(context);
            context.FilePickRequests.Count.ShouldBe(2, "每次点「保存」都重新问一次位置");
            module.SaveCount.ShouldBe(1, "只是又问了一次，还没选 ⇒ 不重复写盘");
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
    /// **用户取消对话框（空结果）⇒ 不改路径、不写盘、也不报错**。
    /// </summary>
    /// <remarks>取消是正常操作，不是失败；把它当失败会让用户每次取消都看到一次红。</remarks>
    [Fact]
    public void 数据显示器_对话框被取消时不得写盘也不得报错()
    {
        var context = new TestModuleContext();
        var module = new DataMonitorModule();
        module.OnRegistered(context);

        context.EnqueueCommand(DataMonitorModule.SaveCommand);
        module.PumpOnce(context);

        // 框架在"用户取消"时投一个空串（与"选定了一个空路径"是同一件事：这次没有结果）
        context.EnqueueInput(DataMonitorModule.SavePathSource, string.Empty);
        module.PumpOnce(context);

        module.SaveCount.ShouldBe(0);
        module.LastSaveFailure.ShouldBeNull();
        context.SetTexts.ShouldBeEmpty("取消不该产生任何消息");
        context.AppearanceReports.ShouldBeEmpty("取消不该改变任何外观");
    }

    /// <summary>
    /// **保存只有一步**：选完位置就**当场写盘**，并把落点/失败**回报给界面**。
    /// </summary>
    /// <remarks>
    /// 本用例**只投路径、不发 `save` 命令** —— 这正是界面上那个按钮做的事；要求再发 `save` 命令的实现，此时 `SaveCount` 会是 0。
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

            var message = Message(context);
            message.ShouldNotBeNullOrEmpty();
            message.ShouldContain("已保存");
            message.ShouldContain(Path.GetFileName(path));   // 落点确认要说清存到哪个文件
            message.ShouldContain("2 行");                   // 并且说清存了多少行（界面那一行要能读）
            Alerted(context, DataMonitorModule.SavePathSource).ShouldBeFalse("成功不标色");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // 失败路径：目录不存在 ⇒ **失败也必须说出来**（不许静默）
        var bad = Path.Combine(Path.GetTempPath(), $"hyw-无此目录-{Guid.NewGuid():N}", "x.txt");
        context.EnqueueInput(DataMonitorModule.SavePathSource, bad);
        module.PumpOnce(context);

        module.SaveCount.ShouldBe(1, "失败不得计为一次成功保存");

        Message(context).ShouldNotBeNullOrEmpty();
        Alerted(context, DataMonitorModule.SavePathSource).ShouldBeTrue("失败由模块标色");
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
            .ShouldBe("显示我", "数据到达即落地 —— 文本方向不加任何分隔字符");

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

    /// <summary>
    /// **空白是分隔符，不是装饰**（最严判据）：每一段必须**恰好两位**。
    /// </summary>
    /// <remarks>
    /// 这条钉住一个"静默吃错"的老毛病：丢掉空白再按两位切，`123456 23 55` 会被解成 5 个字节，
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

    [Fact]
    public void 转换器_空输入应静默无动作()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        module.Convert(context, string.Empty, toHex: true, HexStringConverterModule.CharSourceOf(0)).ShouldBeFalse();
        module.Convert(context, "   ", toHex: false, HexStringConverterModule.HexSourceOf(0)).ShouldBeFalse();

        // 空 ⇒ 结果区**清空**（"把框清掉"与"报错"不是一件事）
        ScreenOf(context, HexStringConverterModule.HexResultSource).ShouldBeEmpty();
        ScreenOf(context, HexStringConverterModule.CharResultSource).ShouldBeEmpty();

        module.LastFailure.ShouldBeNull("空输入不是错");
        Alerted(context, HexStringConverterModule.CharSourceOf(0)).ShouldBeFalse("空输入不标红");
        Alerted(context, HexStringConverterModule.HexSourceOf(0)).ShouldBeFalse("空输入不标红");
    }

    [Fact]
    public void 转换器_没有按钮_输入一变就转()
    {
        var context = new TestModuleContext();
        var module = new HexStringConverterModule();
        module.OnRegistered(context);

        // 第 2 页有内容、当前选中第 2 页
        context.ViewState.SetInputText(HexStringConverterModule.CharSourceOf(0), "AAA");
        context.ViewState.SetInputText(HexStringConverterModule.CharSourceOf(1), "BBB");
        context.ViewState.SetSelectedPage(HexStringConverterModule.CharPagesKey, 1);

        module.Poll(context);
        module.LastHexResult.ShouldBe("42 42 42", "轮询要转的是**当前选中页**（回读视图状态）");

        // **没变就不重算**：再轮一次不该重复写（静止时零开销）
        var writes = context.SetTexts.Count;
        module.Poll(context);
        context.SetTexts.Count.ShouldBe(writes, "内容没变 ⇒ 一个字都不动");

        // 改了这一页的字 ⇒ 立刻跟上（不靠按钮、不靠回车）
        context.ViewState.SetInputText(HexStringConverterModule.CharSourceOf(1), "C");
        module.Poll(context);
        module.LastHexResult.ShouldBe("43");

        // 切回第 1 页 ⇒ 即使"文本没改过"也要重算（源名进了比较键）
        context.ViewState.SetSelectedPage(HexStringConverterModule.CharPagesKey, 0);
        module.Poll(context);
        module.LastHexResult.ShouldBe("41 41 41");
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

    /// <summary>某个源现在是否被标成了**提示色**（看**最后一次**设定，而不是"设过就算"）。</summary>
    /// <param name="context">测试上下文。</param>
    /// <param name="sourceName">数据源名。</param>
    /// <returns>当前被标色为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 必须取最后一条**颜色**报告：<list type="bullet">
    ///   <item>模块清色时也会发一条（<c>Outline = null</c>）—— "设过色"与"现在是这个色"是两件事，
    ///     后者才是界面上的事实；</item>
    ///   <item>**动作复原**（<c>Reset</c>）不是颜色报告，要跳过 —— 它是"把这一格退回去"，
    ///     与这一格什么颜色无关。</item>
    /// </list>
    /// </remarks>
    /// <summary>
    /// **HEX 发送器与文本发送器同一套开关纪律**：间隔为空时怎么拨都开不起来，且写法错误只标那一格。
    /// </summary>
    /// <remarks>
    /// 两个模块共用一份逻辑（同判同形），所以这条与文本发送器那条是**对应的**；
    /// HEX 多一支"写法合法性"——它只影响"这一拍发不发得出去"，不影响开关能不能开。
    /// </remarks>
    [Fact]
    public void 十六进制发送器_开关与间隔同一套纪律_写法错误只标那一格()
    {
        var module = new HexSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        var subject = HexSenderModule.HexSourceOf(0);
        context.ViewState.SetInputText(subject, "41 42");

        // 间隔空着 ⇒ 反复拨也开不起来（那条"回弹后还能开"的回归网）
        for (var i = 1; i <= 3; i++)
        {
            context.EnqueueInput(HexSenderModule.ContinuousSource, "true");
            _ = module.PumpOnce(context);

            module.IsContinuous.ShouldBeFalse($"第 {i} 次拨也不许开（间隔那一格是空的）");
            ContinuousValue(context).ShouldBe("false", "那一格的值必须回到 false");
        }

        Alerted(context, HexSenderModule.IntervalSource).ShouldBeTrue("红落在间隔那一格");

        // 补上合法间隔 + 再拨一次 ⇒ 开起来
        context.ViewState.SetInputText(HexSenderModule.IntervalSource, "50");
        context.EnqueueInput(HexSenderModule.ContinuousSource, "true");
        _ = module.PumpOnce(context);

        module.IsContinuous.ShouldBeTrue("有可用间隔 ⇒ 站得住");
        Alerted(context, HexSenderModule.IntervalSource).ShouldBeFalse("红要清掉");

        // **写法不合法** ⇒ 只标那一格 + 说一句，且**不影响开关**（它仍然开着）
        context.ViewState.SetInputText(subject, "0x41");
        _ = module.PumpOnce(context);

        module.IsContinuous.ShouldBeTrue("写法错误不是开关的成立条件");
        Alerted(context, subject).ShouldBeTrue("哪一格写法不对就标哪一格");
        Message(context).ShouldStartWith("HEX 转换错误");
    }

    /// <summary>**开关那一格现在是什么值**（界面照它画 —— 所以它就是开关的位置）。</summary>
    /// <param name="context">测试上下文。</param>
    /// <returns>那一格的值（未设过 ⇒ 空串）。</returns>
    private static string ContinuousValue(TestModuleContext context) =>
        context.ViewState.GetInputText(TextSenderModule.ContinuousSource) ?? string.Empty;

    /// <summary>
    /// **这一格现在被标出来了没有**（模块说的"这里不对"）。
    /// </summary>
    /// <param name="context">测试上下文。</param>
    /// <param name="sourceName">数据源名。</param>
    /// <remarks>
    /// 判据是**角色词**（`danger`）而不是"某个色值非空"：模块说的是"这一格要配色表里的失败色"，
    /// 具体是哪个红归用户设置管（这样改主题色 / 改 `SemanticError` 时模块侧零改动）。
    /// </remarks>
    private static bool Alerted(TestModuleContext context, string sourceName)
    {
        var last = context.AppearanceReports
            .LastOrDefault(r => !r.Reset && string.Equals(r.SourceName, sourceName, StringComparison.Ordinal));

        return string.Equals(last.OutlineRole, HYWDebugAssistant.Sdk.Views.ViewInkRoles.Danger, StringComparison.Ordinal);
    }
}
