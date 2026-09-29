using System.Diagnostics;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.HexSender;

/// <summary>
/// **HEX 发送器**（第一批 #2 · 功能角色「十六进制发送器」）——把十六进制文本解析成字节发出去。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>模块页</b>：`docs/modules/hyw-hex-sender.md`（阶段 0 ✅ · 本文件 = 阶段 1/3 实现）；</item>
///   <item><b>与 `hyw/text-sender` 是"同形态的两兄弟"</b>：视图结构、活动形态、拆包口径完全一致，
///     **唯一差别 = 输入解析**（那边按 UTF-8 编码文本，这边按"每字节两位十六进制"解析字节）；
///     **两边声明同一个类型标识 `RawBytes`@v1** ⇒ 与 `hyw/data-monitor` **开箱即连**（`decisions.md` 7.26）；</item>
///   <item><b>多页消息 + 回读</b>（`decisions.md` 7.28）：`TabPages` 各页一个输入框（源名 `hex1…hex3`），
///     **只有一个「发送」按钮** —— 点它时模块**回读**当前选中页与那一页的当前文本；</item>
///   <item><b>结构约定</b>：按 `implementation/06` §C.2 的固定四段写（①标识与常量 ②声明块 ③钩子块 ④接线与业务）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "hex-sender",
    "1.0.0",
    DisplayName = "HEX 发送器",
    Description = "把一串十六进制文本解析成字节发出去；单次点击 + 连续发送（开关 + 间隔）",
    Tags = ["hex", "sender"],
    ParticipatesInDataFlow = true)]
public sealed class HexSenderModule : IModule, IActivitySource
{
    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>输出端口名（唯一的数据端点）。</summary>
    public const string OutPort = "out";

    /// <summary>数据角色 Id（纯源 ⇒ 只有一个输出角色）。</summary>
    public const string RoleId = "hex-send";

    /// <summary>命令名：发送一次（视图按钮用它关联）。</summary>
    public const string SendCommand = "send";

    /// <summary>标签容器的**状态键**（模块靠它回读"当前选中页"）。</summary>
    public const string PagesStateKey = "hex-pages";

    /// <summary>
    /// 消息页数（每页一个输入框）。
    /// </summary>
    /// <remarks>【2026-09 用户裁定】**3 → 5 页**（"tabpages 的标签数量增加为 5 个"）；原先压在 3 页是因为 `implementation/06` §D.1 的"视图元素 ≤ 12"约定，该约定已按用户裁定**取消数量上限**。</remarks>
    public const int PageCount = 5;

    /// <summary>第 n 页输入源名（<c>hex1</c> … <c>hex5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界回落到第 1 页）。</returns>
    public static string HexSourceOf(int page) =>
        $"hex{Math.Clamp(page, 0, PageCount - 1) + 1}";

    /// <summary>输入源 / 数据源：连续发送开关。</summary>
    public const string ContinuousSource = "continuous";

    /// <summary>输入源 / 数据源：发送间隔（毫秒）。</summary>
    public const string IntervalSource = "interval";

    /// <summary>数据源：已发送计数。</summary>
    public const string SentSource = "sent";

    /// <summary>活动周期（`implementation/06` §E.2：20 ms）。</summary>
    public static readonly TimeSpan ActivityInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>数据类型标识（**与两个兄弟模块逐字相同**）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>数据类型版本。</summary>
    public const int RawBytesVersion = 1;

    /// <summary>单条字节上限（= `data` 字节区长度；一个完整 UDP 载荷）。</summary>
    public const int TextCapacity = 1472;

    /// <summary>**`RawBytes`@v1 的契约形状**：`{ len: u16, data: bytes[1472] }`（与两兄弟完全一致）。</summary>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(TextCapacity)));

    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesTypeId,
            RawBytesVersion,
            RawBytesShape)
        {
            // 【2026-09 用户裁定"速率只要有效数据"】同文本发送器：`len` = 有效字节数 ⇒ 声明它，
            // 框架逐单元读它累计有效字节（否则补零会被算进"有效速率"）。
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),

            // **默认边 = 下边**（用户草图："宽 > 高 ⇒ 网络标签横置在下边处"），与显示器"上边"配对
            Edge = Sdk.Ports.PortEdge.Bottom,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleId,
            new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            OutPort,
            "发送"));

        // ---------------------------------------------------------------- 视图（**按控件分块**）
        // 约定见 `TextSenderModule`：**一个控件一段**（控件 + 它的数据源），改默认值只看自己那一段。

        // ① 分页输入区（C2 `TabPages`）：每页一个多行 HEX 输入框
        var pages = new ViewElement(
            ViewElementKind.TabPages,
            children:
            [
                .. Enumerable.Range(0, PageCount).Select(page => (ViewElement)new ViewElement(
                    ViewElementKind.Group,
                    title: $"{page + 1}",
                    children:
                    [
                        // **`InputKind = Hex`**（用户裁定 D2）：框架只放行 0-9/A-F/a-f 与空白，
                        // 空白**仅排版**；"是否成对、是不是合法字节序列"归模块（失败 ⇒ 红框）
                        new ViewElement(ViewElementKind.TextInput, HexSourceOf(page), "十六进制")
                        {
                            Multiline = true,
                            InputKind = TextInputKind.Hex,
                        },
                    ])),
            ])
        {
            StateKey = PagesStateKey,
        };

        // ①-源：每页一个**文本**数据源（源名 = `hex1…hex5`）
        var pageSources = Enumerable.Range(0, PageCount)
            .Select(page => new ViewDataSource(HexSourceOf(page), kind: ViewDataSourceKind.Text))
            .ToArray();

        // ② 发送按钮（B2）：一个，发哪一页由模块回读
        var sendButton = new ViewElement(ViewElementKind.CommandButton, SendCommand);

        // ③ 连续发送开关（B7 滑块）
        var continuousSwitch = new ViewElement(ViewElementKind.ToggleSwitch, ContinuousSource, "连续发送");
        var continuousSource = new ViewDataSource(ContinuousSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.Bool);

        // ④ 间隔输入（B5）：单位 `ms` 走框外标签
        var intervalInput = new ViewElement(ViewElementKind.TextInput, IntervalSource, "间隔")
        {
            InputKind = TextInputKind.Number,
            Alignment = ViewAlignment.Right,
        };

        var intervalSource = new ViewDataSource(IntervalSource, unit: "ms", kind: ViewDataSourceKind.Text);

        // ⑤ 已发送计数（A1）
        var sentCounter = new ViewElement(ViewElementKind.Numeric, SentSource, "已发送");
        var sentSource = new ViewDataSource(SentSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.U64);

        context.Declaration.DeclareView(new ViewDeclaration(
            [pages, sendButton, continuousSwitch, intervalInput, sentCounter],
            dataSources: [.. pageSources, continuousSource, intervalSource, sentSource],
            commands: [new ViewCommand(SendCommand, "发送")])
        {
            // 尺寸按**实测**定（2026-09 第四批反馈后重定）：与文本发送器同形
            PreferredWidth = 340,
            PreferredHeight = 340,
        });
    }

    // ---------------------------------------------------------------- ③ 钩子块

    /// <inheritdoc />
    public Sdk.Lifecycle.ActivityKind Kind => Sdk.Lifecycle.ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => ActivityInterval;

    // 其余钩子：无配置（不实现 OnConfigurationChanged）· 能力 None（不实现 OnRemoved）·
    // 停/启用由框架暂停活动体（不实现 OnDisabled/OnEnabled）。

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schedule = Stopwatch.StartNew();

        // **下一次"该发"的时刻**（`Zero` = 还没起算 ⇒ 本轮立即发一条，之后按间隔）。
        var nextDue = TimeSpan.Zero;

        _sent = 0;
        context.Ports.Publish(SentSource, CountPayload(0));

        while (!context.StopSignal.IsStopRequested)
        {
            var pending = PumpOnce(context);

            // 间隔被改过 ⇒ 重新起算（"值应用"）
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
                    nextDue = schedule.Elapsed + interval;   // **只在真正发之后推进**（详见文本发送器的说明）
                }
            }
            else
            {
                nextDue = TimeSpan.Zero;
            }

            if (due || pending)
            {
                _ = SendCurrentPage(context, out _);
            }

            _ = context.StopSignal.Wait(ActivityInterval);
        }
    }

    // ---------------------------------------------------------------- ④ 接线与业务

    private ulong _sent;
    private long _continuous;
    /// <summary>
    /// **连续发送的间隔**（毫秒）。默认 <see cref="DefaultIntervalMs"/> —— 用户没填时也应是**已知的稳定频率**。
    /// </summary>
    private int _intervalMs;

    /// <summary>
    /// **未设置间隔时的取值**（0）——连续发送**必须有间隔**：没设置就**显式报错、绝不硬发**（用户 2026-09 裁定）。
    /// </summary>
    public const int UnsetIntervalMs = 0;

    /// <summary>间隔被改过 ⇒ 让活动体**重新起算**调度（"值应用"）。</summary>
    private int _reschedule;

    /// <summary>连续发送是否开着（测试可读）。</summary>
    public bool IsContinuous => Volatile.Read(ref _continuous) == 1;

    /// <summary>当前间隔（毫秒；测试可读）。</summary>
    public int IntervalMilliseconds => _intervalMs;

    /// <summary>已发送计数。</summary>
    public ulong SentCount => Interlocked.Read(ref _sent);

    /// <summary>消费**一次**收件箱（活动体的单步；模块测试直接驱动，无需起线程）。</summary>
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

                // 【2026-09 用户裁定】开关打开时**当场校验**：缺什么报什么（与文本发送器同口径，且**不硬发**）。
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
                if (TryParseInterval(text, out var milliseconds))
                {
                    _intervalMs = (int)Math.Min(milliseconds, int.MaxValue);
                    Interlocked.Exchange(ref _reschedule, 1);   // "值应用"：让活动体按新间隔重新起算
                    context.InputFeedback.Report(IntervalSource, succeeded: true, message: null);
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    context.InputFeedback.Report(IntervalSource, succeeded: false, message: "间隔不能为空（毫秒）；连续发送需要它");
                }
                else
                {
                    context.InputFeedback.Report(
                        IntervalSource,
                        succeeded: false,
                        message: $"间隔必须是 0 – {uint.MaxValue} 的整数（毫秒）");
                }

                continue;
            }

            if (IsHexSource(source))
            {
                _ = Send(context, text!, source, out _);
            }
        }

        return pending == 1;
    }

    /// <summary>当前选中页的十六进制文本（模块**回读**视图状态；空 = 没内容）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>当前页文本（可能为空串）。</returns>
    public static string CurrentPageText(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        return context.ViewState.GetInputText(HexSourceOf(page)) ?? string.Empty;
    }

    /// <summary>发送**当前选中页**的内容（回读视图状态；解析失败 ⇒ 回报失败 ⇒ 该页输入框红框）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="packets">实际投递的数据包个数。</param>
    /// <returns>是否真的发了。</returns>
    public bool SendCurrentPage(IModuleContext context, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var source = HexSourceOf(page);
        var text = context.ViewState.GetInputText(source) ?? string.Empty;
        return Send(context, text, source, out packets);
    }

    /// <summary>解析并发送一段十六进制文本（**模块级测试可直接调用**，无需线程）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="text">十六进制文本。</param>
    /// <param name="source">输入源名（失败反馈打到这个输入框上）。</param>
    /// <param name="packets">实际投递的数据包个数。</param>
    /// <returns>是否真的发了。</returns>
    public bool Send(IModuleContext context, string text, string source, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);
        packets = 0;

        if (!HexTextCodec.TryParse(text, out var bytes, out var failure))
        {
            // **解析失败 ⇒ 就地反馈（红框）**（用户裁定：范围/合法性归模块；框架只判"格式"）
            context.InputFeedback.Report(source, succeeded: false, message: failure);
            return false;
        }

        if (bytes.Length == 0)
        {
            // 空（或全空白）⇒ **静默不发**（T11）
            return false;
        }

        context.InputFeedback.Report(source, succeeded: true, message: null);

        for (var offset = 0; offset < bytes.Length; offset += TextCapacity)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(TextCapacity, bytes.Length - offset));

            if (context.Ports.TrySend(OutPort, BuildPayload(chunk, TextCapacity)))
            {
                packets++;
            }
        }

        if (packets > 0)
        {
            var total = Interlocked.Add(ref _sent, (ulong)packets);
            context.Ports.Publish(SentSource, CountPayload(total));
        }

        if (packets > 1)
        {
            context.Log.Log(
                ModuleLogLevel.Information,
                $"字节数超过单包上限，已拆成 {packets} 个数据包发送（接收侧会表现为 {packets} 条）");
        }

        return packets > 0;
    }

    /// <summary>把一块字节写进 `{ len, data }` 形状的定长载荷（`data` 尾部补零）。</summary>
    /// <param name="chunk">有效字节。</param>
    /// <param name="capacity">`data` 区长度。</param>
    /// <returns>载荷。</returns>
    public static byte[] BuildPayload(ReadOnlySpan<byte> chunk, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunk.Length, capacity);

        var payload = new byte[sizeof(ushort) + capacity];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)chunk.Length);
        chunk.CopyTo(payload.AsSpan(sizeof(ushort)));
        return payload;
    }

    /// <summary>解析「间隔」（0 – u32 最大值；用户裁定"不预设用户需求"）。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="milliseconds">解析结果。</param>
    /// <returns>成功为 <see langword="true"/>。</returns>
    public static bool TryParseInterval(string? text, out uint milliseconds)
    {
        milliseconds = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return uint.TryParse(
            text.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out milliseconds);
    }

    private static bool IsHexSource(string source)
    {
        for (var i = 0; i < PageCount; i++)
        {
            if (string.Equals(source, HexSourceOf(i), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] CountPayload(ulong value)
    {
        var payload = new byte[sizeof(ulong)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        return payload;
    }
}
