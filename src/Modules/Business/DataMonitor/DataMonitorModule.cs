using System.Diagnostics;
using System.Globalization;
using System.Text;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.DataMonitor;

/// <summary>
/// **数据显示器**（第一批 #3 · 功能角色「数据显示器」）——把收到的字节显示出来（文本 / 十六进制），
/// 可暂停、按包换行、加时间戳、清空、保存为 `.txt`。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>模块页</b>：`docs/modules/hyw-data-monitor.md`（阶段 0 ✅ · 本文件 = 阶段 1/3 实现）；</item>
///   <item><b>显示缓冲由框架持有</b>（`decisions.md` 7.31）：显示文本**不清空就一直在**（容量 20 000 行、
///     到顶丢最旧并计数），**模块不自己留副本** —— 保存时**回读**显示区内容（`decisions.md` 7.29）；</item>
///   <item><b>「暂停」= 收下即弃</b>（用户原话："直接丢弃接收到的数据，**而不是一种在内存里存着**"）⇒
///     暂停时**继续排空接收队列并丢弃**，否则队列会积压到满（那是用户明确不要的行为）；</item>
///   <item><b>换行归通道</b>〔2026-09 用户裁定 · WinForms 模型〕：本模块经
///     `IModuleContext.Display.AppendText`（**不换行**，数据直接接在后面）/
///     `AppendLine`（「数据包换行」或「时间戳」开着 ⇒ 换行**由通道调用**表达）写显示区；
///     **一行多长由文本框宽度决定**，模块侧不规定字节数、不补齐、不加任何标记（`↵` 只由显示层画）；</item>
///   <item><b>结构约定</b>：按 `implementation/06` §C.2 的固定四段写。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "data-monitor",
    "1.0.0",
    DisplayName = "数据显示器",
    Description = "把收到的字节显示出来（文本 / 十六进制）；可暂停、按包换行、加时间戳、清空、保存为 .txt",
    Tags = ["monitor", "display"],
    ParticipatesInDataFlow = true)]
public sealed class DataMonitorModule : IModule, IActivitySource
{
    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>输入端口名（唯一的接收端点）。</summary>
    public const string InPort = "in";

    /// <summary>数据角色 Id（纯汇 ⇒ 只有一个输入角色）。</summary>
    public const string RoleId = "monitor";

    /// <summary>数据源：显示区（A5 文本日志）。</summary>
    public const string LogSource = "log";

    /// <summary>输入源 / 数据源：「暂停显示」开关。</summary>
    public const string PausedSource = "paused";

    /// <summary>输入源 / 数据源：「接收换行」开关。</summary>
    public const string LineBreakSource = "wrap";   // 键名保持不变（组合件/测试兼容；只有**显示名**改为「数据包换行」）

    /// <summary>输入源 / 数据源：「十六进制显示」开关。</summary>
    public const string HexViewSource = "hexView";

    /// <summary>输入源 / 数据源：「添加时间戳」开关。</summary>
    public const string TimestampSource = "timestamp";

    /// <summary>输入源：保存路径（框架的 B6 文件选择器弹**保存对话框**后回投）。</summary>
    public const string SavePathSource = "savePath";

    /// <summary>
    /// **保存对话框的默认文件名**（2026-09 用户裁定："文件名应给一个默认文件名…而不是空着"）。
    /// </summary>
    /// <remarks>
    /// 一句**建议**：框架把它填进保存对话框，用户可改成任何名字或换目录（模块**不**替用户决定路径）。
    /// </remarks>
    public const string DefaultSaveFileName = "savetext1.txt";

    /// <summary>命令名：清空显示区。</summary>
    public const string ClearCommand = "clear";

    /// <summary>命令名：把显示区内容保存到 <see cref="SavePathSource"/> 指定的文件。</summary>
    public const string SaveCommand = "save";

    /// <summary>活动周期（20 ms）。</summary>
    public static readonly TimeSpan ActivityInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>数据类型标识（**与两个发送器逐字相同** ⇒ 开箱即连）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>数据类型版本。</summary>
    public const int RawBytesVersion = 1;

    /// <summary>单条字节上限（与发送端一致）。</summary>
    public const int TextCapacity = 1472;

    /// <summary>**`RawBytes`@v1 的契约形状**：`{ len: u16, data: bytes[1472] }`。</summary>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(TextCapacity)));

    /// <summary>本口契约的布局（读 `len` / `data` 用）。</summary>
    private static readonly ContractLayout InputLayout = ContractLayout.FromShape(RawBytesShape);

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
            // 【2026-09 用户裁定"速率只要有效数据"】三个模块同口径：`len` = 有效字节数。
            // 本模块是**宿**（速率按源计 ⇒ 实际取发送器声明的计量块），但本模块自己作为**类型所有者**
            // 也声明同一份 —— 否则"同一种数据类型在不同模块里说两套计量口径"。
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),

            // **默认边 = 上边**：与两个发送器的"下边"配对 ⇒ 上下叠放即可连（`05` §2 第七条第②项）
            Edge = Sdk.Ports.PortEdge.Top,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Input(
            RoleId,
            new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            InPort,
            "显示"));

        // ---------------------------------------------------------------- 视图（**按控件分块**）
        // 约定见 `TextSenderModule`：**一个控件一段**（控件 + 它的数据源），改默认值只看自己那一段。

        // ① 显示区（A5 `TextLog`）：**尽量大** —— 用户："显示模块要大一点以显示更多的消息"
        var log = new ViewElement(ViewElementKind.TextLog, LogSource, "显示");
        var logSource = new ViewDataSource(LogSource, kind: ViewDataSourceKind.Text);

        // ② 暂停显示开关（B7 滑块）
        var pausedSwitch = new ViewElement(ViewElementKind.ToggleSwitch, PausedSource, "暂停显示");
        var pausedSource = new ViewDataSource(PausedSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.Bool);

        // ③ 接收换行开关（**关**时数据**直接接在后面**：不切块、不补齐、不插任何标记，
        //    一行多长由**文本框宽度**决定 —— 见 `MonitorLineFormatter` 与 `IViewDisplayAccess`）
        var lineBreakSwitch = new ViewElement(ViewElementKind.ToggleSwitch, LineBreakSource, "数据包换行");
        var lineBreakSource = new ViewDataSource(LineBreakSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.Bool);

        // ④ 十六进制显示开关
        var hexViewSwitch = new ViewElement(ViewElementKind.ToggleSwitch, HexViewSource, "十六进制显示");
        var hexViewSource = new ViewDataSource(HexViewSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.Bool);

        // ⑤ 添加时间戳开关
        var timestampSwitch = new ViewElement(ViewElementKind.ToggleSwitch, TimestampSource, "添加时间戳");
        var timestampSource = new ViewDataSource(TimestampSource, kind: ViewDataSourceKind.Value, valueType: ContractPrimitive.Bool);

        // ⑥ 清空按钮（B2）：模块请求框架清掉**显示缓冲**（`IModuleContext.Display`）
        var clearButton = new ViewElement(ViewElementKind.CommandButton, ClearCommand);

        // ⑦ 保存（B6 `FileSelector`，**保存模式**）——**就这一个按钮**：
        //    点它 → 系统保存对话框（预填默认文件名 `savetext1.txt`）→ **选中位置即当场写盘**。
        //    【2026-09 用户裁定】取消"先选位置、再点另一个「保存」按钮"的两步：用户原话
        //    "各种控件占位置且有些多此一举，更好的方案为只提供一个保存按钮，点击后弹出窗口选择位置
        //     → **选择完成自动保存在此处**，同时文件名应给一个默认文件名（如：savetext1.txt）而不是空着"。
        var saveButton = new ViewElement(ViewElementKind.FileSelector, SavePathSource, "保存")
        {
            FileMode = FilePickerMode.Save,
            FileFilter = "文本文件 (*.txt)|*.txt",
            SuggestedFileName = DefaultSaveFileName,
        };

        var savePathSource = new ViewDataSource(SavePathSource, kind: ViewDataSourceKind.Text);

        context.Declaration.DeclareView(new ViewDeclaration(
            [
                log,
                pausedSwitch,
                lineBreakSwitch,
                hexViewSwitch,
                timestampSwitch,
                clearButton,
                saveButton,
            ],
            dataSources:
            [
                logSource,
                pausedSource,
                lineBreakSource,
                hexViewSource,
                timestampSource,
                savePathSource,
            ],
            commands:
            [
                new ViewCommand(ClearCommand, "清空"),

                // `save` 命令**保留**（模块仍受理），但**界面上不再有它的按钮** —— 保存由文件选择器一步完成；
                // 留着它是给"程序化/热键/测试"用的入口，不是给用户点的第二个按钮。
                new ViewCommand(SaveCommand, "保存"),
            ])
        {
            // 尺寸按**实测**定（2026-09 第四批反馈后重定：4 个滑块 + 结果行/标题占位 + 填满型余量）
            PreferredWidth = 460,
            PreferredHeight = 500,
        });
    }

    // ---------------------------------------------------------------- ③ 钩子块

    /// <inheritdoc />
    public Sdk.Lifecycle.ActivityKind Kind => Sdk.Lifecycle.ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => ActivityInterval;

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (!context.StopSignal.IsStopRequested)
        {
            // ① 收件箱：命令（清空 / 保存）与输入（开关 / 保存路径）
            PumpOnce(context);

            // ② 接收：**主动取**（本模块有活动体 ⇒ 主动接收模式）；**暂停时收下即弃**（不缓存）
            while (context.Ports.TryReceive(out var buffer, out _))
            {
                try
                {
                    if (!_paused)
                    {
                        _ = HandlePacket(context, buffer.Span);
                    }

                    // 暂停：什么都不做 —— 但**必须继续排空**（否则队列积压 ⇒ 正是用户不要的"存在内存里"）
                }
                finally
                {
                    context.Ports.ReleaseBuffer(buffer);
                }
            }

            // 【2026-09 用户裁定 · 删除"空闲断行"】此处原先还有一段 `else if (now - lastDataAt >= IdleFlushAfter)
            // FlushPending(...)`：它是"按字节流填 96 字节定宽行"的**必要补充**（不这么办，短包在 OFF 模式下
            // 永远看不到）。定宽行本身已被删除 ⇒ 半行/pending tail 不复存在，**收到即落地**，这一段随之消失。
            _ = context.StopSignal.Wait(ActivityInterval);
        }
    }

    // ---------------------------------------------------------------- ④ 接线与业务

    private bool _paused;
    private bool _lineBreakPerPacket;
    private bool _hexView;
    private bool _timestamp;
    private string? _savePath;

    /// <summary>是否处于"暂停显示"（测试可读）。</summary>
    public bool IsPaused => _paused;

    /// <summary>最近一次保存结果（测试可读；成功为 <see langword="null"/>）。</summary>
    public string? LastSaveFailure { get; private set; }

    /// <summary>已保存的次数（测试可读）。</summary>
    public int SaveCount { get; private set; }

    /// <summary>最近一次成功保存写出的**行数**（用于就地给"已保存 …（N 行）"的落点确认）。</summary>
    public int LastSavedLineCount { get; private set; }

    /// <summary>
    /// 保存并把结果经**输入反馈通道**回报给界面（界面读它来显示落点确认 / 把按钮标红）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <remarks>
    /// <b>为什么让模块回报、而不是视图层自己显示</b>：路径只是"投递出去了"，**写盘成没成功只有模块知道**
    /// （目录不可写 / 被占用 / 非法路径都在模块侧）。视图层若自己写"已保存"就是**预览骗人**。
    /// </remarks>
    private void SaveAndReport(IModuleContext context)
    {
        var saved = Save(context);

        var message = saved
            ? $"已保存：{Path.GetFileName(_savePath)}（{LastSavedLineCount} 行）"
            : LastSaveFailure ?? "保存失败";

        context.InputFeedback.Report(SavePathSource, saved, message);
    }

    /// <summary>消费**一次**收件箱（活动体的单步；模块测试直接驱动）。</summary>
    /// <param name="context">模块上下文。</param>
    public void PumpOnce(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, ClearCommand, StringComparison.Ordinal))
            {
                ClearDisplay(context);
                continue;
            }

            if (string.Equals(command, SaveCommand, StringComparison.Ordinal))
            {
                Save(context);
                continue;
            }

            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                $"未知命令：{command}（模块未实现）"));
        }

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            if (string.Equals(source, PausedSource, StringComparison.Ordinal))
            {
                _paused = IsTrue(text);
                continue;
            }

            if (string.Equals(source, LineBreakSource, StringComparison.Ordinal))
            {
                _lineBreakPerPacket = IsTrue(text);
                continue;
            }

            if (string.Equals(source, HexViewSource, StringComparison.Ordinal))
            {
                _hexView = IsTrue(text);
                continue;
            }

            if (string.Equals(source, TimestampSource, StringComparison.Ordinal))
            {
                _timestamp = IsTrue(text);
                continue;
            }

            if (string.Equals(source, SavePathSource, StringComparison.Ordinal))
            {
                _savePath = text;

                // 【2026-09 用户裁定 · 保存改一步】用户选完位置**当场保存**（不再等 `save` 命令）：
                // 界面上的"保存"就是那个文件选择器按钮，所以"选完就存"才是用户期望的一步操作。
                SaveAndReport(context);
                continue;
            }
        }
    }

    /// <summary>
    /// 处理**一个到达的数据包**（按 `len` 切片 → 渲染文本 → **经通道追加**）。模块测试可直接调用。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="payload">到达的**定长载荷**（`{ len, data }`；用 <see cref="BuildPayload"/> 可构造）。</param>
    /// <returns>实际追加到显示区的**条数**（0 或 1）。</returns>
    /// <remarks>
    /// <b>2026-09 用户裁定 · 换行归通道</b>：本方法要么 <see cref="IViewDisplayAccess.AppendText"/>
    /// （数据**直接追加在后面**，不做任何标记），要么 <see cref="IViewDisplayAccess.AppendLine"/>
    /// （「数据包换行」/「时间戳」开着 ⇒ 换行**由通道调用**表达）。
    /// 两条路都**不往文本里塞任何标记**：数据里的换行字符原样保留，通道换行则连字符都不是。
    /// </remarks>
    public int HandlePacket(IModuleContext context, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_paused)
        {
            return 0;   // 暂停 = 收下即弃（调用方仍会排空队列）
        }

        // **按 `len` 切片**：到达的是**定长载荷**（2 + 1472 字节），真实内容只有前 `len` 字节 ——
        // 不切片就会把 1472 字节的**填充**也当成数据显示出来（`0x00` 是合法数据 ⇒ 绝不能靠"遇到 0 就停"）。
        if (!TrySlicePayload(payload, out var content))
        {
            ReportShortPayloadOnce(context, payload.Length);
            return 0;
        }

        if (content.Length == 0)
        {
            return 0;   // 空包不产出任何显示内容（也不该凭空多出一个空行）
        }

        var formatter = new MonitorLineFormatter(CurrentFormat());
        var text = formatter.Render(content, DateTimeOffset.Now);

        if (formatter.BreakAfterPacket)
        {
            context.Display.AppendLine(LogSource, text);
        }
        else
        {
            context.Display.AppendText(LogSource, text);
        }

        return 1;
    }

    /// <summary>从收到的 `{ len, data }` 载荷里**按 `len` 切片**（禁止 NUL 截断）。</summary>
    /// <param name="payload">到达的字节。</param>
    /// <param name="content">有效内容（调用方用完即弃）。</param>
    /// <returns>载荷是否可读（短于契约 ⇒ <see langword="false"/>，调用方报 Issue）。</returns>
    public static bool TrySlicePayload(ReadOnlySpan<byte> payload, out ReadOnlySpan<byte> content)
    {
        content = default;

        if (!PayloadReader.TryCreate(payload, InputLayout, out var reader))
        {
            return false;
        }

        var length = reader.ReadU16("len");
        var data = reader.ReadBytes("data");
        content = data[..Math.Min(length, data.Length)];
        return true;
    }

    /// <summary>
    /// **把一个数据包渲染成显示文本**（公开给测试与调试：不起框架也能验证"显示成什么样"）。
    /// </summary>
    /// <param name="payload">本包有效字节。</param>
    /// <param name="hex">十六进制显示。</param>
    /// <param name="timestamp">加时间戳。</param>
    /// <param name="perPacket">「数据包换行」= 每包一条（与 <paramref name="timestamp"/> 一起决定**通道**要不要换行）。</param>
    /// <param name="at">时间（测试可固定）。</param>
    /// <returns>要追加的文本（最多一条；空包 ⇒ 空表）。**不含任何标记、不做任何补齐与截断**。</returns>
    /// <remarks>
    /// <b>这里只能回答"文本长什么样"</b>；"要不要换行"由 <see cref="BreaksAfterPacket"/> 回答
    /// —— 两者**分开**，正是"通道换行不是字符"这条裁定的落点。
    /// </remarks>
    public static IReadOnlyList<string> FormatLines(
        ReadOnlySpan<byte> payload,
        bool hex,
        bool timestamp,
        bool perPacket,
        DateTimeOffset at)
    {
        var formatter = new MonitorLineFormatter(new MonitorFormat(hex, timestamp, perPacket));
        var text = formatter.Render(payload, at);
        return text.Length == 0 ? [] : [text];
    }

    /// <summary>
    /// **本包之后换行是否由通道补**（「数据包换行」或「时间戳」开着 —— 与 <see cref="FormatLines"/> 同一判据）。
    /// </summary>
    /// <param name="timestamp">加时间戳。</param>
    /// <param name="perPacket">「数据包换行」。</param>
    /// <returns>是否为通道换行。</returns>
    public static bool BreaksAfterPacket(bool timestamp, bool perPacket) => timestamp || perPacket;

    /// <summary>到达的载荷短于本口契约时**只提示一次**（后续同类丢弃只计数 —— 与参考模块同一口径）。</summary>
    private static void ReportShortPayloadOnce(IModuleContext context, int length)
    {
        if (Interlocked.Exchange(ref _shortPayloadReported, 1) == 1)
        {
            return;
        }

        context.Issues.Report(new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Warning,
            $"收到 {length} 字节的数据报，小于本口契约 {InputLayout.SizeInBytes} 字节 —— **已丢弃**"
            + "（通常是发布方版本比本口旧：接收方必须按到达长度读，`PayloadReader.TryCreate`）。此提示只报一次。"));
    }

    private static int _shortPayloadReported;

    /// <summary>
    /// 构造一个 `{ len, data }` **定长载荷**（模块测试与调试用；发送端就是这么发的）。
    /// </summary>
    /// <param name="content">有效内容（长度 ≤ <see cref="TextCapacity"/>）。</param>
    /// <returns>载荷：`2 + 1472` 字节，`data` 尾部补零。</returns>
    public static byte[] BuildPayload(ReadOnlySpan<byte> content)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Length, TextCapacity);

        var payload = new byte[sizeof(ushort) + TextCapacity];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)content.Length);
        content.CopyTo(payload.AsSpan(sizeof(ushort)));
        return payload;
    }

    /// <summary>把显示区内容保存为 `.txt`（**读的是框架持有的显示缓冲** ⇒ 所见即所存）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>是否保存成功。</returns>
    /// <remarks>
    /// <b>2026-09 用户裁定</b>："文本框里看到的文本是什么样，保存就是什么样，**并去除标记**（保存的 .txt
    /// 文件无需标记）"。标记现在**只由显示层画**、根本不进文本 ⇒ 落盘的内容天然既没有 `↵`、也没有任何补齐
    /// （定宽那条路已删）。换行就是**真的换行**：显示区的"条"之间写成行分隔符，数据里真有的换行字符原样保留。
    /// </remarks>
    public bool Save(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(_savePath))
        {
            LastSaveFailure = "还没有选择保存位置（点界面上的「保存」按钮选一个 .txt）";
            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                LastSaveFailure));
            return false;
        }

        var path = _savePath!;

        try
        {
            // **回读显示缓冲**（框架侧已把"还没被 UI 取走的追加"叠进快照 ⇒ 不会丢尾巴）
            var lines = new List<string>(context.ViewState.GetDisplayLines(LogSource));

            File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            LastSaveFailure = null;
            SaveCount++;
            LastSavedLineCount = lines.Count;
            context.Log.Log(ModuleLogLevel.Information, $"显示内容已保存：{path}（{lines.Count} 行）");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // 外部 IO 的失败**就地反馈**（不逸出）：路径不可写 / 被占用 / 非法
            LastSaveFailure = $"保存失败：{ex.GetType().Name} — {ex.Message}";
            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                LastSaveFailure));
            return false;
        }
    }

    /// <summary>
    /// 清空显示区：**请求框架清掉该显示源的缓冲**（模块不留副本 ⇒ 只能请求 —— `IViewDisplayAccess`）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <remarks>
    /// 模块侧**已经没有任何缓冲**了（定宽行的半行缓冲随"空闲断行"一并删除）⇒ 这里只需发出请求，
    /// 因此它是 <see langword="static"/>（不再读实例状态）。
    /// </remarks>
    public static void ClearDisplay(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Display.Clear(LogSource);
    }

    private MonitorFormat CurrentFormat() => new(_hexView, _timestamp, _lineBreakPerPacket);

    private static bool IsTrue(string? text) =>
        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

}
