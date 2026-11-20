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
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.DataMonitor;

/// <summary>
/// **数据显示器**：把收到的字节显示出来（文本 / 十六进制），
/// 可暂停、按包换行、加时间戳、清空、保存为 `.txt`。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>模块页</b>：`docs/modules/hyw-data-monitor.md`；</item>
///   <item><b>显示缓冲由框架持有</b>：显示文本**不清空就一直在**（容量 20 000 行、
///     到顶丢最旧并计数），**模块不自己留副本** —— 保存时**回读**显示区内容；</item>
///   <item><b>「暂停」= 收下即弃</b>：暂停时**继续排空接收队列并丢弃**，不在内存里留副本，
///     否则队列会积压到满；</item>
///   <item><b>换行归通道</b>：本模块经
///     `IModuleContext.Display.AppendText`（**不换行**，数据直接接在后面）/
///     `AppendLine`（「数据包换行」或「时间戳」开着 ⇒ 换行**由通道调用**表达）写显示区；
///     **一行多长由文本框宽度决定**，模块侧不规定字节数、不补齐、不加任何标记（`↵` 只由显示层画）；</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "data-monitor",
    DisplayNameKey = DataMonitorText.Title,
    DescriptionKey = DataMonitorText.Description,
    Tags = ["terminal", "monitor", "display"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.DataMonitorViewFactory))]
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

    /// <summary>输入源：保存路径（模块**请求框架弹保存对话框**后，框架把路径投回本输入源）。</summary>
    public const string SavePathSource = "savePath";

    /// <summary>
    /// **保存对话框的默认文件名**（预填一个名字，不留空）。
    /// </summary>
    /// <remarks>
    /// 一句**建议**：框架把它填进保存对话框，用户可改成任何名字或换目录（模块**不**替用户决定路径）。
    /// </remarks>
    public const string DefaultSaveFileName = "savetext1.txt";

    /// <summary>保存对话框的文件类型过滤串（形如 <c>文本文件 (*.txt)|*.txt</c>）。</summary>
    public static string SaveFileFilter => Tr(DataMonitorText.SaveFileFilter);

    /// <summary>
    /// 显示源：**消息**（"已保存到哪 / 为什么没存成"）。
    /// </summary>
    /// <remarks>
    /// 它**没有界面实体**：由模块按自己的逻辑写入，界面上一块 `label`（或只读文本面）按声明吃它。
    /// 这正是"消息不是控件能力"的落点 —— 模块只管写，长在哪儿由声明面决定。
    /// </remarks>
    public const string StatusSource = "status";

    /// <summary>
    /// 显示源：**缓冲已满**提示（空串 = 没满过）。
    /// </summary>
    /// <remarks>
    /// 它由模块按缓冲状态写入，界面上一块**固定高度**的 `label` 吃它 ——
    /// 提示不改变文本区的高度，也不会压在正文上。
    /// </remarks>
    public const string BufferFullSource = "bufferFull";

    /// <summary>
    /// 模块自己挑的**提示角色**（**相对色**）—— 框架只提供"设边框色"这个能力，不认识"红 = 失败"。
    /// </summary>
    /// <remarks>
    /// 值只在这一处；用的是**配色表里的角色词**，不是色值 ⇒ 用户在设置里改那一支色、或换深浅主题，
    /// 这一圈跟着变（写死 ARGB 就不会跟）。要换观感就换词。
    /// </remarks>
    public const string AlertRole = ViewInkRoles.Danger;

    /// <summary>命令名：清空显示区。</summary>
    public const string ClearCommand = "clear";

    /// <summary>命令名：把显示区内容保存到 <see cref="SavePathSource"/> 指定的文件。</summary>
    /// <remarks>
    /// <b>它是"选一个位置并保存"</b>：模块自己请求框架弹保存对话框（
    /// <see cref="Sdk.Context.IFileAccess"/>），用户选定后框架把路径投回
    /// <see cref="SavePathSource"/>，那条输入一到就**当场写盘** —— 与用户自己敲一个路径同一个口径。
    /// </remarks>
    public const string SaveCommand = "save";

    /// <summary>数据类型标识（**与两个发送器逐字相同** ⇒ 开箱即连）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>单条字节上限（与发送端一致）。</summary>
    public const int TextCapacity = 1472;

    /// <summary>**`RawBytes` 的契约形状**：`{ len: u16, data: bytes[1472] }`。</summary>
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
            RawBytesShape)
        {
            // 「速率只要有效数据」三个模块同口径：`len` = 有效字节数。
            // 本模块是**宿**（速率按源计 ⇒ 实际取发送器声明的计量块），但本模块自己作为**类型所有者**
            // 也声明同一份 —— 否则"同一种数据类型在不同模块里说两套计量口径"。
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        // 端口声明在 Views/DataMonitorView.axaml 的 ModuleView.Ports 里。

        context.Declaration.DeclareRole(RoleDeclaration.Input(
            RoleId,
            RawBytesTypeId,
            InPort,
            Tr(DataMonitorText.RoleDisplay)));

        DeclareUi(context);
    }

    /// <summary>
    /// **界面契约**：显示源 / 输入源 / 命令。
    /// </summary>
    /// <remarks>
    /// <b>`savePath` 必须登记为输入源</b>：它**没有界面实体**，用户是在系统保存对话框里选的路径，
    /// 框架把路径投回这条输入。不登记它，选完文件就会被"模块未声明该输入"拒掉 ——
    /// 表现是"对话框选完了却什么都没发生"，而没有任何报错。
    /// </remarks>
    private static void DeclareUi(IModuleContext context)
    {
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LogSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(PausedSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(HexViewSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(LineBreakSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(TimestampSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(SavePathSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StatusSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(BufferFullSource, ViewDataSourceKind.Text));

        // 四个显示开关是用户偏好，恢复；保存路径是一次动作的结果，不恢复。
        context.Declaration.DeclareValue(new ValueDeclaration(PausedSource)
        {
            Editor = ValueEditor.Boolean,
            Persistent = true,
        });
        context.Declaration.DeclareValue(new ValueDeclaration(LineBreakSource)
        {
            Editor = ValueEditor.Boolean,
            Persistent = true,
        });
        context.Declaration.DeclareValue(new ValueDeclaration(HexViewSource)
        {
            Editor = ValueEditor.Boolean,
            Persistent = true,
        });
        context.Declaration.DeclareValue(new ValueDeclaration(TimestampSource)
        {
            Editor = ValueEditor.Boolean,
            Persistent = true,
        });

        context.Declaration.DeclareInput(SavePathSource);

        context.Declaration.DeclareCommand(ClearCommand, Tr(DataMonitorText.Clear));
        context.Declaration.DeclareCommand(SaveCommand, Tr(DataMonitorText.Save));
    }

    // ---------------------------------------------------------------- ③ 钩子块

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Pull;

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (reason.HasFlag(StepReason.Start))
        {
            RestorePersistedState(context);
        }

        // ① 收件箱：命令（清空 / 保存）与输入（开关 / 保存路径）
        PumpOnce(context);

        // ② 接收：**主动取**（本模块是 Pull 模式）；**暂停时收下即弃**（不缓存）
        _tickBuilder.Clear();

        while (context.Ports.TryReceive(out var buffer, out _))
        {
            try
            {
                if (!_paused)
                {
                    _ = ConsumePacket(context, buffer.Span, _tickBuilder);
                }

                // 暂停：什么都不做 —— 但**必须继续排空**（否则队列积压到满）
            }
            finally
            {
                context.Ports.ReleaseBuffer(buffer);
            }
        }

        // **"不换行"的包按 tick 合并成一次追加**（每包一次追加会让缓冲/视图按包数做复制）。
        // 包尾 CR 在本 tick 内可能还在等下一包的 LF；tick 结束必须落成单独 CR，不能丢失。
        _ = _formatter?.FlushPending(_tickBuilder);

        if (_tickBuilder.Length > 0)
        {
            context.Display.AppendText(LogSource, _tickBuilder.ToString());
            _tickBuilder.Clear();
        }
    }

    // ---------------------------------------------------------------- ④ 接线与业务

    private IModuleContext? _context;
    private bool _paused;
    private bool _lineBreakPerPacket;
    private bool _hexView;
    private bool _timestamp;
    private bool _bufferFull;
    private string? _savePath;

    /// <summary>单包渲染的复用缓冲（实例线程独占）。</summary>
    private readonly StringBuilder _lineBuilder = new();

    /// <summary>本 tick 内所有"不换行"包合并后的缓冲（实例线程独占）。</summary>
    private readonly StringBuilder _tickBuilder = new();

    /// <summary>跨包保留的换行状态：包尾 CR 要等下一包首字节决定 CRLF 还是单独 CR。</summary>
    private MonitorLineFormatter? _formatter;
    private MonitorFormat _formatterFormat;

    /// <summary>从持久化视图状态恢复四个显示开关（加载时先恢复 ViewState，再激活）。</summary>
    /// <param name="context">模块上下文。</param>
    public void RestorePersistedState(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Values.ReadText(PausedSource) is { } paused)
        {
            _paused = IsTrue(paused);
        }

        if (context.Values.ReadText(LineBreakSource) is { } lineBreak)
        {
            _lineBreakPerPacket = IsTrue(lineBreak);
        }

        if (context.Values.ReadText(HexViewSource) is { } hex)
        {
            _hexView = IsTrue(hex);
        }

        if (context.Values.ReadText(TimestampSource) is { } timestamp)
        {
            _timestamp = IsTrue(timestamp);
        }
    }

    /// <summary>是否处于"暂停显示"（测试可读）。</summary>
    public bool IsPaused => _paused;

    /// <inheritdoc />
    public void OnConfigurationChanged()
    {
        if (_context is { } context)
        {
            RestorePersistedState(context);
        }
    }

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
    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集）。</summary>
    private static string Tr(string key) => TextCatalog.Get(typeof(DataMonitorModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(DataMonitorModule).Assembly, key, args);

    private void SaveAndReport(IModuleContext context)
    {
        var saved = Save(context);

        var message = saved
            ? Tr(DataMonitorText.StatusSaved, Path.GetFileName(_savePath), LastSavedLineCount)
            : LastSaveFailure ?? Tr(DataMonitorText.StatusSaveFailed);

        // **消息走"写文本"这条路**：界面上由一块 `label`（或只读文本面）显示，
        // 落点由声明面决定 —— 不是"某个控件自己弹一块角"。
        context.Display.SetText(StatusSource, message);

        // **颜色是模块自己的选择**：框架只提供"设边框色"这个基础外观能力，
        // 不认识"红 = 失败"（绿、蓝都行，只要模块自己的逻辑需要）。
        context.Appearance.SetOutlineRole(SavePathSource, saved ? null : AlertRole);
    }

    /// <summary>消费**一次**收件箱（step 的单步；模块测试直接驱动）。</summary>
    /// <param name="context">模块上下文。</param>
    public void PumpOnce(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, ClearCommand, StringComparison.Ordinal))
            {
                _formatter = null;
                _formatterFormat = default;
                ClearDisplay(context);
                continue;
            }

            if (string.Equals(command, SaveCommand, StringComparison.Ordinal))
            {
                // **点「保存」= 选一个位置再存**：由**模块**请求框架弹保存对话框
                // （界面那一侧只是同一个按钮，没有任何"选择器控件"）。
                // 已有路径时同样重新请求 —— 用户要换存处时不该只能存回同一个文件。
                context.Files.Request(new Sdk.Context.FilePickRequest(
                    SavePathSource,
                    Sdk.Context.FilePickMode.Save,
                    SaveFileFilter,
                    DefaultSaveFileName));
                continue;
            }

            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                Tr(DataMonitorText.StatusUnknownCommand, command)));
        }

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            // **四个开关都要把值写回那一格**：开关的**位置**只有一个来源（视图状态里那一格），
            // 界面每帧照它画。只把值收进模块字段、不写回，界面读到的就永远是旧值 ——
            // 用户拨了它又被"读回原值"弹回去，看起来就是"按了它自己又切回去了"。
            if (string.Equals(source, PausedSource, StringComparison.Ordinal))
            {
                _paused = IsTrue(text);
                context.Values.AcceptText(PausedSource, _paused ? "true" : "false");
                continue;
            }

            if (string.Equals(source, LineBreakSource, StringComparison.Ordinal))
            {
                _lineBreakPerPacket = IsTrue(text);
                context.Values.AcceptText(LineBreakSource, _lineBreakPerPacket ? "true" : "false");
                continue;
            }

            if (string.Equals(source, HexViewSource, StringComparison.Ordinal))
            {
                _hexView = IsTrue(text);
                context.Values.AcceptText(HexViewSource, _hexView ? "true" : "false");
                continue;
            }

            if (string.Equals(source, TimestampSource, StringComparison.Ordinal))
            {
                _timestamp = IsTrue(text);
                context.Values.AcceptText(TimestampSource, _timestamp ? "true" : "false");
                continue;
            }

            if (string.Equals(source, SavePathSource, StringComparison.Ordinal))
            {
                // 用户取消（空结果）⇒ 不改路径、不写盘、也不报错：取消是正常操作，不是失败。
                if (text.Length == 0)
                {
                    continue;
                }

                _savePath = text;

                // 选完位置**当场保存**（不再等 `save` 命令）：点「保存」是一步操作 ——
                // 选完就存，用户不必再点第二次。
                SaveAndReport(context);
                continue;
            }
        }

        // **缓冲满过 ⇒ 在自己那块固定高度的 label 上提示**（只回答"有没有丢过"，不计数）：
        // 它写在模块自己的显示源里，长在文本框下面，绝不改变文本区布局。
        var full = context.Display.IsFull(LogSource);
        if (full != _bufferFull)
        {
            _bufferFull = full;
            context.Display.SetText(BufferFullSource, full ? Tr(DataMonitorText.BufferFull) : string.Empty);
        }
    }

    /// <summary>
    /// 处理**一个到达的数据包**（按 `len` 切片 → 渲染文本 → **经通道追加**）。模块测试可直接调用。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="payload">到达的**定长载荷**（`{ len, data }`；用 <see cref="BuildPayload"/> 可构造）。</param>
    /// <returns>实际追加到显示区的**条数**（0 或 1）。</returns>
    /// <remarks>
    /// <b>换行归通道</b>：本方法要么 <see cref="IViewDisplayAccess.AppendText"/>
    /// （数据**直接追加在后面**，不做任何标记），要么 <see cref="IViewDisplayAccess.AppendLine"/>
    /// （「数据包换行」/「时间戳」开着 ⇒ 换行**由通道调用**表达）。
    /// 两条路都**不往文本里塞任何标记**：数据里的换行字符原样保留，通道换行则连字符都不是。
    /// </remarks>
    public int HandlePacket(IModuleContext context, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(context);

        _tickBuilder.Clear();
        var appended = ConsumePacket(context, payload, _tickBuilder);
        _ = _formatter?.FlushPending(_tickBuilder);

        if (_tickBuilder.Length > 0)
        {
            context.Display.AppendText(LogSource, _tickBuilder.ToString());
            _tickBuilder.Clear();
        }

        return appended;
    }

    /// <summary>
    /// **消费一个包**：切片 → 渲染 → 落地（换行包直接追加一行；不换行包并入本 tick 的合并缓冲）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="payload">到达的定长载荷。</param>
    /// <param name="batch">本 tick 的合并缓冲（只有"不换行"路径会写它）。</param>
    /// <returns>实际产出内容的条数（0 或 1）。</returns>
    private int ConsumePacket(IModuleContext context, ReadOnlySpan<byte> payload, StringBuilder batch)
    {
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

        var formatter = GetFormatter();
        formatter.Render(content, DateTimeOffset.Now, _lineBuilder);

        if (formatter.BreakAfterPacket)
        {
            // 通道换行之前先把包尾 CR 落地；否则 AppendLine 之后挂起的 CR 会被新行覆盖。
            _ = formatter.FlushPending(_lineBuilder);
            context.Display.AppendLine(LogSource, _lineBuilder.ToString());
        }
        else
        {
            // **同一 tick 的"不换行"包合成一次追加**：每包一次 `AppendText` 会让显示缓冲每包
            // 复制一份后缀；合成之后这些次数按 tick 而不是按包增长。
            _ = batch.Append(_lineBuilder);
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
            Tr(
                DataMonitorText.StatusShortDatagram,
                length,
                InputLayout.SizeInBytes)));
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
    /// <b>所见即所存</b>："文本框里看到的文本是什么样，保存就是什么样，**并去除标记**（保存的 .txt
    /// 文件无需标记）"。标记**只由显示层画**、根本不进文本 ⇒ 落盘的内容天然既没有 `↵`、也没有任何补齐
    /// （定宽那条路不存在）。换行就是**真的换行**：显示区的「条」之间写成行分隔符，数据里真有的换行字符原样保留。
    /// </remarks>
    public bool Save(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(_savePath))
        {
            LastSaveFailure = Tr(DataMonitorText.StatusSaveNoPath);
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
            // 显示层在数据换行前放了私有哨兵（只用于画箭头），落盘时还原成真实换行种类。
            var lines = context.ViewState.GetDisplayLines(LogSource)
                .Select(HYWDebugAssistant.Sdk.Ui.Controls.NewlineEncoding.Expand)
                .ToList();

            var text = lines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, lines) + Environment.NewLine;

            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            LastSaveFailure = null;
            SaveCount++;
            LastSavedLineCount = lines.Count;
            context.Log.Log(ModuleLogLevel.Information, Tr(DataMonitorText.StatusSavedLog, path, lines.Count));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // 外部 IO 的失败**就地反馈**（不逸出）：路径不可写 / 被占用 / 非法
            LastSaveFailure = Tr(DataMonitorText.StatusSaveError, ex.Message);
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

    /// <summary>
    /// 取本 tick 的 formatter；**跨包保留**，否则包尾 CR 的挂起状态和跨包 CRLF 都不成立。
    /// </summary>
    /// <remarks>
    /// 格式只在 tick 开始时由输入更新一次；换格式时旧 formatter 的 pending 应已在上一 tick 末落地。
    /// </remarks>
    private MonitorLineFormatter GetFormatter()
    {
        var format = CurrentFormat();

        if (_formatter is null || !_formatterFormat.Equals(format))
        {
            _formatter = new MonitorLineFormatter(format);
            _formatterFormat = format;
        }

        return _formatter;
    }

    private static bool IsTrue(string? text) =>
        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

}




