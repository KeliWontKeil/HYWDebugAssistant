using System.Diagnostics;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.TextSender;

/// <summary>
/// **文本发送器**（第一批 #1 · 功能角色「文本发送器」）——发送一串文本；单次点击 + 连续发送（开关 + 间隔）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>模块页</b>：`docs/modules/hyw-text-sender.md`（阶段 0/1/2 ✅ · 本文件 = 阶段 3 实现）；</item>
///   <item><b>字节口径</b>（用户 2026-09 裁定"与 C 里面的约定对齐"）：`len` = **字节数**（`strlen` 同口径）·
///     文本编码 **UTF-8**（ASCII 部分单字节兼容）· **`0x00` 是合法数据、不是终止符** · 协议层**不碰字符**；</item>
///   <item><b>多页消息 + 回读</b>（2026-09 · `decisions.md` 7.28）：输入区是 `TabPages`（各页一个输入框，源名
///     `text1…text3`），**只有一个「发送」按钮** —— 点它时模块**回读**"当前选中页 + 该页的当前文本"
///     （含用户**尚未提交**的编辑）⇒ **所见即所发**，且 UI 上不必每页重复一套控件；</item>
///   <item><b>结构约定</b>：本类按 `implementation/06` §C.2 的**固定四段**写 —— ① 标识与常量 ② 声明块
///     ③ 钩子块 ④ 接线与业务。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "text-sender",
    "1.0.0",
    DisplayName = "文本发送器",
    Description = "发送一串文本；单次点击 + 连续发送（开关 + 间隔）",
    Tags = ["text", "sender"],
    ParticipatesInDataFlow = true)]
public sealed class TextSenderModule : IModule, IActivitySource
{
    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>输出端口名（唯一的数据端点）。</summary>
    public const string OutPort = "out";

    /// <summary>数据角色 Id（**一种数据类型 = 一个角色**；本模块是纯源 ⇒ 只有一个输出角色）。</summary>
    public const string RoleId = "text-send";

    /// <summary>命令名：发送一次（视图按钮用它关联）。</summary>
    public const string SendCommand = "send";

    /// <summary>标签容器的**状态键**：模块靠它回读"当前选中页"（`ViewElement.StateKey`）。</summary>
    public const string PagesStateKey = "text-pages";

    /// <summary>
    /// 消息页数（每页一个输入框）。
    /// </summary>
    /// <remarks>
    /// 【2026-09 用户裁定】**3 → 5 页**：用户原话"**tabpages 的标签数量增加为 5 个**"。
    /// 此前压在 3 页是因为 `implementation/06` §D.1 有一条"视图元素 ≤ 12"的约定（4 页会到 13 ⇒ 超限）；
    /// 该约定已按用户裁定**取消数量上限**（"12 个太少了，建议不设上限…无意义的约束不符合逻辑"）⇒ 页数可自由取。
    /// </remarks>
    public const int PageCount = 5;

    /// <summary>第 n 页输入源名（<c>text1</c> … <c>text5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界时回落到第 1 页 —— 表格是声明的一部分，不该因越界崩）。</returns>
    public static string TextSourceOf(int page) =>
        $"text{Math.Clamp(page, 0, PageCount - 1) + 1}";

    /// <summary>输入源 / 数据源：连续发送开关。</summary>
    public const string ContinuousSource = "continuous";

    /// <summary>输入源 / 数据源：发送间隔（毫秒，数值输入 + 框外单位标签）。</summary>
    public const string IntervalSource = "interval";

    /// <summary>数据源：已发送计数。</summary>
    public const string SentSource = "sent";

    /// <summary>活动周期（`implementation/06` §E.2：20 ms — 人工操作无感，也让收件箱及时被消费）。</summary>
    public static readonly TimeSpan ActivityInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// 数据类型标识 —— **PascalCase 短名**（`03` §6 的新范式；旧式 `hyw/*` 已冻结）。
    /// </summary>
    /// <remarks>
    /// 这个类型的本质是**一段带长度的字节区**（0–1472 字节）——**文本（UTF-8）与 HEX 字节都只是它的用法**。
    /// 文本发送器、HEX 发送器与数据监视器**共用同一标识**才能**开箱即连**
    /// （兼容规则：**不同标识默认不兼容**，要用户显式强连 —— `ADR-0051` · `decisions.md` 7.26）。
    /// </remarks>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>数据类型版本。</summary>
    public const int RawBytesVersion = 1;

    /// <summary>
    /// **单条文本的字节上限**（= `data` 字节区的长度；**一个完整 UDP 载荷**，用户 2026-09 裁定）。
    /// </summary>
    /// <remarks>
    /// 契约**只定长**（`03` §2）⇒ 每个数据单元**永远搬 `2 + 1472 = 1474` 字节**（就算只发 1 个字符）。
    /// 超过 1472 的文本由本模块**内部拆成多个数据包**发送（用户裁定：契约不上调；接收侧因此表现为多条）。
    /// </remarks>
    public const int TextCapacity = 1472;

    /// <summary>
    /// **`RawBytes`@v1 的契约形状**：`{ len: u16, data: bytes[1472] }`。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>`len` = 有效字节数</b>（0–1472）——**唯一的"到哪结束"判据**；
    ///     `data` 尾部**补零**只是定长填充，**`0x00` 是合法数据**（不是终止符）；</item>
    ///   <item>**文本**类的用法：按 **UTF-8** 编码进 `data`，拆包**按字符边界**切；
    ///     **HEX/二进制**类的用法：字节原样进 `data`；**形状不区分二者** —— 那是用法的事；</item>
    ///   <item>消费者：下游模块（如 `hyw/data-monitor`）按 `len` 切片后解码 —— 形状本身**不解释语义**。</item>
    /// </list>
    /// </remarks>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(TextCapacity)));

    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ① 数据类型（形状即契约）→ ② 口 → ③ 角色 → ④ 视图（顺序即 `06` §C.2 的声明块顺序）
        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesTypeId,
            RawBytesVersion,
            RawBytesShape)
        {
            // 【2026-09 用户裁定"速率只要有效数据"】`len` 就是**有效字节数**（0–1472，尾部补零不算）
            // ⇒ 声明它 ⇒ 框架逐单元读这个字段累计有效字节，第 3 档速率才是**真实**有效速率
            //（未声明时框架只能按"契约尺寸"算，把补零也算成有效数据）。
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),

            // **默认边 = 下边**（用户草图："由于宽 > 高，因此网络标签横置在下边处"）：
            // 与 `hyw/data-monitor` 的"上边"配对 ⇒ 上下叠放即可连（`05` §2 七条判定的第 ② 条）。
            Edge = Sdk.Ports.PortEdge.Bottom,
        });

        // 纯源角色：**只有输出口** ⇒ α（两侧同型）天然满足；单角色 ⇒ 不声明 DeclareFlow
        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleId,
            new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            OutPort,
            "发送"));

        // ---------------------------------------------------------------- 视图（**按控件分块**）
        //
        // 约定（2026-09 用户裁定）：视图声明**一个控件一段**，每段自带它的默认属性 + 它的数据源 ——
        // 改观感/默认值时**只看自己那一段**，不用在一条大表达式里到处翻。
        // 段序 = 界面自上而下的顺序；段内顺序 = ①控件 ②它的输入/数据源 ③（必要时）该控件的说明。

        // ① 分页输入区（C2 `TabPages`）：每页一个多行输入框（可存多条消息；不持久化）
        var pages = new ViewElement(
            ViewElementKind.TabPages,
            children:
            [
                .. Enumerable.Range(0, PageCount).Select(page => (ViewElement)new ViewElement(
                    ViewElementKind.Group,
                    title: $"{page + 1}",
                    children:
                    [
                        new ViewElement(ViewElementKind.TextInput, TextSourceOf(page), "内容")
                        {
                            Multiline = true,   // 多行：用户裁定 T4（提交键走**应用级**设置）
                        },
                    ])),
            ])
        {
            StateKey = PagesStateKey,   // 模块靠它回读"当前选中页"（7.28）
        };

        // ①-源：每页一个**文本**数据源（源名 = `text1…text5`，见 `TextSourceOf`）
        var pageSources = Enumerable.Range(0, PageCount)
            .Select(page => new ViewDataSource(TextSourceOf(page), kind: ViewDataSourceKind.Text))
            .ToArray();

        // ② 发送按钮（B2 `CommandButton`）：**只有一个** —— 发哪一页由模块**回读**当前选中页（7.28）
        var sendButton = new ViewElement(ViewElementKind.CommandButton, SendCommand);

        // ③ 连续发送开关（B7 `ToggleSwitch`，滑块外观）：`true/false` 走输入通道
        var continuousSwitch = new ViewElement(ViewElementKind.ToggleSwitch, ContinuousSource, "连续发送");
        var continuousSource = new ViewDataSource(ContinuousSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.Bool);

        // ④ 间隔输入（B5 `TextInput`）：单位 `ms` 走**框外单位标签**（不做框内后缀）
        var intervalInput = new ViewElement(ViewElementKind.TextInput, IntervalSource, "间隔")
        {
            InputKind = TextInputKind.Number,     // 只放行数字写法（范围校验归模块 + 输入反馈）
            Alignment = ViewAlignment.Right,      // 对齐由**模块声明**（框架不限死）
        };

        // 「间隔」是**文本输入**（用户敲的是数字字符、经文本通道投递 ⇒ `SubmitInput` 签名是 string）——
        // "这是数字"由元素声明的 `InputKind = Number` 表达，**不是**数据源类型。
        var intervalSource = new ViewDataSource(IntervalSource, unit: "ms", kind: ViewDataSourceKind.Text);

        // ⑤ 已发送计数（A1 `Numeric`）：只读数值，值由模块发布
        var sentCounter = new ViewElement(ViewElementKind.Numeric, SentSource, "已发送");
        var sentSource = new ViewDataSource(SentSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64);

        context.Declaration.DeclareView(new ViewDeclaration(
            [pages, sendButton, continuousSwitch, intervalInput, sentCounter],
            dataSources: [.. pageSources, continuousSource, intervalSource, sentSource],
            commands: [new ViewCommand(SendCommand, "发送")])
        {
            // 尺寸按**实测**定（2026-09 第四批反馈后重定：内容最小高 233 + 上下预留带 48 + 填满型余量）
            PreferredWidth = 340,
            PreferredHeight = 340,
        });
    }

    // ---------------------------------------------------------------- ③ 钩子块

    /// <inheritdoc />
    public Sdk.Lifecycle.ActivityKind Kind => Sdk.Lifecycle.ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => ActivityInterval;

    // 六个钩子里**只实现需要的**（其余有默认空实现）：
    //   · `OnConfigurationChanged` —— 本模块**没有配置**（用户裁定 T2/T3：不追加行尾、间隔不做配置字段）；
    //   · `OnRemoved` —— 能力 = `None`，没有需要释放的外部资源；
    //   · `OnDisabled` / `OnEnabled` —— 框架停/启活动体即可（连续发送随之停/恢复，无需额外状态）。

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schedule = Stopwatch.StartNew();

        // **下一次"该发"的时刻**（`Zero` = 还没起算 ⇒ 本轮立即发一条，之后按间隔）。
        var nextDue = TimeSpan.Zero;

        // 初始状态：把「已发送」置 0（`OnActivated()` 没有上下文 ⇒ 在活动体开头做这件"需要上下文"的事）
        _sent = 0;
        context.Ports.Publish(SentSource, CountPayload(0));

        while (!context.StopSignal.IsStopRequested)
        {
            var pending = PumpOnce(context);

            // 间隔被改过 ⇒ 重新起算（"值应用"：改完立刻按新值走，不用等旧的到期）
            if (Interlocked.Exchange(ref _reschedule, 0) == 1)
            {
                nextDue = TimeSpan.Zero;
            }

            var due = false;

            if (Volatile.Read(ref _continuous) == 1 && _intervalMs > UnsetIntervalMs)
            {
                var interval = TimeSpan.FromMilliseconds(_intervalMs);

                if (nextDue <= TimeSpan.Zero)
                {
                    nextDue = schedule.Elapsed;
                }

                if (schedule.Elapsed >= nextDue)
                {
                    due = true;

                    // 【2026-09 缺陷修复 · 用户实测"配置了值后静默不发送 / 不配置值却按未知频率猛发"】
                    // 旧写法是**每轮循环都 `nextTick += interval`**（本循环每 20 ms 跑一轮）⇒
                    //   · 间隔 > 20 ms ⇒ `nextTick` 每轮都往前跳、**永远追不上** ⇒ **一条都不发**；
                    //   · 间隔 = 0（未配置时的默认值）⇒ 每轮都到期 ⇒ **每 20 ms 发一条**（"未知频率"）。
                    // 现在：**只在真正发出去之后**才把 `nextDue` 推进一个间隔 ⇒ 间隔即真实发送周期。
                    nextDue = schedule.Elapsed + interval;
                }
            }
            else
            {
                nextDue = TimeSpan.Zero;   // 关掉连续发送 ⇒ 清除起算（再打开时立即发一条，然后按间隔）
            }

            if (due || pending)
            {
                // **每周期都回读当前页**（而不是缓存上次那条）：所见即所发，切页立刻生效
                _ = SendCurrentPage(context, out _);
            }

            _ = context.StopSignal.Wait(ActivityInterval);
        }
    }

    // ---------------------------------------------------------------- ④ 接线与业务

    private ulong _sent;
    private long _continuous;

    /// <summary>
    /// **连续发送的间隔**（毫秒）。默认 <see cref="DefaultIntervalMs"/> —— 用户没填时也应是**已知的稳定频率**，
    /// 不能像旧实现那样"0 毫秒 ⇒ 每轮循环都发"（用户的"未知频率猛发"就是它）。
    /// </summary>
    private int _intervalMs;

    /// <summary>
    /// **未设置间隔时的取值**（0）——连续发送**必须有间隔**：没设置就**显式报错、绝不硬发**
    /// （用户 2026-09 裁定："**留空应该显式报错而不是硬发**"）。
    /// </summary>
    public const int UnsetIntervalMs = 0;

    /// <summary>间隔被改过 ⇒ 让活动体**重新起算**调度（"值应用"：改完立刻按新值走）。</summary>
    private int _reschedule;

    /// <summary>
    /// 消费**一次**收件箱（活动体的单步；模块测试直接驱动它，无需起线程）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>是否需要"发一次"（命令到达）。</returns>
    public bool PumpOnce(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var pending = 0;

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, SendCommand, StringComparison.Ordinal))
            {
                _ = Interlocked.Exchange(ref pending, 1);
                continue;
            }

            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                $"未知命令：{command}（模块未实现）"));
        }

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            if (string.Equals(source, ContinuousSource, StringComparison.Ordinal))
            {
                var on = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
                Volatile.Write(ref _continuous, on ? 1 : 0);

                // 【2026-09 用户裁定】开关**打开时**必须**当场校验**，缺什么就报什么（G3 就地反馈：红框 + 原因）：
                //   · 当前页没有内容 ⇒ 报错；· **还没有设置间隔** ⇒ 报错（"留空应该显式报错而不是硬发"）。
                // 两条都不满足时才真正开始连续发送；**不硬发**（T11 语义不变：空内容不发）。
                var missing = string.IsNullOrEmpty(CurrentPageText(context))
                    ? "当前页没有内容"
                    : _intervalMs <= UnsetIntervalMs
                        ? "还没有设置间隔（毫秒）"
                        : null;

                context.InputFeedback.Report(
                    ContinuousSource,
                    succeeded: missing is null,
                    message: missing is null ? null : $"{missing}：连续发送开着也发不出东西（先补上这一项）");

                continue;
            }

            if (string.Equals(source, IntervalSource, StringComparison.Ordinal))
            {
                if (TextSenderCodec.TryParseInterval(text, out var milliseconds))
                {
                    _intervalMs = (int)Math.Min(milliseconds, int.MaxValue);
                    Interlocked.Exchange(ref _reschedule, 1);   // "值应用"：让活动体按新间隔重新起算
                    context.InputFeedback.Report(IntervalSource, succeeded: true, message: null);
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    // 空 ⇒ **显式报错**（"留空应该显式报错而不是硬发"）
                    context.InputFeedback.Report(IntervalSource, succeeded: false, message: "间隔不能为空（毫秒）；连续发送需要它");
                }
                else
                {
                    // **范围/合法性归模块**（框架只判"是不是数字写法"）⇒ 失败走 G3 的就地反馈（红框）
                    context.InputFeedback.Report(
                        IntervalSource,
                        succeeded: false,
                        message: $"间隔必须是 0 – {uint.MaxValue} 的整数（毫秒）");
                }

                continue;
            }

            // 文本输入（text1…text3）：**回车 / 失焦**提交 ⇒ 等价于"发这一页"
            if (IsTextSource(source, out var page))
            {
                _ = Send(context, text!, out _);
                _ = page;
            }
        }

        return pending == 1;
    }

    /// <summary>连续发送是否开着（测试可读）。</summary>
    public bool IsContinuous => Volatile.Read(ref _continuous) == 1;

    /// <summary>当前间隔（毫秒；测试可读）。</summary>
    public int IntervalMilliseconds => _intervalMs;

    /// <summary>已发送计数。</summary>
    public ulong SentCount => Interlocked.Read(ref _sent);

    /// <summary>
    /// **发送当前选中页的内容**（回读视图状态 ⇒ 含用户**尚未提交**的编辑；空文本 ⇒ 静默不发）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="packets">实际投递的数据包个数（拆包后可能 &gt; 1）。</param>
    /// <returns>是否真的发了。</returns>
    public bool SendCurrentPage(IModuleContext context, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var text = context.ViewState.GetInputText(TextSourceOf(page)) ?? string.Empty;
        return Send(context, text, out packets);
    }

    /// <summary>当前选中页的文本（模块**回读**视图状态；空 = 没内容）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>当前页文本（可能为空串）。</returns>
    public static string CurrentPageText(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        return context.ViewState.GetInputText(TextSourceOf(page)) ?? string.Empty;
    }
    /// <summary>发送一段文本（**模块级测试可直接调用**，无需线程）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="text">要发送的文本（原样，不做规范化）。</param>
    /// <param name="packets">实际投递的数据包个数。</param>
    /// <returns>是否真的发了（**空文本 ⇒ false 且什么都不做** —— 用户裁定 T11）。</returns>
    public bool Send(IModuleContext context, string text, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);
        packets = 0;

        // **空内容静默不发**（T11："窗口为空这在各个层级的意义都是一致的：没东西，不发即可"）
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var chunk in TextSenderCodec.SplitByCharacterBoundary(text, TextCapacity))
        {
            var payload = TextSenderCodec.BuildPayload(chunk, TextCapacity);

            if (context.Ports.TrySend(OutPort, payload))
            {
                packets++;
            }
        }

        if (packets > 0)
        {
            var total = Interlocked.Add(ref _sent, (ulong)packets);
            context.Ports.Publish(SentSource, CountPayload(total));
        }

        // 拆包提示：**状态栏一条**（T14c：拆包是"真的在发"，与"空 = 不发"不同 ⇒ 应知情，但不弹窗、不红框）
        if (packets > 1)
        {
            context.Log.Log(
                ModuleLogLevel.Information,
                $"文本被拆成 {packets} 个数据包发送（单包上限 {TextCapacity} 字节；接收侧会表现为 {packets} 条）");
        }

        return packets > 0;
    }

    private static bool IsTextSource(string source, out int page)
    {
        for (var i = 0; i < PageCount; i++)
        {
            if (string.Equals(source, TextSourceOf(i), StringComparison.Ordinal))
            {
                page = i;
                return true;
            }
        }

        page = -1;
        return false;
    }

    private static byte[] CountPayload(ulong value)
    {
        var payload = new byte[sizeof(ulong)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        return payload;
    }
}