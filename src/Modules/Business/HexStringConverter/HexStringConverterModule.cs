using System.Text;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.HexStringConverter;

/// <summary>
/// **HEX ↔ 字符串转换器**（第一批 #4 · 功能角色「十六进制/字符串转换器」）—— **零口零角色**的离线小工具。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>模块页</b>：`docs/modules/hyw-hex-string-converter.md`；</item>
///   <item><b>不交换数据</b>：**没有任何端口** ⇒ 不进拓扑、不参与建网；数据只走**视图输入 / 数据源**通道
///     （契约层已核实：零口零角色**合法且不出建议**）；</item>
///   <item><b>结构与触发</b>：char 组与 hex 组**各一个 `TabPages`**（各 5 页，可存多条消息、
///     **不持久化**）；**每个方向一个按钮**；**点按钮或敲回车**都能触发 —— 两者都靠
///     **回读「当前选中页 + 该页当前文本」**实现，UI 上不必每页重复控件；</item>
///   <item><b>失败 ⇒ 该输入框红框</b>（「如果转换失败，将文本框边框显著标红」⇒ G3 的就地反馈）；</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "hex-string-converter",
    "1.0.0",
    DisplayName = "HEX ↔ 字符串转换器",
    Description = "十六进制与字符串互转的离线小工具（无端口、不交换数据）",
    Tags = ["hex", "tool"],
    ParticipatesInDataFlow = false)]
public sealed class HexStringConverterModule : IModule, IActivitySource
{
    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>命令名：把 **char 组当前页**的内容转成 HEX。</summary>
    public const string ToHexCommand = "to-hex";

    /// <summary>命令名：把 **hex 组当前页**的内容转成字符。</summary>
    public const string ToTextCommand = "to-text";

    /// <summary>char 组的标签容器状态键（回读"当前选中页"）。</summary>
    public const string CharPagesKey = "char-pages";

    /// <summary>hex 组的标签容器状态键。</summary>
    public const string HexPagesKey = "hex-pages";

    /// <summary>
    /// 每组页数。
    /// </summary>
    /// <remarks>
    /// 页数取 5（`tabpages` 的标签数量为 5 个）；视图元素数量不设上限。
    /// </remarks>
    public const int PageCount = 5;

    /// <summary>第 n 页 char 输入源名（<c>char1</c> … <c>char5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界回落到第 1 页）。</returns>
    public static string CharSourceOf(int page) => $"char{Math.Clamp(page, 0, PageCount - 1) + 1}";

    /// <summary>第 n 页 hex 输入源名（<c>hex1</c> … <c>hex5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界回落到第 1 页）。</returns>
    public static string HexSourceOf(int page) => $"hex{Math.Clamp(page, 0, PageCount - 1) + 1}";

    /// <summary>显示源：char→HEX 的结果区。</summary>
    public const string HexResultSource = "hexResult";

    /// <summary>显示源：HEX→char 的结果区。</summary>
    public const string CharResultSource = "charResult";

    /// <summary>结果行的定宽（A5 显示源要求"同一数据源样本长度必须一致"）。</summary>
    public const int ResultLineBytes = 96;

    /// <summary>活动周期（20 ms）。</summary>
    public static readonly TimeSpan ActivityInterval = TimeSpan.FromMilliseconds(20);


    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // **零口零角色**：不声明数据类型、不声明口、不声明角色（契约层核实过：合法且不出建议）
        // ---------------------------------------------------------------- 视图
        // 界面形态**不在代码里声明**：控件树、数据源、命令与窗口尺寸都在包内 `view.json`
        // （唯一事实源），由框架读取并按 schema 补齐默认值。
        // 本模块的数据源名（`char1…char5` / `hex1…hex5` / `hexResult` / `charResult`）与
        // 命令名（`to-hex` / `to-text`）必须与 `view.json` 一致。
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

        // 本模块**没有端口**、也没有活动体自身的业务 —— 活动体的唯一职责是**消费收件箱**
        // （无活动循环的模块不消费收件箱，见 `02-module-sdk`「人机操作的投递」）。
        while (!context.StopSignal.IsStopRequested)
        {
            PumpOnce(context);
            _ = context.StopSignal.Wait(ActivityInterval);
        }
    }

    // ---------------------------------------------------------------- ④ 接线与业务

    /// <summary>最近一次转换的方向与结果（测试可读）。</summary>
    public string? LastResult { get; private set; }

    /// <summary>最近一次失败原因（成功为 <see langword="null"/>）。</summary>
    public string? LastFailure { get; private set; }

    /// <summary>消费**一次**收件箱（活动体的单步；模块测试直接驱动）。</summary>
    /// <param name="context">模块上下文。</param>
    public void PumpOnce(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, ToHexCommand, StringComparison.Ordinal))
            {
                ConvertCurrentPage(context, toHex: true);
                continue;
            }

            if (string.Equals(command, ToTextCommand, StringComparison.Ordinal))
            {
                ConvertCurrentPage(context, toHex: false);
                continue;
            }

            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                $"未知命令：{command}（模块未实现）"));
        }

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            // **回车 / 失焦提交**也触发转换（点按钮**或**敲回车均可）
            if (IsCharSource(source))
            {
                Convert(context, text, toHex: true, source);
                continue;
            }

            if (IsHexSource(source))
            {
                Convert(context, text, toHex: false, source);
            }
        }
    }

    /// <summary>转换**当前选中页**的内容（回读视图状态 —— 含尚未提交的编辑）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="toHex">true = char → HEX；false = HEX → char。</param>
    /// <returns>是否产出了结果（空输入 ⇒ false，静默无动作）。</returns>
    public bool ConvertCurrentPage(IModuleContext context, bool toHex)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(toHex ? CharPagesKey : HexPagesKey) ?? 0;
        var source = toHex ? CharSourceOf(page) : HexSourceOf(page);
        var text = context.ViewState.GetInputText(source) ?? string.Empty;

        return Convert(context, text, toHex, source);
    }

    /// <summary>转换一段文本（**模块级测试可直接调用**，无需线程）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="text">输入文本。</param>
    /// <param name="toHex">true = char → HEX；false = HEX → char。</param>
    /// <param name="source">输入源名（失败反馈打到这个输入框上）。</param>
    /// <returns>是否产出了结果。</returns>
    public bool Convert(IModuleContext context, string? text, bool toHex, string source)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ok = toHex
            ? ConverterCodec.TryToHex(text, out var result, out var failure)
            : ConverterCodec.TryToText(text, out result, out failure);

        if (!ok)
        {
            // **失败 ⇒ 该输入框红框**；结果区**不动**
            LastFailure = failure;
            LastResult = null;
            context.InputFeedback.Report(source, succeeded: false, message: failure);
            return false;
        }

        // **空输入 ⇒ 静默无动作**（不改结果、不报错、**也不回报"成功"** —— "空"在各层意义一致）
        if (result.Length == 0)
        {
            LastFailure = null;
            LastResult = null;
            return false;
        }

        context.InputFeedback.Report(source, succeeded: true, message: null);

        LastFailure = null;
        LastResult = result;

        var target = toHex ? HexResultSource : CharResultSource;

        foreach (var line in ConverterCodec.SplitLines(result, ResultLineBytes))
        {
            context.Ports.Publish(target, BuildLine(line));
        }

        return true;
    }

    /// <summary>把一行文本写成**定宽样本**（A5 显示源要求长度一致）。</summary>
    /// <param name="line">行文本。</param>
    /// <returns>样本字节。</returns>
    public static byte[] BuildLine(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        var sample = new byte[ResultLineBytes];
        bytes.AsSpan(0, Math.Min(bytes.Length, sample.Length)).CopyTo(sample);
        return sample;
    }

    private static bool IsCharSource(string source)
    {
        for (var i = 0; i < PageCount; i++)
        {
            if (string.Equals(source, CharSourceOf(i), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
}
