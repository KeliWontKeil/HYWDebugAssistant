using System.Text;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.HexStringConverter;

/// <summary>
/// **HEX ↔ 字符串转换器** —— **零口零角色**的离线小工具。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不交换数据</b>：**没有任何端口** ⇒ 不进拓扑、不参与建网；数据只走**视图输入 / 显示**通道
///     （契约层已核实：零口零角色**合法且不出建议**）；</item>
///   <item><b>两个方向各一组页签**（各 5 页，可存多条消息、**不持久化**）；
///     **没有按钮**：每拍轮询"当前选中页 + 该页当前文本"，一变就转 —— 打字、切页都算变化。
///     "所见即所转"靠**回读视图状态**（含尚未提交的编辑）实现，不依赖"用户按过回车"；</item>
///   <item><b>结果写进显示源**（`IViewDisplayAccess.SetText`）：结果框是只读文本面，
///     与输入框走同一条"文本面"机制，而不是另一条二进制样本通道；</item>
///   <item><b>失败 ⇒ 只把该输入框的边框标红</b>（帧架只提供"设边框色"这个基础能力，颜色由本模块挑），
///     **不写任何文字提示** —— 这一格的"对错"用边框就够说清，不必再多一行字；</item>
///   <item><b>空输入 ⇒ 清掉结果、清掉标色</b>（静默：用户还没写完不算错）。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "hex-string-converter",
    DisplayNameKey = HexStringConverterText.Title,
    DescriptionKey = HexStringConverterText.Description,
    Tags = ["hex", "tool"],
    ParticipatesInDataFlow = false,
    WindowWidth = 400,
    WindowHeight = 298,
    ViewFactory = typeof(Views.HexStringConverterViewFactory))]
public sealed class HexStringConverterModule : IModule, IActivitySource
{
    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>char 组的标签容器状态键（回读"当前选中页"）。</summary>
    public const string CharPagesKey = "char-pages";

    /// <summary>hex 组的标签容器状态键。</summary>
    public const string HexPagesKey = "hex-pages";

    /// <summary>
    /// 每组页数。
    /// </summary>
    /// <remarks>页数取 5（声明里两个 `tabs` 各 5 页）；视图元素数量不设上限。</remarks>
    public const int PageCount = 5;

    /// <summary>第 1 页 char 输入源名。</summary>
    public const string Char1 = "char1";

    /// <summary>第 2 页 char 输入源名。</summary>
    public const string Char2 = "char2";

    /// <summary>第 3 页 char 输入源名。</summary>
    public const string Char3 = "char3";

    /// <summary>第 4 页 char 输入源名。</summary>
    public const string Char4 = "char4";

    /// <summary>第 5 页 char 输入源名。</summary>
    public const string Char5 = "char5";

    /// <summary>第 1 页 hex 输入源名。</summary>
    public const string Hex1 = "hex1";

    /// <summary>第 2 页 hex 输入源名。</summary>
    public const string Hex2 = "hex2";

    /// <summary>第 3 页 hex 输入源名。</summary>
    public const string Hex3 = "hex3";

    /// <summary>第 4 页 hex 输入源名。</summary>
    public const string Hex4 = "hex4";

    /// <summary>第 5 页 hex 输入源名。</summary>
    public const string Hex5 = "hex5";

    /// <summary>第 n 页 char 输入源名（<c>char1</c> … <c>char5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界回落到第 1 页）。</returns>
    public static string CharSourceOf(int page) => Math.Clamp(page, 0, PageCount - 1) switch
    {
        0 => Char1,
        1 => Char2,
        2 => Char3,
        3 => Char4,
        _ => Char5,
    };

    /// <summary>第 n 页 hex 输入源名（<c>hex1</c> … <c>hex5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界回落到第 1 页）。</returns>
    public static string HexSourceOf(int page) => Math.Clamp(page, 0, PageCount - 1) switch
    {
        0 => Hex1,
        1 => Hex2,
        2 => Hex3,
        3 => Hex4,
        _ => Hex5,
    };

    /// <summary>显示源：char→HEX 的结果区。</summary>
    public const string HexResultSource = "hexResult";

    /// <summary>显示源：HEX→char 的结果区。</summary>
    public const string CharResultSource = "charResult";

    /// <summary>
    /// 模块自己挑的**提示角色**（**相对色**）—— 框架只提供"设边框色"这个能力，不认识"红 = 失败"。
    /// </summary>
    /// <remarks>
    /// 值只在这一处；用的是**配色表里的角色词**，不是色值 ⇒ 用户在设置里改那一支色、或换深浅主题，
    /// 这一圈跟着变。要换观感就换词。
    /// </remarks>
    public const string AlertRole = ViewInkRoles.Danger;

    /// <summary>活动周期（20 ms）——每拍轮询一次输入，用户打字与切页都及时跟上。</summary>
    public static readonly TimeSpan ActivityInterval = TimeSpan.FromMilliseconds(20);

    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // **零口零角色**：不声明数据类型、不声明口、不声明角色。
        // 但**界面契约照常要声明**（显示源与输入源）：它是"界面能读什么、能提交什么"的事实，
        // 与有没有数据口无关 —— 不声明的话界面既读不到值、提交也会被闸门拒掉。
        DeclareUi(context);
    }

    /// <summary>界面契约（显示源 / 输入源）：名字只在这里写一次。</summary>
    private static void DeclareUi(IModuleContext context)
    {
        for (var i = 0; i < PageCount; i++)
        {
            var charSource = CharSourceOf(i);
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(charSource, ViewDataSourceKind.Text));
            context.Declaration.DeclareInput(new InputDeclaration(charSource) { Persistence = InputPersistence.Persistent });

            var hexSource = HexSourceOf(i);
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(hexSource, ViewDataSourceKind.Text));
            context.Declaration.DeclareInput(new InputDeclaration(hexSource) { Persistence = InputPersistence.Persistent });
        }

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(HexResultSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(CharResultSource, ViewDataSourceKind.Text));
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

        // 本模块**没有端口**、也没有活动体自身的业务 —— 活动体的唯一职责是"看着输入变没变"。
        while (!context.StopSignal.IsStopRequested)
        {
            Poll(context);
            _ = context.Timing.WaitFor(ActivityInterval);
        }
    }

    // ---------------------------------------------------------------- ④ 业务

    /// <summary>最近一次字符→HEX 的结果（测试可读）。</summary>
    public string? LastHexResult { get; private set; }

    /// <summary>最近一次 HEX→字符 的结果（测试可读）。</summary>
    public string? LastCharResult { get; private set; }

    /// <summary>最近一次失败原因（成功 / 空输入为 <see langword="null"/>）。</summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// **屏幕上那份结果现在属于哪一格、哪段文本**（按方向记：<see langword="true"/> = char → HEX）。
    /// </summary>
    /// <remarks>
    /// 结果面是**每个方向一份**的共享面：它摆在当前选中页下面，切页之后就不再对应当前页。
    /// 判据因此落在"输出属于谁"上，而不是"这一格转过什么"——后者会在切回某页时判成"没变化"，
    /// 让输出停在另一页的结果上。
    /// </remarks>
    private readonly Dictionary<bool, (string Source, string Text)> _published = new();

    /// <summary>
    /// **看一眼两个方向、该转就转**（活动体的单步；模块测试可直接调用）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <remarks>
    /// 每拍调一次。判据是"**输出现在属于哪一格、哪段文本**"：切页（选中源变了）或该页文本变了
    /// ⇒ 重算并重写结果面；两者都不变 ⇒ 一个字都不动（静止时零开销，也避免每拍重写显示缓冲）。
    /// </remarks>
    public void Poll(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Refresh(context, toHex: true);
        Refresh(context, toHex: false);
    }

    /// <summary>对**一个方向**做一次"输出该不该重写"的结算。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="toHex">true = char → HEX；false = HEX → char。</param>
    private void Refresh(IModuleContext context, bool toHex)
    {
        var page = context.ViewState.GetSelectedPage(toHex ? CharPagesKey : HexPagesKey) ?? 0;
        var source = toHex ? CharSourceOf(page) : HexSourceOf(page);
        var text = context.ViewState.GetInputText(source) ?? string.Empty;

        if (_published.TryGetValue(toHex, out var shown)
            && string.Equals(shown.Source, source, StringComparison.Ordinal)
            && string.Equals(shown.Text, text, StringComparison.Ordinal))
        {
            return;   // 输出已经属于这一格、这段文本 ⇒ 不重算、不重写
        }

        _published[toHex] = (source, text);

        Convert(context, text, toHex, source);
    }

    /// <summary>转换一段文本（**模块级测试可直接调用**，无需线程）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="text">输入文本。</param>
    /// <param name="toHex">true = char → HEX；false = HEX → char。</param>
    /// <param name="source">输入源名（失败时把边框色打在它身上）。</param>
    /// <returns>是否产出了结果。</returns>
    public bool Convert(IModuleContext context, string? text, bool toHex, string source)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ok = toHex
            ? ConverterCodec.TryToHex(text, out var result, out var failure)
            : ConverterCodec.TryToText(text, out result, out failure);

        if (!ok)
        {
            // **失败 ⇒ 清掉结果 + 只把这一格的边框标红**（不写文字提示：这一格的对错，边框说清就够）。
            // 为什么**清掉**而不是留着上一次的结果：留着会让屏幕上同时有"新输入 + 旧结果"，
            // 而旧结果看起来像是这次算出来的 —— 那是最容易骗到自己的一种状态。
            LastFailure = failure;
            PublishResult(context, toHex, null);
            context.Appearance.SetOutlineRole(source, AlertRole);
            return false;
        }

        LastFailure = null;

        if (result.Length == 0)
        {
            // **空输入 ⇒ 静默**：清掉结果、清掉标色（用户还没写完不算错）。
            PublishResult(context, toHex, null);
            context.Appearance.SetOutlineColor(source, null);
            return false;
        }

        PublishResult(context, toHex, result);

        // **成功 ⇒ 清掉标色**（回到主题给的边框）。
        context.Appearance.SetOutlineColor(source, null);
        return true;
    }

    /// <summary>把结果落到**界面**（显示缓冲）—— 与输入框同一条"文本面"机制。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="toHex">true = 写 HEX 结果区；false = 写字符结果区。</param>
    /// <param name="result">结果文本；<see langword="null"/> / 空 ⇒ 清空该结果区。</param>
    /// <remarks>
    /// 结果框是**只读文本面**，它读的是**显示缓冲**（`IViewDisplayAccess`）。
    /// 走别的通道（例如 `Ports.Publish` 那种二进制样本）结果永远到不了框里 —— 那正是"功能看起来全失效"的成因。
    /// </remarks>
    public void PublishResult(IModuleContext context, bool toHex, string? result)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (toHex)
        {
            LastHexResult = result;
        }
        else
        {
            LastCharResult = result;
        }

        context.Display.SetText(toHex ? HexResultSource : CharResultSource, result ?? string.Empty);
    }
}




