using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// 视图元素视图模型（**按模块声明生成**；模块不提供 View 类型，§5.3）。
/// </summary>
/// <remarks>
/// 元素目录取自 SDK 冻结目录；未渲染的元素**明确显示"未渲染"**（不假装支持）。
/// </remarks>
public abstract partial class ViewElementViewModel : ViewModelBase
{
    /// <summary>创建元素视图模型。</summary>
    /// <param name="kind">元素类型。</param>
    /// <param name="title">标题（可为空）。</param>
    /// <param name="dataSourceName">绑定的数据源名（可为空）。</param>
    protected ViewElementViewModel(ViewElementKind kind, string? title, string? dataSourceName)
    {
        Kind = kind;
        Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle(kind) : title;
        DataSourceName = dataSourceName;
    }

    /// <summary>元素类型。</summary>
    public ViewElementKind Kind { get; }

    /// <summary>显示标题。</summary>
    public string Title { get; }

    /// <summary>绑定的数据源名（可为空）。</summary>
    public string? DataSourceName { get; }

    private static string DefaultTitle(ViewElementKind kind) => kind switch
    {
        ViewElementKind.Numeric => "数值",
        ViewElementKind.StatusLight => "状态",
        ViewElementKind.Curve => "曲线",
        ViewElementKind.Table => "表格",
        ViewElementKind.TextLog => "日志",
        ViewElementKind.HexView => "字节",
        ViewElementKind.Gauge => "仪表",
        ViewElementKind.Image => "图像",
        ViewElementKind.ConfigurationEditor => "配置",
        ViewElementKind.CommandButton => "命令",
        ViewElementKind.ToggleSlider => "开关",
        ViewElementKind.ToggleSwitch => "开关滑块",
        ViewElementKind.EnumSelector => "选择",
        ViewElementKind.TextInput => "输入",
        ViewElementKind.FileSelector => "文件",
        ViewElementKind.Group => "分组",
        ViewElementKind.TabPages => "标签页",
        ViewElementKind.Collapsible => "折叠",
        ViewElementKind.Scroll => "滚动",
        _ => kind.ToString(),
    };
}

/// <summary>订阅类元素（绑定一个声明数据源）。</summary>
public abstract partial class SubscriptionElementViewModel : ViewElementViewModel
{
    /// <summary>创建订阅类元素视图模型。</summary>
    /// <param name="kind">元素类型。</param>
    /// <param name="title">标题。</param>
    /// <param name="dataSource">数据源声明。</param>
    protected SubscriptionElementViewModel(ViewElementKind kind, string? title, ViewDataSource? dataSource)
        : base(kind, title, dataSource?.Name)
    {
        DataSource = dataSource;
    }

    /// <summary>数据源声明（单位/格式/原语）。</summary>
    public ViewDataSource? DataSource { get; }

    /// <summary>单位文本（显示用）。</summary>
    public string? Unit => DataSource?.Unit;

    /// <summary>格式化串（如 F2）。</summary>
    public string? Format => DataSource?.Format;
}

/// <summary>
/// **B2 命令按钮**【M1-16 / ADR-0040 D1】：点击即把命令**投递**到模块线程执行。
/// </summary>
/// <remarks>
/// 反馈闭环：投递结果（含"未声明命令"这类拒绝原因）由调用方经状态栏提示，**不静默**。
/// </remarks>
public sealed partial class CommandElementViewModel : ViewElementViewModel
{
    private readonly Func<string, CommandResult>? _invoke;

    /// <summary>创建命令按钮视图模型。</summary>
    /// <param name="command">命令声明。</param>
    /// <param name="invoke">投递回调（返回结果，供调用方提示）。</param>
    public CommandElementViewModel(ViewCommand command, Func<string, CommandResult>? invoke = null)
        : base(ViewElementKind.CommandButton, command.Title, command.Name)
    {
        ArgumentNullException.ThrowIfNull(command);
        Command = command;
        _invoke = invoke;
    }

    /// <summary>命令声明。</summary>
    public ViewCommand Command { get; }

    /// <summary>工具提示（说明文本，缺省给一句通用说明）。</summary>
    public string Tooltip => string.IsNullOrWhiteSpace(Command.Description)
        ? "投递到模块执行（在模块自己的线程上）"
        : Command.Description!;

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

        var result = invoke(Command.Name);
        LastResult = result.Succeeded
            ? result.Notices.Count > 0 ? result.Notices[^1] : "已投递"
            : $"未生效：{string.Join("；", result.Failures)}";
    }
}

/// <summary>
/// **B5 文本输入**【M1-16 / ADR-0040 D2】：单行 = 回车 / 失焦时**一次提交**（不逐字符写入）。
/// </summary>
/// <remarks>
/// <b>多行（G2 · 2026-09）</b>：元素声明 <see cref="ViewElement.Multiline"/> = true 时渲染为多行框，
/// 提交键由**应用级设置** <c>multilineSubmitKey</c> 决定（**不属模块声明** —— 用户裁决"不同用户习惯不一样"）：
/// <c>CtrlEnter</c>（默认：回车换行 / Ctrl+Enter 提交 / 失焦不提交）· <c>Enter</c>（回车提交 / Shift+Enter 换行）·
/// <c>None</c>（键盘都不提交）。**单行行为不变**。
/// </remarks>
public sealed partial class TextInputElementViewModel : ViewElementViewModel
{
    private readonly Func<string, string, CommandResult>? _submit;
    private readonly IViewStateAccess? _viewState;
    private readonly string _instanceId;

    /// <summary>创建文本输入视图模型。</summary>
    /// <param name="element">元素声明（<c>DataSource</c> = 输入源名；<c>Multiline</c> = 是否多行）。</param>
    /// <param name="submit">提交回调（源名 + 文本 → 结果）。</param>
    /// <param name="multilineSubmitKey">多行提交键档位（应用级设置；默认 <c>CtrlEnter</c>）。</param>
    /// <param name="dataSource">
    /// 绑定的数据源声明（2026-09 增加：**只为取"框外单位标签"** —— `ViewDataSource.Unit`）。
    /// 可为 <see langword="null"/>（数据源未声明时不影响其余行为）。
    /// </param>
    /// <param name="viewState">
    /// **视图状态存取面**〔2026-09 · `decisions.md` 7.28〕：输入框的"当前文本"落在框架侧存储里 ⇒
    /// **元素表重建（切页 / 改设置）后文本还在**（对齐 WinForms"控件活着，数据就在"）；
    /// 模块也能经 `IModuleContext.ViewState.GetInputText` 回读到**未提交**的内容。
    /// `null`（设计期 / 旧调用点）⇒ 只留在本地（今天的行为）。
    /// </param>
    /// <param name="instanceId">实例 ID（写视图状态时的归属；<paramref name="viewState"/> 为 null 时忽略）。</param>
    public TextInputElementViewModel(
        ViewElement element,
        Func<string, string, CommandResult>? submit = null,
        string? multilineSubmitKey = null,
        ViewDataSource? dataSource = null,
        IViewStateAccess? viewState = null,
        string? instanceId = null)
        : base(ViewElementKind.TextInput, element.Title, element.DataSource)
    {
        ArgumentNullException.ThrowIfNull(element);
        Source = element.DataSource ?? string.Empty;
        IsMultiline = element.Multiline;
        InputKind = element.InputKind;
        Alignment = element.Alignment;
        Unit = string.IsNullOrWhiteSpace(dataSource?.Unit) ? null : dataSource.Unit;
        _submit = submit;
        _viewState = viewState;
        _instanceId = instanceId ?? string.Empty;

        // 【2026-09 · 7.28】**恢复**用户此前（尚未提交）的编辑内容 —— 修掉"切页/重建即丢编辑"。
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

        // 单行：失焦即提交（ADR-0040 D-5）。多行且提交键为 Ctrl+Enter 时**不**在失焦提交 ——
        // 否则"点模块的发送按钮 ⇒ 先失焦提交一次 ⇒ 按钮点击又提交一次"会**双发**。
        SubmitOnLostFocus = !IsMultiline || string.Equals(key, "Enter", StringComparison.Ordinal);
    }

    /// <summary>多行提交键的默认档位（与 <c>ApplicationSettings.MultilineSubmitKey</c> 的默认值一致）。</summary>
    public const string DefaultSubmitKey = "CtrlEnter";

    /// <summary>是否多行输入（元素声明的 <c>Multiline</c>；视图据此渲染多行框）。</summary>
    public bool IsMultiline { get; }

    /// <summary>
    /// 元素的**文本类型**（`Text` / `Number` / `Hex`）—— 视图据此**只放行合法字符**
    /// （数字写法 / 十六进制 + 空白排版）。
    /// </summary>
    /// <remarks>
    /// <b>框架只做格式判定</b>：范围、字节分组与业务合法性由**模块**用输入反馈通道回报（红框）——
    /// 与"可声明 ≠ 可判定"同一纪律（`Sdk/Views/ViewDeclaration.cs` 的 `InputKind` 注释）。
    /// </remarks>
    public TextInputKind InputKind { get; }

    /// <summary>是否**数值输入**（`InputKind = Number`）。</summary>
    public bool IsNumeric => InputKind == TextInputKind.Number;

    /// <summary>是否**十六进制输入**（`InputKind = Hex`）—— 只放行 `0-9 A-F a-f` 与空白。</summary>
    public bool IsHex => InputKind == TextInputKind.Hex;

    /// <summary>元素声明的**对齐**（当前只有文本输入消费；`Default` = 跟随框架默认）。</summary>
    public ViewAlignment Alignment { get; }

    /// <summary>是否右对齐（元素声明 <c>Alignment = Right</c>）。</summary>
    public bool IsAlignRight => Alignment == ViewAlignment.Right;

    /// <summary>是否居中（元素声明 <c>Alignment = Center</c>）。</summary>
    public bool IsAlignCenter => Alignment == ViewAlignment.Center;

    /// <summary>是否显式左对齐（元素声明 <c>Alignment = Left</c>）。</summary>
    public bool IsAlignLeft => Alignment == ViewAlignment.Left;

    /// <summary>
    /// **框外单位标签**（取自数据源声明的 <c>Unit</c>；空 = 不显示）。
    /// </summary>
    /// <remarks>
    /// 用户 2026-09 裁定：**不做框内单位后缀**（"容易给人一种'这里单位可以改'的错觉"），
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
    /// 【2026-09 用户裁定】**标记必须是不可编辑的装饰**（用户："我更倾向于自绘层"）：可编辑文本框里塞显示字符
    /// 会连带接管光标（实测"光标变化规律难以预测"，已被用户否定）⇒ 改为
    /// <see cref="HYWDebugAssistant.Ui.Controls.NewlineMarkOverlay"/> 在**同格的只读绘制层**里画 `↵`
    /// （光标跳过、删不掉、不参与选择，与 Word 的 ¶ 同构）。
    /// <para>
    /// **只有多行 + 纯文本**输入才画：十六进制输入的换行是**排版**（用户既有裁定"HEX 的换行不作为字符"）
    /// ⇒ 标 `↵` 反而误导；单行框里没有换行。
    /// </para>
    /// </remarks>
    public bool ShowsNewlineMarks => IsMultiline && InputKind == TextInputKind.Text;

    /// <summary>最近一次提交结果（失败就地显示）。</summary>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

    /// <summary>
    /// **处理失败态**（G3）：模块经 <c>IModuleContext.InputFeedback</c> 回报"这次输入没处理好"⇒ 视图渲染**红框**。
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

        // 【2026-09 · 7.28】"当前文本"写进视图状态存储（含**未提交**内容）⇒ ① 重建后恢复
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

        // 【2026-09 缺陷修复 · 用户实测"输入了内容反而发不了"】成功时**不显示任何信息**：
        // 此前把框架的提示（"已写入输入「interval」= 100 → 文本发送器"）写进这一行，而这一行按设计是
        // **失败色**（红）⇒ 用户把"成功"读成了"失败"（截图即此）。按 D4 裁定：**没有特殊问题就不显示这行信息**；
        // 框架提示的落点是**外壳状态栏**（那里已经是灰色正文），不在这里重复。
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
/// **A10 开关**【M1-16 / ADR-0040 D3】：拨动即提交一次（true/false），**松手写入**。
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
    /// <param name="element">元素声明（<c>DataSource</c> = 输入源名）。</param>
    /// <param name="submit">提交回调（源名 + 文本 → 结果）。</param>
    /// <param name="viewState">
    /// **视图状态存取面**〔2026-09 缺陷修复〕：开关的"当前值"落在这里 ⇒ **元素表重建（切页 / 改设置 / 重建窗口）后
    /// 开关还停在用户上次拨到的位置**。此前不回读 ⇒ 切页后开关**弹回默认的"关"**，而模块里的标志**还是"开"**
    /// ⇒ **UI 与行为不符**（用户实测："切换页面的时候连续发送开关弹回去了，但继续发当前页面内容"）。
    /// </param>
    /// <param name="instanceId">实例 ID（写视图状态时的归属）。</param>
    public ToggleElementViewModel(
        ViewElement element,
        Func<string, string, CommandResult>? submit = null,
        IViewStateAccess? viewState = null,
        string? instanceId = null)
        : base(element.Kind, element.Title, element.DataSource)
    {
        ArgumentNullException.ThrowIfNull(element);
        Source = element.DataSource ?? string.Empty;
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
    /// 两者**语义完全相同**（值都是 `true` / `false`），只有外观不同（用户 2026-09 裁定新增滑块）
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

/// <summary>A1 数值：最新值 + 单位（等宽字体，09 §3）。</summary>
public sealed partial class ValueElementViewModel : SubscriptionElementViewModel{
    /// <summary>创建数值元素视图模型。</summary>
    /// <param name="title">标题。</param>
    /// <param name="dataSource">数据源声明。</param>
    public ValueElementViewModel(string? title, ViewDataSource? dataSource)
        : base(ViewElementKind.Numeric, title, dataSource)
    {
    }

    /// <summary>当前值（尚未收到数据时为 null）。</summary>
    [ObservableProperty]
    public partial double? Value { get; set; }

    /// <summary>显示文本（按格式串格式化；无数据时为 "—"）。</summary>
    [ObservableProperty]
    public partial string Display { get; set; } = "—";

    /// <summary>更新数值。</summary>
    public void Update(double? value)
    {
        Value = value;
        Display = value is null
            ? "—"
            : value.Value.ToString(Format ?? "0.###", CultureInfo.CurrentCulture);
    }
}

/// <summary>A3 曲线：环形订阅缓冲的抽样窗口（**自动量程**，不做固定刻度——数据优先，09 §1）。</summary>
public sealed partial class CurveElementViewModel : SubscriptionElementViewModel
{
    /// <summary>曲线最多绘制的点数（超出即在 UI 侧降采样，§4.3）。</summary>
    public const int MaxPoints = 256;

    /// <summary>创建曲线元素视图模型。</summary>
    /// <param name="title">标题。</param>
    /// <param name="dataSource">数据源声明。</param>
    public CurveElementViewModel(string? title, ViewDataSource? dataSource)
        : base(ViewElementKind.Curve, title, dataSource)
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
/// <b>2026-09 用户裁定</b>：一行多长由**文本框宽度**决定（自动折行）—— 不再有"定长样本"。
/// 显示区里的"条"由**通道**决定：`AppendText`（不换行 ⇒ 接在当前最后一条后面）/
/// `AppendLine`（通道换行 ⇒ 起一条新的）；数据里真有的换行**字符**原样保留在其中。
/// 显示**最近 N 条**（旧条滚出，不无限增长）。
/// </remarks>
public sealed partial class TextLogElementViewModel : SubscriptionElementViewModel
{
    /// <summary>
    /// **保留行数上限**（"不清空就一直在"的容量）—— 用户 2026-09 裁定 **20 000 行**（`decisions.md` 7.31）。
    /// </summary>
    /// <remarks>
    /// 语义对齐 WinForms 的 `TextBox`：**只增**，只有 <see cref="Clear"/> 才清；到顶**丢最旧**并累计
    /// <see cref="DroppedLines"/>（界面提示"已丢弃 N 行"，**不静默丢**）。缓冲**由框架持有**（视图状态存储），
    /// 模块可经 `IModuleContext.ViewState.GetDisplayLines` 回读**同一份**内容。
    /// </remarks>
    public const int MaxLines = DisplayBuffer.DefaultCapacity;

    private readonly DisplayBuffer _buffer;

    /// <summary>创建文本日志元素视图模型。</summary>
    /// <param name="title">标题。</param>
    /// <param name="dataSource">数据源声明。</param>
    /// <param name="buffer">
    /// **共享文本缓冲**（视图状态存储里那一份；`null` ⇒ 自建本地缓冲 —— 设计期 / 旧调用点行为不变）。
    /// </param>
    public TextLogElementViewModel(string? title, ViewDataSource? dataSource, DisplayBuffer? buffer = null)
        : base(ViewElementKind.TextLog, title, dataSource)
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
    /// **不再对外发事件**：A5 改成"只读文本框 + 分页窗口"后（2026-09 用户裁定 B 案），视图直接订阅
    /// <see cref="Lines"/> 的集合变化即可（追加 / 丢弃 / 清空都会冒泡），原先为"自动滚到底"设的
    /// <c>LineAppended</c> 已无用武之地 ⇒ 删掉，避免出现两个"有新内容"的信号源。
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
    /// 2026-09 用户裁定（WinForms 模型）："不开换行/时间戳时，追加数据应**直接追加在后面**" ⇒
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
/// B1 配置项编辑：**按配置 Schema 自动生成表单**（Q5-5 / `11` §4.2）——
/// 有 UI 模块的配置编辑就在模块视图内（C5-4）。
/// </summary>
public sealed partial class ConfigurationEditorElementViewModel : ViewElementViewModel
{
    private string? _instanceId;
    private Func<string, string, CommandResult>? _writer;
    private IRuntimeQuery? _query;

    /// <summary>创建配置编辑元素。</summary>
    public ConfigurationEditorElementViewModel()
        : base(ViewElementKind.ConfigurationEditor, "配置", dataSourceName: null)
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
    /// <param name="kind">元素类型。</param>
    /// <param name="title">标题。</param>
    public UnsupportedElementViewModel(ViewElementKind kind, string? title)
        : base(kind, title, dataSourceName: null)
    {
    }

    /// <summary>提示文本。</summary>
    public string Notice => $"「{Title}」尚未渲染（元素类型 {Kind}）";
}
