using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// 视图元素视图模型（**按模块声明生成**；模块不提供 View 类型）。
/// </summary>
/// <remarks>
/// 元素目录取自 SDK 冻结目录；未渲染的元素**明确显示"未渲染"**（不假装支持）。
/// 标题 / 几何 / 绑定名一律取自**声明节点**（<see cref="Node"/>）—— 渲染侧不另带缺省（缺省由 schema 补齐）。
/// </remarks>
public abstract partial class ViewElementViewModel : ViewModelBase
{
    /// <summary>创建元素视图模型。</summary>
    /// <param name="node">本元素在声明树里的节点（标题、绑定名与几何都取自它）。</param>
    protected ViewElementViewModel(ViewNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Node = node;
        Kind = ViewElementKindExtensions.Parse(node.Kind);
        Title = node.Title;
        DataSourceName = node.DataSource;
    }

    /// <summary>
    /// 本元素的**声明节点**（<c>view.json</c> 里这一个控件）。
    /// </summary>
    /// <remarks>
    /// **不是可在线调整的状态**：声明在开发模块阶段定好（尺寸口径 / 停靠 / 网格格子都在其中），
    /// 由渲染管线在加载时构造一次。
    /// </remarks>
    public ViewNode Node { get; }

    /// <summary>元素类型。</summary>
    public ViewElementKind Kind { get; }

    /// <summary>显示标题（= 声明里的 <c>title</c>；缺省由 schema 给出）。</summary>
    public string Title { get; }

    /// <summary>绑定的数据源名（可为空）。</summary>
    public string? DataSourceName { get; }

    /// <summary>
    /// **反馈来源**（`view.json` 的 `feedbackSource`）：把该输入源（如文件选择器的 `savePath`）的处理结果显示在
    /// 本元素的**角落信息区**；空 = 本元素不接别人的反馈。
    /// </summary>
    /// <remarks>目前只有文本日志有角落信息区（文本框左下角那一行）。属**加载时一次性声明**，不可在线调整。</remarks>
    public string FeedbackSource { get; internal set; } = string.Empty;

    /// <summary>
    /// 是否**就地**显示处理结果（`view.json` 的 `showInlineResult`，缺省 <see langword="true"/>）；
    /// <see langword="false"/> ⇒ 关掉本元素的结果行，改由 <see cref="FeedbackSource"/> 指定的角落去接。
    /// </summary>
    public bool ShowInlineResult { get; internal set; } = true;
}

/// <summary>
/// **容器类元素**（`Group` 等）：自己占一块区域，并把子元素**收在自己内部**排布。
/// </summary>
/// <remarks>
/// <para>
/// **容器不展平**：`Group` 渲染出分组框
/// （标题 + 边框 + 内层流式排布），子元素由**内层排布面板**摆放（与 WinForms 的 `GroupBox` 同一思想）。
/// </para>
/// <para>
/// **页节点是例外**：`TabPages` 的每个子节点按 WinForms `TabPage` 语义处理 —— 页节点**自身不渲染、不画框**，
/// 它只是"当前页内容的容器"（容器由标签控件提供）。因此内置模块把 `Group` 当页包装用的写法保持不变。
/// </para>
/// <para>
/// **填满性透传**：容器内部含填满型（文本日志 / 多行输入）时，容器自身也参与父层的余量分配 ——
/// 规则见 `ModuleElementPanel`。
/// </para>
/// </remarks>
public abstract class ContainerElementViewModel : ViewElementViewModel
{
    private readonly Action<bool>? _collapsedChanged;
    private bool _isExpanded;

    /// <summary>创建容器元素视图模型。</summary>
    /// <param name="node">容器在声明树里的节点（排布口径、留白、间距、网格规格都取自它）。</param>
    /// <param name="children">子元素（按声明顺序）。</param>
    /// <param name="isExpanded">初始展开态（仅 `Collapsible` 用；其余容器恒为 <see langword="true"/>）。</param>
    /// <param name="collapsedChanged">展开态变化回调（仅 `Collapsible` 用；UI 借此把折叠态交给视图状态存储）。</param>
    protected ContainerElementViewModel(
        ViewNode node,
        IEnumerable<ViewElementViewModel> children,
        bool isExpanded = true,
        Action<bool>? collapsedChanged = null)
        : base(node)
    {
        ArgumentNullException.ThrowIfNull(children);
        Children = new ObservableCollection<ViewElementViewModel>(children);
        _isExpanded = isExpanded;
        _collapsedChanged = collapsedChanged;
    }

    /// <summary>容器的子元素（顺序 = 声明顺序；容器内部有自己的一套排布）。</summary>
    public ObservableCollection<ViewElementViewModel> Children { get; }

    /// <summary>容器内部的排布模式（声明面已解析；缺省流式）。</summary>
    public ViewLayoutMode LayoutMode => ViewLayoutModeExtensions.Parse(Node.Layout, ViewLayoutMode.Flow);

    /// <summary>容器内部的越界处理（声明面已解析；缺省交给外层滚动）。</summary>
    public ViewLayoutOverflow Overflow => ViewLayoutOverflowExtensions.Parse(Node.Overflow, ViewLayoutOverflow.Scroll);

    /// <summary>容器内元素之间的间距（声明缺省由 schema 给出）。</summary>
    public double Spacing => Math.Max(0, Node.Spacing);

    /// <summary>容器内容四周留白（容器外壳的边框/标题厚度由模板自己报，不在此列）。</summary>
    public double Padding => Math.Max(0, Node.Padding);

    /// <summary>容器内部的网格列规格（空 = 未声明 ⇒ 单列满宽；仅网格模式用得上）。</summary>
    public IReadOnlyList<GridTrack>? Columns => Node.Columns.Count > 0 ? Node.Columns : null;

    /// <summary>容器内部的网格行规格（空 = 未声明 ⇒ 行数由元素推断、每行按内容）。</summary>
    public IReadOnlyList<GridTrack>? Rows => Node.Rows.Count > 0 ? Node.Rows : null;

    /// <summary>
    /// 容器内部的网格列间距（<see cref="double.NaN"/> = 用 <see cref="Spacing"/>）。
    /// </summary>
    /// <remarks>
    /// 面板用 <c>NaN</c> 表示"没单独声明间距"；声明面的 0 与"未声明"不可分，故按 0 处理成
    /// <c>NaN</c>（回到 <see cref="Spacing"/>）—— 见 `ModuleElementPanel.EffectiveColumnSpacing`。
    /// </remarks>
    public double ColumnSpacing => Node.ColumnSpacing > 0 ? Node.ColumnSpacing : double.NaN;

    /// <summary>容器内部的网格行间距（<see cref="double.NaN"/> = 用 <see cref="Spacing"/>）。</summary>
    public double RowSpacing => Node.RowSpacing > 0 ? Node.RowSpacing : double.NaN;

    /// <summary>
    /// 容器内容的边距（= 声明留白 + 标题与内容之间既有的间隔）。
    /// **带标题的容器**（分组框 / 折叠区）用默认值；**不带标题的排布容器**重写成纯留白。
    /// </summary>
    public virtual Thickness ContentMargin => new(Padding, Padding + TitleGap, Padding, Padding);

    /// <summary>标题与内容之间既有的间隔（DIP；与容器模板里的字面值一致）。</summary>
    public const double TitleGap = 4;

    /// <summary>
    /// 展开态（**双向绑定**：用户点折叠区的标题行 ⇒ 写回这里 ⇒ 回调把折叠态交给视图状态存储）。
    /// </summary>
    /// <remarks>
    /// 为什么折叠态必须存到外面：元素表**会整体重建**（切页 / 改设置 / 重新声明）
    /// —— 只挂在元素视图模型上会"重建即弹回展开"。视图状态**不进组合件、不落盘**（重启即展开）。
    /// </remarks>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                _collapsedChanged?.Invoke(!value);
            }
        }
    }
}

/// <summary>**分组容器**（WinForms `GroupBox` 思想）：标题 + 边框，不滚动，子元素在框内流式排布。</summary>
public sealed class GroupElementViewModel : ContainerElementViewModel
{
    /// <summary>创建分组容器。</summary>
    /// <param name="node">分组容器在声明树里的节点。</param>
    /// <param name="children">子元素（按声明顺序）。</param>
    public GroupElementViewModel(ViewNode node, IEnumerable<ViewElementViewModel> children)
        : base(node, children)
    {
    }
}

/// <summary>
/// **排布容器**（C4）：只负责排布（流式 / 网格 / 绝对坐标），**不带语义外壳**（分组框 / 页签 / 折叠区各由别的容器承担）。
/// </summary>
/// <remarks>
/// 它存在的理由：`grid` 这类排布本身可能要多一层嵌套（例如"左边一列控件、右边一片日志"），
/// 那层嵌套不该被迫画出一个分组框或标签条。
/// </remarks>
public sealed class LayoutContainerElementViewModel : ContainerElementViewModel
{
    /// <summary>创建排布容器。</summary>
    /// <param name="node">排布容器在声明树里的节点（排布键全在它身上）。</param>
    /// <param name="children">子元素（按声明顺序）。</param>
    public LayoutContainerElementViewModel(ViewNode node, IEnumerable<ViewElementViewModel> children)
        : base(node, children)
    {
    }

    /// <summary>内容边距 = 声明留白（**没有标题行**，故不加标题与内容之间的那段间隔）。</summary>
    public override Thickness ContentMargin => new(Padding);
}

/// <summary>**可折叠容器**（WinForms `Panel` + 展开按钮 思想）：收起时只剩标题行；展开态记在视图状态里。</summary>
public sealed class CollapsibleElementViewModel : ContainerElementViewModel
{
    /// <summary>创建可折叠容器。</summary>
    /// <param name="node">折叠容器在声明树里的节点（<c>expanded</c> 是它的声明缺省）。</param>
    /// <param name="children">子元素（按声明顺序）。</param>
    /// <param name="isExpanded">初始展开态（来自视图状态；未知 ⇒ 用声明缺省）。</param>
    /// <param name="collapsedChanged">折叠态变化回调（交给视图状态存储）。</param>
    public CollapsibleElementViewModel(
        ViewNode node,
        IEnumerable<ViewElementViewModel> children,
        bool isExpanded,
        Action<bool>? collapsedChanged)
        : base(node, children, isExpanded, collapsedChanged)
    {
    }
}

/// <summary>订阅类元素（绑定一个声明数据源）。</summary>
public abstract partial class SubscriptionElementViewModel : ViewElementViewModel
{
    /// <summary>创建订阅类元素视图模型。</summary>
    /// <param name="node">元素在声明树里的节点（绑定名取自 <c>data</c> 角色键）。</param>
    /// <param name="dataSource">数据源声明（单位/原语）。</param>
    protected SubscriptionElementViewModel(ViewNode node, ViewSourceDeclaration? dataSource)
        : base(node)
    {
        DataSource = dataSource;
    }

    /// <summary>数据源声明（单位/原语）。</summary>
    public ViewSourceDeclaration? DataSource { get; }

    /// <summary>单位文本（显示用）。</summary>
    public string? Unit => DataSource?.Unit;
}

/// <summary>
/// **B2 命令按钮**：点击即把命令**投递**到模块线程执行。
/// </summary>
/// <remarks>
/// 反馈闭环：投递结果（含"未声明命令"这类拒绝原因）由调用方经状态栏提示，**不静默**。
/// </remarks>
public sealed partial class CommandElementViewModel : ViewElementViewModel
{
    private readonly Func<string, CommandResult>? _invoke;

    /// <summary>创建命令按钮视图模型。</summary>
    /// <param name="node">命令按钮在声明树里的节点（<c>command</c> = 命令名，<c>title</c> = 按钮文案）。</param>
    /// <param name="invoke">投递回调（返回结果，供调用方提示）。</param>
    public CommandElementViewModel(ViewNode node, Func<string, CommandResult>? invoke = null)
        : base(node)
    {
        _invoke = invoke;
    }

    /// <summary>投递的命令名（= 声明里的 <c>command</c> 角色键）。</summary>
    public string CommandName => Node.Command ?? string.Empty;

    /// <summary>
    /// 工具提示：点下去会发生什么 + **技术命令名**（声明面没有描述键，命令名是模块作者唯一给出的辨识信息）。
    /// </summary>
    public string Tooltip => CommandName.Length > 0
        ? $"投递「{CommandName}」到模块执行（在模块自己的线程上）"
        : "投递到模块执行（在模块自己的线程上）";

    /// <summary>最近一次投递结果（失败原因就地显示；空 = 尚未点击）。</summary>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

    /// <summary>最近一次投递是否失败。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(LastResult) && LastResult!.StartsWith("未生效", StringComparison.Ordinal);

    partial void OnLastResultChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>点击按钮（由视图绑定调用）。</summary>
    [RelayCommand]
    private void Invoke()
    {
        if (_invoke is not { } invoke)
        {
            LastResult = "内部错误：未连接到运行时";
            return;
        }

        var result = invoke(CommandName);
        LastResult = result.Succeeded
            ? result.Notices.Count > 0 ? result.Notices[^1] : "已投递"
            : $"未生效：{string.Join("；", result.Failures)}";
    }
}

/// <summary>
/// **B5 文本输入**：单行 = 回车 / 失焦时**一次提交**（不逐字符写入）。
/// </summary>
/// <remarks>
/// <b>多行</b>：元素声明 <c>multiline</c> = true 时渲染为多行框，
/// 提交键由**应用级设置** <c>multilineSubmitKey</c> 决定（**不属模块声明** —— 各人习惯不一样）：
/// <c>CtrlEnter</c>（默认：回车换行 / Ctrl+Enter 提交 / 失焦不提交）· <c>Enter</c>（回车提交 / Shift+Enter 换行）·
/// <c>None</c>（键盘都不提交）。**单行行为不变**。
/// </remarks>
public sealed partial class TextInputElementViewModel : ViewElementViewModel
{
    private readonly Func<string, string, CommandResult>? _submit;
    private readonly IViewStateAccess? _viewState;
    private readonly string _instanceId;

    /// <summary>创建文本输入视图模型。</summary>
    /// <param name="node">元素声明节点（<c>input</c> = 输入源名；<c>multiline</c> = 是否多行）。</param>
    /// <param name="submit">提交回调（源名 + 文本 → 结果）。</param>
    /// <param name="multilineSubmitKey">多行提交键档位（应用级设置；默认 <c>CtrlEnter</c>）。</param>
    /// <param name="dataSource">
    /// 绑定的数据源声明（**只为取「框外单位标签」** —— `ViewSourceDeclaration.Unit`）。
    /// 可为 <see langword="null"/>（数据源未声明时不影响其余行为）。
    /// </param>
    /// <param name="viewState">
    /// **视图状态存取面**：输入框的"当前文本"落在框架侧存储里 ⇒
    /// **元素表重建（切页 / 改设置）后文本还在**（对齐 WinForms"控件活着，数据就在"）；
    /// 模块也能经 `IModuleContext.ViewState.GetInputText` 回读到**未提交**的内容。
    /// `null`（设计期 / 未接线的调用点）⇒ 只留在本地（重建即丢）。
    /// </param>
    /// <param name="instanceId">实例 ID（写视图状态时的归属；<paramref name="viewState"/> 为 null 时忽略）。</param>
    public TextInputElementViewModel(
        ViewNode node,
        Func<string, string, CommandResult>? submit = null,
        string? multilineSubmitKey = null,
        ViewSourceDeclaration? dataSource = null,
        IViewStateAccess? viewState = null,
        string? instanceId = null)
        : base(node)
    {
        Source = node.Input ?? string.Empty;
        IsMultiline = node.Multiline;
        InputKind = node.InputKind;
        Alignment = node.TextAlign;
        Unit = string.IsNullOrWhiteSpace(dataSource?.Unit) ? null : dataSource.Unit;
        _submit = submit;
        _viewState = viewState;
        _instanceId = instanceId ?? string.Empty;

        // **恢复**尚未提交的编辑内容 —— 否则表现为"切页/重建即丢编辑"。
        if (_viewState is not null
            && _instanceId.Length > 0
            && Source.Length > 0
            && _viewState.GetInputText(_instanceId, Source) is { } restored)
        {
            Text = restored;
            IsDirty = restored.Length > 0;
        }

        // 未知/空档位 ⇒ 回落默认 `CtrlEnter`（与设置层的口径一致：损坏的文件不能让"回车与 Ctrl+回车都不提交"）
        var key = multilineSubmitKey is "Enter" or "None" ? multilineSubmitKey : DefaultSubmitKey;
        SubmitOnEnter = !IsMultiline || string.Equals(key, "Enter", StringComparison.Ordinal);
        SubmitOnCtrlEnter = !IsMultiline || string.Equals(key, "CtrlEnter", StringComparison.Ordinal);

        // 单行：失焦即提交。多行且提交键为 Ctrl+Enter 时**不**在失焦提交 ——
        // 否则"点模块的发送按钮 ⇒ 先失焦提交一次 ⇒ 按钮点击又提交一次"会**双发**。
        SubmitOnLostFocus = !IsMultiline || string.Equals(key, "Enter", StringComparison.Ordinal);
    }

    /// <summary>多行提交键的默认档位（与 <c>ApplicationSettings.MultilineSubmitKey</c> 的默认值一致）。</summary>
    public const string DefaultSubmitKey = "CtrlEnter";

    /// <summary>是否多行输入（元素声明的 <c>Multiline</c>；视图据此渲染多行框）。</summary>
    public bool IsMultiline { get; }

    /// <summary>
    /// 元素的**文本类型**（声明面的 `inputKind`：`text` / `number` / `hex`）—— 视图据此**只放行合法字符**
    /// （数字写法 / 十六进制 + 空白排版）。
    /// </summary>
    /// <remarks>
    /// <b>框架只做格式判定</b>：范围、字节分组与业务合法性由**模块**用输入反馈通道回报（红框）——
    /// 与"可声明 ≠ 可判定"同一纪律（见包声明 schema 的 `inputKind` 说明）。
    /// </remarks>
    public string InputKind { get; }

    /// <summary>文本类型取值：纯文本。</summary>
    public const string TextInputKind = "text";

    /// <summary>文本类型取值：数值。</summary>
    public const string NumberInputKind = "number";

    /// <summary>文本类型取值：十六进制。</summary>
    public const string HexInputKind = "hex";

    /// <summary>是否**数值输入**（<c>inputKind = number</c>）。</summary>
    public bool IsNumeric => string.Equals(InputKind, NumberInputKind, StringComparison.Ordinal);

    /// <summary>是否**十六进制输入**（<c>inputKind = hex</c>）—— 只放行 `0-9 A-F a-f` 与空白。</summary>
    public bool IsHex => string.Equals(InputKind, HexInputKind, StringComparison.Ordinal);

    /// <summary>元素声明的**对齐**（声明面的 `textAlign`：`default` / `left` / `center` / `right`）。</summary>
    public string Alignment { get; }

    /// <summary>是否右对齐（元素声明 <c>textAlign = right</c>）。</summary>
    public bool IsAlignRight => string.Equals(Alignment, "right", StringComparison.Ordinal);

    /// <summary>是否居中（元素声明 <c>textAlign = center</c>）。</summary>
    public bool IsAlignCenter => string.Equals(Alignment, "center", StringComparison.Ordinal);

    /// <summary>是否显式左对齐（元素声明 <c>textAlign = left</c>）。</summary>
    public bool IsAlignLeft => string.Equals(Alignment, "left", StringComparison.Ordinal);

    /// <summary>
    /// **框外单位标签**（取自数据源声明的 <c>Unit</c>；空 = 不显示）。
    /// </summary>
    /// <remarks>
    /// **不做框内单位后缀**（容易给人一种「这里单位可以改」的错觉），
    /// 单位用**文本框外部的标签**呈现。
    /// </remarks>
    public string? Unit { get; }

    /// <summary>回车是否**提交**（false ⇒ 回车交给输入框换行）。</summary>
    public bool SubmitOnEnter { get; }

    /// <summary>Ctrl+Enter 是否**提交**。</summary>
    public bool SubmitOnCtrlEnter { get; }

    /// <summary>失焦是否**提交**（多行 + CtrlEnter 档位下为 false，防双发）。</summary>
    public bool SubmitOnLostFocus { get; }

    /// <summary>多行编辑框的最小高度（DIP = 约三行）；单行时 0（= 不设最小值）。</summary>
    public double EditorMinHeight => IsMultiline ? MultilineEditorMinHeight : 0;

    /// <summary>多行编辑框最小高度取值（DIP）。</summary>
    public const double MultilineEditorMinHeight = 60;

    /// <summary>输入源名。</summary>
    public string Source { get; }

    /// <summary>
    /// **输入中的文本**（模块、提交、视图状态看到的**唯一**文本；**行末 `↵` 不在其中**）。
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// 本输入框是否要**画行末换行标记**（多行 + 纯文本）——标记由**绘制层**画，不进文本。
    /// </summary>
    /// <remarks>
    /// **标记必须是不可编辑的装饰**：可编辑文本框里塞显示字符
    /// 会连带接管光标（光标变化规律难以预测）⇒ 改为
    /// <see cref="HYWDebugAssistant.Ui.Controls.NewlineMarkOverlay"/> 在**同格的只读绘制层**里画 `↵`
    /// （光标跳过、删不掉、不参与选择，与 Word 的 ¶ 同构）。
    /// <para>
    /// **只有多行 + 纯文本**输入才画：十六进制输入的换行是**排版**（"HEX 的换行不作为字符"）
    /// ⇒ 标 `↵` 反而误导；单行框里没有换行。
    /// </para>
    /// </remarks>
    public bool ShowsNewlineMarks => IsMultiline && string.Equals(InputKind, TextInputKind, StringComparison.Ordinal);

    /// <summary>最近一次提交结果（失败就地显示）。</summary>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

    /// <summary>
    /// **处理失败态**：模块经 <c>IModuleContext.InputFeedback</c> 回报"这次输入没处理好"⇒ 视图渲染**红框**。
    /// </summary>
    /// <remarks>
    /// 由**框架**从查询面（`IRuntimeQuery.GetInputFeedback`）写入 —— 模块不持 UI，无法自己标红。
    /// 生命周期 = "**下一次提交即刷新**"（框架在投递入队时清旧结果）⇒ 这里只需跟着查询面走。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsInvalid { get; set; }

    /// <summary>是否有未提交的编辑（视图用于提示"未提交"）。</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    partial void OnTextChanged(string value)
    {
        IsDirty = true;

        // "当前文本"写进视图状态存储（含**未提交**内容）⇒ ① 重建后恢复
        // ② 模块可回读（点按钮时拿到用户此刻看到的文本 —— "所见即所发"）。
        if (_viewState is not null && _instanceId.Length > 0 && Source.Length > 0)
        {
            _viewState.SetInputText(_instanceId, Source, value);
        }
    }

    /// <summary>显示文本（用户编辑后）⇒ **真实文本**。</summary>
    /// <remarks>
    /// **用户敲进去的那一份以"剥掉标记后的真实文本"为准**（他可能只是删了标记、也可能删了换行本身）；
    /// 随后把显示**重新规范化**（标记回来），文本框据此写回并**由视图修正光标**（见 `ModuleContentView` 的
    /// <summary>提交当前文本（Enter / 失焦 / 按钮触发）。</summary>
    [RelayCommand]
    private void Commit()
    {
        if (_submit is not { } submit)
        {
            LastResult = "内部错误：未连接到运行时";
            return;
        }

        var result = submit(Source, Text);

        // 成功时**不显示任何信息**：这一行按设计是**失败色**（红），把框架提示
        // （"已写入输入 interval = 100 → 文本发送器"）写进这里会把"成功"读成"失败"。
        // **没有特殊问题就不显示这行信息**；框架提示的落点是**外壳状态栏**（那里已经是灰色正文），不在这里重复。
        LastResult = result.Succeeded
            ? null
            : $"未生效：{string.Join("；", result.Failures)}";

        if (result.Succeeded)
        {
            IsDirty = false;
        }
    }
}

/// <summary>
/// **A10 开关**：拨动即提交一次（true/false），**松手写入**。
/// </summary>
public sealed partial class ToggleElementViewModel : ViewElementViewModel
{
    private readonly Func<string, string, CommandResult>? _submit;
    private readonly IViewStateAccess? _viewState;
    private readonly string _instanceId;

    /// <summary>
    /// 正在**回读**上次的开关值（此时不得再投递一次：那会把"恢复 UI"变成"又下一次命令"）。
    /// </summary>
    /// <remarks>用 <see cref="Volatile"/> / <see cref="Interlocked"/> 读写（与发送器的"连续发送"标志同一写法）——
    /// 直接赋值的字段只在构造函数里被写过，会被只读分析器（IDE0044）点名。</remarks>
    private int _restoring;

    /// <summary>创建开关视图模型。</summary>
    /// <param name="node">元素声明节点（<c>input</c> = 输入源名；<c>kind</c> 决定滑块还是勾选框）。</param>
    /// <param name="submit">提交回调（源名 + 文本 → 结果）。</param>
    /// <param name="viewState">
    /// **视图状态存取面**：开关的"当前值"落在这里 ⇒ **元素表重建（切页 / 改设置 / 重建窗口）后
    /// 开关还停在用户上次拨到的位置**。不回读 ⇒ 切页后开关**弹回默认的"关"**，而模块里的标志**还是"开"**
    /// ⇒ **UI 与行为不符**（表现为"切换页面时开关弹回去，但仍在按开的状态发"）。
    /// </param>
    /// <param name="instanceId">实例 ID（写视图状态时的归属）。</param>
    public ToggleElementViewModel(
        ViewNode node,
        Func<string, string, CommandResult>? submit = null,
        IViewStateAccess? viewState = null,
        string? instanceId = null)
        : base(node)
    {
        Source = node.Input ?? string.Empty;
        _submit = submit;
        _viewState = viewState;
        _instanceId = instanceId ?? string.Empty;

        // 回读上次的开关值（与文本输入同一套机制：**不持久化到磁盘**，只在本次会话内跨元素表重建存活）
        if (_viewState is not null
            && _instanceId.Length > 0
            && Source.Length > 0
            && _viewState.GetInputText(_instanceId, Source) is { Length: > 0 } restored)
        {
            _ = Interlocked.Exchange(ref _restoring, 1);

            try
            {
                IsOn = string.Equals(restored, "true", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                _ = Interlocked.Exchange(ref _restoring, 0);
            }
        }
    }

    /// <summary>输入源名。</summary>
    public string Source { get; }

    /// <summary>
    /// **外观 = 开关式滑块**（B7 `ToggleSwitch`）还是**勾选框**（B3 `ToggleSlider`）。
    /// </summary>
    /// <remarks>
    /// 两者**语义完全相同**（值都是 `true` / `false`），只有外观不同
    /// ⇒ 共用同一个视图模型，模板里按本属性二选一渲染（**互斥可见**）。
    /// </remarks>
    public bool IsSwitch => Kind == ViewElementKind.ToggleSwitch;

    /// <summary>外观 = 勾选框（<see cref="IsSwitch"/> 的反面）。</summary>
    public bool IsCheckBox => !IsSwitch;

    /// <summary>开关状态。</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>最近一次提交结果。</summary>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        if (Volatile.Read(ref _restoring) == 1)
        {
            // 回读上次的值不算"用户拨了一下"：**这一道守卫只对本次赋值有效**（自清）
            _ = Interlocked.Exchange(ref _restoring, 0);
            return;
        }

        // 记下**用户拨到的位置**：元素表重建后据此恢复（否则开关弹回默认值 ⇒ UI 与行为不符）
        if (_viewState is not null && _instanceId.Length > 0 && Source.Length > 0)
        {
            _viewState.SetInputText(_instanceId, Source, value ? "true" : "false");
        }

        if (_submit is not { } submit)
        {
            LastResult = "内部错误：未连接到运行时";
            return;
        }

        var result = submit(Source, value ? "true" : "false");
        LastResult = result.Succeeded ? null : $"未生效：{string.Join("；", result.Failures)}";
    }
}

/// <summary>A1 数值：最新值 + 单位（等宽字体）。</summary>
public sealed partial class ValueElementViewModel : SubscriptionElementViewModel{
    /// <summary>创建数值元素视图模型。</summary>
    /// <param name="node">元素声明节点。</param>
    /// <param name="dataSource">数据源声明。</param>
    public ValueElementViewModel(ViewNode node, ViewSourceDeclaration? dataSource)
        : base(node, dataSource)
    {
    }

    /// <summary>当前值（尚未收到数据时为 null）。</summary>
    [ObservableProperty]
    public partial double? Value { get; set; }

    /// <summary>显示文本（按既有数字写法格式化；无数据时为 "—"）。</summary>
    /// <remarks>
    /// 声明面**没有**格式化串这一项（四个内置模块也不设）⇒ 固定用"最多三位小数"的数字写法；
    /// 要按模块自定义格式显示，得先在声明面加一个键（见迁移报告的待裁项）。
    /// </remarks>
    [ObservableProperty]
    public partial string Display { get; set; } = "—";

    /// <summary>数值显示的数字写法（"." = 必要的小数点，"#" = 最多三位小数）。</summary>
    public const string DisplayFormat = "0.###";

    /// <summary>更新数值。</summary>
    public void Update(double? value)
    {
        Value = value;
        Display = value is null
            ? "—"
            : value.Value.ToString(DisplayFormat, CultureInfo.CurrentCulture);
    }
}

/// <summary>A3 曲线：环形订阅缓冲的抽样窗口（**自动量程**，不做固定刻度——数据优先）。</summary>
public sealed partial class CurveElementViewModel : SubscriptionElementViewModel
{
    /// <summary>曲线最多绘制的点数（超出即在 UI 侧降采样）。</summary>
    public const int MaxPoints = 256;

    /// <summary>创建曲线元素视图模型。</summary>
    /// <param name="node">元素声明节点。</param>
    /// <param name="dataSource">数据源声明。</param>
    public CurveElementViewModel(ViewNode node, ViewSourceDeclaration? dataSource)
        : base(node, dataSource)
    {
    }

    /// <summary>当前窗口的点（时间升序）。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<double> Points { get; set; } = [];

    /// <summary>窗口内最小值。</summary>
    [ObservableProperty]
    public partial double Min { get; set; }

    /// <summary>窗口内最大值。</summary>
    [ObservableProperty]
    public partial double Max { get; set; }

    /// <summary>更新曲线窗口。</summary>
    public void Update(IReadOnlyList<double> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        Points = points;

        if (points.Count == 0)
        {
            Min = 0;
            Max = 0;
            return;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var point in points)
        {
            min = Math.Min(min, point);
            max = Math.Max(max, point);
        }

        // 全平数据给一个可见的窄带，避免除零
        if (Math.Abs(max - min) < double.Epsilon)
        {
            min -= 0.5;
            max += 0.5;
        }

        Min = min;
        Max = max;
    }
}

/// <summary>
/// A5 文本日志：**显示区的一条连续文本**（等宽；行/条由通道与数据决定，见下）。
/// </summary>
/// <remarks>
/// 一行多长由**文本框宽度**决定（自动折行）—— 没有"定长样本"。
/// 显示区里的"条"由**通道**决定：`AppendText`（不换行 ⇒ 接在当前最后一条后面）/
/// `AppendLine`（通道换行 ⇒ 起一条新的）；数据里真有的换行**字符**原样保留在其中。
/// 显示**最近 N 条**（旧条滚出，不无限增长）。
/// </remarks>
public sealed partial class TextLogElementViewModel : SubscriptionElementViewModel
{
    /// <summary>
    /// **保留行数上限**（"不清空就一直在"的容量）—— **20 000 行**。
    /// </summary>
    /// <remarks>
    /// 语义对齐 WinForms 的 `TextBox`：**只增**，只有 <see cref="Clear"/> 才清；到顶**丢最旧**并累计
    /// <see cref="DroppedLines"/>（界面提示"已丢弃 N 行"，**不静默丢**）。缓冲**由框架持有**（视图状态存储），
    /// 模块可经 `IModuleContext.ViewState.GetDisplayLines` 回读**同一份**内容。
    /// </remarks>
    public const int MaxLines = DisplayBuffer.DefaultCapacity;

    private readonly DisplayBuffer _buffer;

    /// <summary>创建文本日志元素视图模型。</summary>
    /// <param name="node">元素声明节点。</param>
    /// <param name="dataSource">数据源声明。</param>
    /// <param name="buffer">
    /// **共享文本缓冲**（视图状态存储里那一份；`null` ⇒ 自建本地缓冲 —— 设计期 / 未接线的调用点各自独立）。
    /// </param>
    public TextLogElementViewModel(ViewNode node, ViewSourceDeclaration? dataSource, DisplayBuffer? buffer = null)
        : base(node, dataSource)
    {
        _buffer = buffer ?? new DisplayBuffer();

        foreach (var line in _buffer.Snapshot())
        {
            _lines.Add(line);
            Lines.Add(line);
        }

        DroppedLines = _buffer.DroppedCount;

        _buffer.Appended += OnAppended;
        _buffer.TextAppended += OnTextAppended;
        _buffer.Trimmed += OnTrimmed;
        _buffer.Cleared += OnCleared;
    }

    private readonly List<string> _lines = [];

    /// <summary>共享文本缓冲（视图据此渲染**同一份**内容；模块回读的也是它）。</summary>
    public DisplayBuffer Buffer => _buffer;

    /// <summary>日志行（最新在最后；镜像自缓冲，供界面与测试读取）。</summary>
    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>因超出容量被丢弃的**旧行累计数**（界面据此提示"已丢弃 N 行"）。</summary>
    [ObservableProperty]
    public partial int DroppedLines { get; set; }

    /// <summary>丢弃提示文本（无丢弃 ⇒ <see langword="null"/> ⇒ 界面整块不显示）。</summary>
    public string? DroppedText => DroppedLines > 0
        ? $"已丢弃 {DroppedLines} 行（超出容量 {MaxLines} 行）"
        : null;

    /// <summary>
    /// **显示区左下角的反馈提示**。
    /// </summary>
    /// <remarks>
    /// 由框架在 <c>ModuleViewViewModel.ApplyInputFeedback</c> 里按 <see cref="ViewElementViewModel.FeedbackSource"/>
    /// 分派：把"保存到哪儿了 / 为什么没生效"这类结果**就近**显示在显示区左下角
    /// （结果紧挨着产生它的控件，不必去别处找）。
    /// </remarks>
    [ObservableProperty]
    public partial string? CornerStatus { get; set; }

    /// <summary>
    /// 左下角提示是否为**失败态**（失败 ⇒ 界面标红）。由框架按输入反馈的成功与否分派，不从文案推断 ——
    /// 模块自己的失败文案（如"保存失败：…"）原样显示，不套一层"未生效"前缀。
    /// </summary>
    [ObservableProperty]
    public partial bool CornerIsError { get; set; }

    partial void OnDroppedLinesChanged(int value) => OnPropertyChanged(nameof(DroppedText));

    /// <summary>缓冲保留的行数（= <see cref="Lines"/> 的条数）。</summary>
    public int Count => _lines.Count;

    /// <summary>追加一行（超出容量则由缓冲丢弃最旧）。</summary>
    /// <param name="line">行文本。</param>
    public void Append(string line) => _buffer.Append(line);

    /// <summary>清空（唯一会丢内容的入口；实例移除或数据源重置时）。</summary>
    public void Clear() => _buffer.Clear();

    /// <summary>全部保留行的快照（保存显示内容时用）。</summary>
    public IReadOnlyList<string> Snapshot() => _buffer.Snapshot();

    /// <summary>
    /// 缓冲追加了一行。
    /// </summary>
    /// <remarks>
    /// **不对外发事件**：A5 是"只读文本框 + 分页窗口"，视图直接订阅
    /// <see cref="Lines"/> 的集合变化即可（追加 / 丢弃 / 清空都会冒泡）；为"自动滚到底"另设一个
    /// <c>LineAppended</c> 会留下第二个"有新内容"的信号源 ⇒ 不设。
    /// </remarks>
    private void OnAppended(string line)
    {
        _lines.Add(line);
        Lines.Add(line);
    }

    /// <summary>
    /// **把内容接长**（不换行的那条路）：改的是**最后一条**，不是新增一条。
    /// </summary>
    /// <remarks>
    /// WinForms 模型：不开换行/时间戳时，追加数据**直接追加在后面** ⇒
    /// 显示区里"条"的个数可能一直不变、而最后一条一直在长。界面必须收到通知
    /// （窗口要重排、`↵` 判断要重来），否则表现为"数据来了但屏幕不动"。
    /// 用集合的**替换**（而非新增）表达，正好对应"还是那一行、内容变了"。
    /// </remarks>
    private void OnTextAppended(string text)
    {
        if (_lines.Count == 0)
        {
            _lines.Add(text);
            Lines.Add(text);
            return;
        }

        _lines[^1] = text;
        Lines[^1] = text;
    }

    private void OnTrimmed(int count)
    {
        var take = Math.Min(count, _lines.Count);

        for (var i = 0; i < take; i++)
        {
            _lines.RemoveAt(0);
            Lines.RemoveAt(0);
        }

        DroppedLines = _buffer.DroppedCount;
    }

    private void OnCleared()
    {
        _lines.Clear();
        Lines.Clear();
        DroppedLines = 0;
    }
}

/// <summary>
/// B1 配置项编辑：**按配置 Schema 自动生成表单**——
/// 有 UI 模块的配置编辑就在模块视图内。
/// </summary>
public sealed partial class ConfigurationEditorElementViewModel : ViewElementViewModel
{
    private string? _instanceId;
    private Func<string, string, CommandResult>? _writer;
    private IRuntimeQuery? _query;

    /// <summary>创建配置编辑元素。</summary>
    /// <param name="node">元素声明节点（标题、几何都取自它）。</param>
    public ConfigurationEditorElementViewModel(ViewNode node)
        : base(node)
    {
    }

    /// <summary>字段（按配置声明顺序；分组由字段的 <c>Group</c> 表达）。</summary>
    public ObservableCollection<ConfigurationFieldViewModel> Fields { get; } = [];

    /// <summary>不可编辑时的说明（空 = 可编辑）。</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    /// <summary>是否有字段可编辑。</summary>
    public bool HasFields => Fields.Count > 0;

    /// <summary>最近一次操作的结果文本（成功/失败提示）。</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>最近一次操作是否成功。</summary>
    [ObservableProperty]
    public partial bool StatusIsOk { get; set; } = true;

    /// <summary>按当前 Schema 重建字段（刷新节拍调用；就地编辑中的字段不覆盖）。</summary>
    /// <param name="query">查询面。</param>
    /// <param name="instanceId">实例 ID（默认由所属模块注入，见 <see cref="Attach"/>）。</param>
    public void Reload(IRuntimeQuery query, string? instanceId = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        var target = instanceId ?? _instanceId;
        if (string.IsNullOrWhiteSpace(target) || !query.TryGetConfigurationSchema(target, out var schema) || schema is null)
        {
            Notice = "无法读取配置 Schema";
            return;
        }

        Notice = schema.Notice;

        if (schema.Notice is { Length: > 0 })
        {
            return;
        }

        foreach (var field in schema.Fields)
        {
            var existing = Fields.FirstOrDefault(f => string.Equals(f.Name, field.Name, StringComparison.Ordinal));
            if (existing is null)
            {
                Fields.Add(ConfigurationFieldViewModel.From(field));
            }
            else
            {
                existing.Refresh(field);
            }
        }

        foreach (var stale in Fields
            .Where(f => !schema.Fields.Any(x => string.Equals(x.Name, f.Name, StringComparison.Ordinal)))
            .ToList())
        {
            _ = Fields.Remove(stale);
        }

        OnPropertyChanged(nameof(HasFields));
    }

    /// <summary>绑定所属实例与写回通道（模块视图构建元素时调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="writer">按字段写回（字段名 + 文本值 → 结果）。</param>
    public void Attach(string instanceId, Func<string, string, CommandResult> writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(writer);

        _instanceId = instanceId;
        _writer = writer;
    }

    /// <summary>
    /// 提交全部**改动过的**字段（一次点击只写真正变化的项；逐字段写回，任一失败即回报原因）。
    /// </summary>
    [RelayCommand]
    private void Apply()
    {
        if (_writer is null)
        {
            StatusIsOk = false;
            StatusMessage = "写回通道未就绪";
            return;
        }

        var dirty = Fields.Where(f => f.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusIsOk = true;
            StatusMessage = "没有改动";
            return;
        }

        var failures = new List<string>();
        var applied = 0;

        foreach (var field in dirty)
        {
            var result = _writer(field.Name, field.Value);
            if (result.Succeeded)
            {
                field.MarkClean();
                applied++;
            }
            else
            {
                failures.Add($"{field.Title}：{string.Join("；", result.Failures)}");
            }
        }

        if (failures.Count > 0)
        {
            StatusIsOk = false;
            StatusMessage = $"已应用 {applied} 项，失败 {failures.Count} 项 —— {string.Join(" / ", failures)}";
            return;
        }

        StatusIsOk = true;
        StatusMessage = $"已应用 {applied} 项";
    }

    /// <summary>放弃改动（重新读回当前值）。</summary>
    [RelayCommand]
    private void Revert()
    {
        foreach (var field in Fields)
        {
            field.MarkClean();
        }

        if (_query is { } query)
        {
            Reload(query);
        }

        StatusIsOk = true;
        StatusMessage = "已还原";
    }
}

/// <summary>配置表单中的一个字段（值就地编辑；提交时按字段写回）。</summary>
public sealed partial class ConfigurationFieldViewModel : ViewModelBase
{
    private ConfigurationFieldViewModel(ConfigurationFieldView field)
    {
        Name = field.Name;
        Kind = field.Kind;
        Update(field);
    }

    /// <summary>由 Schema 字段创建。</summary>
    /// <param name="field">Schema 字段。</param>
    public static ConfigurationFieldViewModel From(ConfigurationFieldView field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new ConfigurationFieldViewModel(field);
    }

    /// <summary>字段名（写回键）。</summary>
    public string Name { get; }

    /// <summary>编辑控件类型。</summary>
    public ConfigurationFieldKind Kind { get; }

    /// <summary>显示标题（模块标注优先）。</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>分组标题（空 = 不分组）。</summary>
    [ObservableProperty]
    public partial string? Group { get; set; }

    /// <summary>当前编辑值（文本形态）。</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>单位文本。</summary>
    [ObservableProperty]
    public partial string? Unit { get; set; }

    /// <summary>说明文本。</summary>
    [ObservableProperty]
    public partial string? Description { get; set; }

    /// <summary>最小/最大值提示文本（空 = 不限制）。</summary>
    [ObservableProperty]
    public partial string? RangeHint { get; set; }

    /// <summary>是否可用开关控件（布尔）。</summary>
    public bool IsBoolean => Kind == ConfigurationFieldKind.Boolean;

    /// <summary>是否可用下拉控件（枚举）。</summary>
    public bool IsEnum => Kind == ConfigurationFieldKind.Enum;

    /// <summary>
    /// 是否用文本输入（整数 / 实数 / 文本）。
    /// </summary>
    /// <remarks>
    /// **必须与 <see cref="IsEnum"/> 互斥**：曾是 <c>!IsBoolean</c>，导致枚举字段同时显示
    /// 下拉与文本框并**叠在同一个单元格里**（界面表现为"输入框重影"）。
    /// </remarks>
    public bool IsText => Kind is ConfigurationFieldKind.WholeNumber
        or ConfigurationFieldKind.Number
        or ConfigurationFieldKind.Text;

    /// <summary>布尔字段的当前勾选状态（与 <see cref="Value"/> 双向同步）。</summary>
    public bool IsChecked
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set
        {
            var text = value ? "true" : "false";
            if (!string.Equals(Value, text, StringComparison.Ordinal))
            {
                Value = text;
            }
        }
    }

    /// <summary>枚举可选项（标题 → 值）。</summary>
    public ObservableCollection<ConfigurationFieldChoice> Choices { get; } = [];

    /// <summary>枚举选中的项。</summary>
    [ObservableProperty]
    public partial ConfigurationFieldChoice? SelectedChoice { get; set; }

    /// <summary>该字段是否被用户改动过（只有改动过的字段才写回 → 一次"应用"只提交真正变化的项）。</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>刷新为最新 Schema 值（就地编辑中不覆盖用户输入）。</summary>
    /// <param name="field">最新 Schema 字段。</param>
    public void Refresh(ConfigurationFieldView field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Update(field);
    }

    /// <summary>标记为已提交（清除改动标记）。</summary>
    public void MarkClean() => IsDirty = false;

    private void Update(ConfigurationFieldView field)
    {
        Title = field.Title;
        Group = field.Group;
        Unit = field.Unit;
        Description = field.Description;
        RangeHint = BuildRangeHint(field);

        if (IsEnum)
        {
            Choices.Clear();
            foreach (var choice in field.Choices ?? [])
            {
                Choices.Add(choice);
            }

            SelectedChoice = Choices.FirstOrDefault(c => string.Equals(c.Value, field.Value, StringComparison.Ordinal));
        }

        if (!IsDirty)
        {
            Value = field.Value;
        }
    }

    private static string? BuildRangeHint(ConfigurationFieldView field)
    {
        if (field.Minimum is null && field.Maximum is null)
        {
            return null;
        }

        var min = field.Minimum?.ToString("0.###", CultureInfo.InvariantCulture) ?? "−∞";
        var max = field.Maximum?.ToString("0.###", CultureInfo.InvariantCulture) ?? "+∞";
        return $"{min} ~ {max}";
    }

    partial void OnValueChanged(string value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsChecked));
    }

    partial void OnSelectedChoiceChanged(ConfigurationFieldChoice? value)
    {
        if (value is not null)
        {
            Value = value.Value;
        }
    }
}

/// <summary>未渲染的元素（**明确显示"未渲染"**，不假装支持）。</summary>
public sealed partial class UnsupportedElementViewModel : ViewElementViewModel
{
    /// <summary>创建占位元素视图模型。</summary>
    /// <param name="node">元素声明节点（类型与标题都取自它）。</param>
    public UnsupportedElementViewModel(ViewNode node)
        : base(node)
    {
    }

    /// <summary>提示文本。</summary>
    public string Notice => $"「{Title}」尚未渲染（元素类型 {Node.Kind}）";
}
