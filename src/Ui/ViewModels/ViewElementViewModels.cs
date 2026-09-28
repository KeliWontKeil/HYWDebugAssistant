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

    /// <summary>创建文本输入视图模型。</summary>
    /// <param name="element">元素声明（<c>DataSource</c> = 输入源名；<c>Multiline</c> = 是否多行）。</param>
    /// <param name="submit">提交回调（源名 + 文本 → 结果）。</param>
    /// <param name="multilineSubmitKey">多行提交键档位（应用级设置；默认 <c>CtrlEnter</c>）。</param>
    public TextInputElementViewModel(
        ViewElement element,
        Func<string, string, CommandResult>? submit = null,
        string? multilineSubmitKey = null)
        : base(ViewElementKind.TextInput, element.Title, element.DataSource)
    {
        ArgumentNullException.ThrowIfNull(element);
        Source = element.DataSource ?? string.Empty;
        IsMultiline = element.Multiline;
        _submit = submit;

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

    /// <summary>输入中的文本（**未提交**）。</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

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

    partial void OnTextChanged(string value) => IsDirty = true;

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
        LastResult = result.Succeeded
            ? result.Notices.Count > 0 ? result.Notices[^1] : "已写入"
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

    /// <summary>创建开关视图模型。</summary>
    /// <param name="element">元素声明（<c>DataSource</c> = 输入源名）。</param>
    /// <param name="submit">提交回调（源名 + 文本 → 结果）。</param>
    public ToggleElementViewModel(ViewElement element, Func<string, string, CommandResult>? submit = null)
        : base(ViewElementKind.ToggleSlider, element.Title, element.DataSource)
    {
        ArgumentNullException.ThrowIfNull(element);
        Source = element.DataSource ?? string.Empty;
        _submit = submit;
    }

    /// <summary>输入源名。</summary>
    public string Source { get; }

    /// <summary>开关状态。</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>最近一次提交结果。</summary>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

    partial void OnIsOnChanged(bool value)
    {
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
/// A5 文本日志：**定长文本样本**解码成的行（等宽、带时间戳；§5.3）。
/// </summary>
/// <remarks>
/// 框架要求同一数据源的样本长度一致（先写入者定槽），故模块按定长发布日志行；
/// UI 解码后去掉尾部空白与 NUL。显示**最近 N 行**（旧行滚出，不无限增长）。
/// </remarks>
public sealed partial class TextLogElementViewModel : SubscriptionElementViewModel
{
    /// <summary>界面保留的最大行数（防无界增长；采样窗口的取舍，Gate C 可调）。</summary>
    public const int MaxLines = 200;

    private readonly List<string> _lines = new(MaxLines);

    /// <summary>创建文本日志元素视图模型。</summary>
    /// <param name="title">标题。</param>
    /// <param name="dataSource">数据源声明。</param>
    public TextLogElementViewModel(string? title, ViewDataSource? dataSource)
        : base(ViewElementKind.TextLog, title, dataSource)
    {
    }

    /// <summary>日志行（最新在最后；绑定到界面）。</summary>
    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>追加一行（超出上限则移除最旧）。</summary>
    /// <param name="line">行文本。</param>
    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _lines.Add(line);
        Lines.Add(line);

        while (_lines.Count > MaxLines)
        {
            _lines.RemoveAt(0);
            Lines.RemoveAt(0);
        }
    }

    /// <summary>清空（实例移除或数据源重置时）。</summary>
    public void Clear()
    {
        _lines.Clear();
        Lines.Clear();
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
