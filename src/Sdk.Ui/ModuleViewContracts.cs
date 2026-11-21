using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Ui;

/// <summary>
/// **模块视图工厂** —— 模块声明"我的窗口长什么样"的唯一入口。
/// </summary>
/// <remarks>
/// <para>
/// 由 `[ModuleDefinition(ViewFactory = typeof(…))]` 指到，框架按**每个实例**调用一次
/// <see cref="Create"/>。工厂类型需要公共无参构造、并且必须无状态（它可能被多个实例复用）。
/// </para>
/// <para>
/// 用**类型**而不是实例：模块对象本身有状态、受生命周期约束，视图不能造出它的第二份实例。
/// </para>
/// </remarks>
public interface IModuleViewFactory
{
    /// <summary>为一个实例造一次视图。</summary>
    /// <param name="context">这一份视图取数、提交与外观的入口。</param>
    /// <returns>视图根控件（模块自己负责它的内部布局）。</returns>
    Control Create(IModuleViewContext context);
}

/// <summary>
/// **一次命令调用的结论**（面向界面，不暴露框架内部结构）。
/// </summary>
/// <param name="IsError">是否失败。</param>
/// <param name="Message">可显示的一句话；空 = 没什么好说的。</param>
public readonly record struct CommandOutcome(bool IsError, string Message)
{
    /// <summary>成功（无可显示的话）。</summary>
    public static readonly CommandOutcome Ok = new(false, string.Empty);

    /// <summary>失败并给一句原因。</summary>
    /// <param name="message">原因。</param>
    public static CommandOutcome Fail(string message) => new(true, message);
}

/// <summary>配置字段的编辑器种类（界面只需这几种）。</summary>
public enum ConfigFieldKind
{
    /// <summary>布尔。</summary>
    Boolean = 0,

    /// <summary>整数。</summary>
    WholeNumber = 1,

    /// <summary>实数。</summary>
    Number = 2,

    /// <summary>文本。</summary>
    Text = 3,

    /// <summary>枚举。</summary>
    Enum = 4,
}

/// <summary>枚举字段的一个可选项。</summary>
/// <param name="Value">写回时的字面量。</param>
/// <param name="Title">显示文本。</param>
public sealed record ConfigChoice(string Value, string Title);

/// <summary>配置字段种类 → 值编辑器的**唯一映射**。</summary>
/// <remarks>
/// 生产呈现层、设计期上下文与值契约默认实现共用这一处，避免同一张表抄三份后漂移。
/// 字段编辑器按 <see cref="ConfigFieldKind"/> 选编辑器属于控件行为，不是界面描述面。
/// 文本准入格式属于视图输入行为，定义在 <c>ModuleTextSurface.TextFormat</c>，不在值声明里。
/// </remarks>
public static class ValueEditorMapping
{
    /// <summary>配置字段种类对应的值编辑器。</summary>
    /// <param name="kind">字段种类。</param>
    public static ValueEditor ToEditor(ConfigFieldKind kind) => kind switch
    {
        ConfigFieldKind.Boolean => ValueEditor.Boolean,
        ConfigFieldKind.WholeNumber or ConfigFieldKind.Number => ValueEditor.Number,
        ConfigFieldKind.Enum => ValueEditor.Enum,
        _ => ValueEditor.Text,
    };
}

/// <summary>控件把用户修改交出去的时机（纯操作行为，与值存在哪里无关）。</summary>
public enum ValueCommitMode
{
    /// <summary>每次修改即提交（开关、下拉、即时生效的文本）。</summary>
    OnChange = 0,

    /// <summary>回车 / 显式动作才提交（普通文本、数字）。</summary>
    OnExplicit = 1,
}

/// <summary>一个配置字段的呈现（"这一格怎么画、写回什么字面量"）。</summary>
/// <param name="Name">字段名（写回时的键）。</param>
/// <param name="Title">显示标题。</param>
/// <param name="Kind">编辑器种类。</param>
/// <param name="Value">当前值的文本形态。</param>
/// <param name="Choices">可选项（仅 <see cref="ConfigFieldKind.Enum"/>）。</param>
/// <param name="Unit">单位文本（空 = 无）。</param>
/// <param name="Description">说明文本（空 = 无；作悬停提示）。</param>
/// <param name="Group">分组标题（空 = 不分组；仅设计时预览使用）。</param>
public sealed record ConfigFieldView(
    string Name,
    string Title,
    ConfigFieldKind Kind,
    string Value,
    IReadOnlyList<ConfigChoice>? Choices = null,
    string? Unit = null,
    string? Description = null,
    string? Group = null);

/// <summary>一块显示缓冲的快照（文本日志）。</summary>
/// <param name="Lines">当前保留的全部行（只读快照，可安全跨帧持有）。</param>
/// <param name="Dropped">因超出容量被丢弃的旧行累计数（诊断用；界面只提示"满过"，不显示数量）。</param>
/// <param name="Capacity">这块缓冲的行容量。</param>
/// <param name="Revision">缓冲变更号（追加/替换/清空都会递增；同一个号 ⇒ 内容没变，可以跳过重排）。</param>
public readonly record struct TextBufferSnapshot(
    IReadOnlyList<string> Lines,
    int Dropped,
    int Capacity,
    long Revision = 0)
{
    /// <summary>空缓冲。</summary>
    public static readonly TextBufferSnapshot Empty = new([], 0, 0, 0);
}

/// <summary>显示缓冲的**尾部窗口**（呈现超预算时跳读用）。</summary>
/// <param name="Snapshot">尾部快照（只含尾部行）。</param>
/// <param name="Cursor">尾部对应的游标（后续增量从这之后开始）。</param>
public readonly record struct TextTailSnapshot(TextBufferSnapshot Snapshot, TextCursor Cursor);

/// <summary>
/// 模块在运行期给某个元素设的**外观**（只含值，不含语义）。
/// </summary>
/// <remarks>框架不认识"红 = 失败"：这里只有颜色、字号与只读开关，什么时候设归模块。</remarks>
public sealed record ViewAppearance
{
    /// <summary>边框色（<c>0xAARRGGBB</c>；<see langword="null"/> = 没设）。</summary>
    public uint? Outline { get; init; }

    /// <summary>边框的**主题 token**（由宿主的色板契约把角色词解析后传入；与 <see cref="Outline"/> 互斥）。</summary>
    public string? OutlineToken { get; init; }

    /// <summary>文字色（<c>0xAARRGGBB</c>）。</summary>
    public uint? Text { get; init; }

    /// <summary>文字的**主题 token**（同 <see cref="OutlineToken"/>）。</summary>
    public string? TextToken { get; init; }

    /// <summary>文字字号（DIP）。</summary>
    public double? TextSize { get; init; }

    /// <summary>运行期只读开关。</summary>
    public bool? ReadOnly { get; init; }

    /// <summary>运行期启用开关；<see langword="null"/> = 没有设置（按默认可用）。</summary>
    public bool? Enabled { get; init; }

    /// <summary>一个什么都没设的外观。</summary>
    public static readonly ViewAppearance None = new();
}

/// <summary>
/// **模块视图的取数 / 提交 / 外观入口**（框架提供，模块视图消费）。
/// </summary>
/// <remarks>
/// <para>
/// 它把"视图需要世界里的什么"收成一个接口：**取数走它、提交走它、外观也走它**。
/// 于是视图不持有任何服务引用，也不需要知道数据从哪来。
/// </para>
/// <para>
/// <b>名字是唯一的寻址键</b>：显示源名 / 输入源名 / 命令名 / 配置字段名都由模块在
/// <c>OnRegistered</c> 里声明过 —— 这里传进来的名字**必须**是声明过的那些。
/// </para>
/// </remarks>
public interface IModuleViewContext
{
    /// <summary>本视图所属实例 ID。</summary>
    string InstanceId { get; }

    /// <summary>
    /// 框架取色服务（可空：设计期或未接线时不可取色）。
    /// </summary>
    /// <remarks>
    /// 视图应优先使用它打开框架统一调色窗口，而不是模块内自建色板；
    /// 自动分配颜色也只从 <see cref="IModuleColorPicker.SuggestedColors"/> 取。
    /// </remarks>
    IModuleColorPicker? ColorPicker { get; }

    /// <summary>取某显示源的最新数值（无值 ⇒ <see langword="null"/>）。</summary>
    /// <param name="source">显示源名（模块声明过）。</param>
    double? ReadLatest(string source);

    /// <summary>取某显示源的序列（等间隔降采样后；无数据 ⇒ 空）。</summary>
    /// <param name="source">显示源名。</param>
    IReadOnlyList<double> ReadSeries(string source);

    /// <summary>
    /// 取某显示源的**全量序列窗口快照**（不可变；无数据 ⇒ <see langword="null"/>）。
    /// </summary>
    /// <param name="source">显示源名（声明为序列窗口）。</param>
    /// <remarks>
    /// 与 <see cref="ReadSeries"/> 的区别：本方法不做容量限制、不做降采样，
    /// 快照里的点就是模块发布窗口的全部点。视图可以直接绘制全部点。
    /// </remarks>
    SeriesWindowSnapshot? ReadSeriesWindow(string source);

    /// <summary>取某显示缓冲的全量行（文本日志 / 十六进制视图共用）。</summary>
    /// <param name="source">显示源名。</param>
    TextBufferSnapshot ReadText(string source);

    /// <summary>取某显示缓冲的**变更号**（同一个号 ⇒ 内容没变，可跳过整表复制）。</summary>
    /// <param name="source">显示源名。</param>
    /// <returns>变更号；无缓冲 ⇒ 0。</returns>
    long ReadTextRevision(string source);

    /// <summary>
    /// 取某显示缓冲**自游标以来**的窗口（每帧热路径：只取新增的已封口行与当前行的新增后缀）。
    /// </summary>
    /// <param name="source">显示源名。</param>
    /// <param name="cursor">上一拍读到的位置。</param>
    /// <param name="sealedLines">接收新增已封口行的缓冲（调用方持有，方法先清空再填）。</param>
    /// <param name="maxChars">本次窗口的字符预算（&lt;=0 ⇒ 不限；超出返回 <c>Overflow</c>）。</param>
    /// <param name="window">窗口（游标失效时 <c>Reset</c> 为真，随附当前全部行与当前行）。</param>
    /// <returns>是否取到（<see langword="false"/> = 宿主未实现，调用方回退全量）。</returns>
    bool TryReadTextWindow(
        string source,
        in TextCursor cursor,
        List<string> sealedLines,
        int maxChars,
        out TextWindow window)
    {
        window = default;
        return false;
    }

    /// <summary>
    /// 取某显示缓冲的**尾部窗口**（呈现超预算时跳读；返回 <see langword="false"/> 表示宿主未实现）。
    /// </summary>
    /// <param name="source">显示源名。</param>
    /// <param name="maxLines">最多取多少行。</param>
    /// <param name="maxChars">最多取多少字符（含当前行；首行可能被截成后缀）。</param>
    /// <param name="tail">尾部快照与对应游标。</param>
    /// <returns>是否取到尾部窗口。</returns>
    bool TryReadTextTail(string source, int maxLines, int maxChars, out TextTailSnapshot tail)
    {
        tail = default;
        return false;
    }

    /// <summary>把一个输入值提交给模块（名字未声明 ⇒ 被拒并报 Issue）。</summary>
    /// <param name="source">输入源名。</param>
    /// <param name="text">提交的文本。</param>
    /// <returns>是否被接受投递（不反映模块的处理结论）。</returns>
    bool SubmitInput(string source, string text);

    /// <summary>触发一条命令。</summary>
    /// <param name="command">命令名（模块声明过）。</param>
    /// <returns>框架的结论。</returns>
    CommandOutcome InvokeCommand(string command);

    /// <summary>取一个配置字段的当前呈现（没有 / 不可编辑 ⇒ <see langword="null"/>）。</summary>
    /// <param name="fieldName">字段名。</param>
    ConfigFieldView? ReadField(string fieldName);

    /// <summary>按字段写回一个配置值（框架按字段类型解析，失败给具名原因）。</summary>
    /// <param name="fieldName">字段名。</param>
    /// <param name="value">新值的文本形态。</param>
    CommandOutcome UpdateField(string fieldName, string value);

    /// <summary>取某元素当前被设定的外观（没设 ⇒ <see cref="ViewAppearance.None"/>）。</summary>
    /// <param name="source">元素绑定的源名（显示源 / 输入源 / 字段名）。</param>
    ViewAppearance Appearance(string source);

    /// <summary>读某一格输入框当前保留的文本（没有 ⇒ <see langword="null"/>）。</summary>
    /// <param name="source">输入源名。</param>
    /// <remarks>它读的是**用户正在编辑的内容**（含未提交的编辑），与模块的输入收件箱不是一回事。</remarks>
    string? ReadInputText(string source);

    /// <summary>把用户正在编辑的文本写回视图状态（不提交给模块）。</summary>
    /// <param name="source">输入源名。</param>
    /// <param name="text">当前文本。</param>
    void WriteInputText(string source, string text);

    /// <summary>
    /// **值的编辑预览**：把用户正在编辑的文本交给值层，但不提交、不写配置。
    /// </summary>
    /// <param name="name">值名。</param>
    /// <param name="text">当前文本。</param>
    /// <remarks>
    /// 它是控件唯一允许使用的“实时编辑”值契约：用于 OnExplicit 字段在键入时就能被模块校验，
    /// 又不会把半成品值写进配置。真正的提交仍走 <see cref="CommitValue"/>。
    /// </remarks>
    void PreviewValue(string name, string text)
    {
    }

    /// <summary>
    /// 读一个值此刻对视图呈现的全部事实（当前值、默认、可选项、只读/禁用、结论、变更号）。
    /// </summary>
    /// <param name="name">值名。</param>
    /// <remarks>控件只按这个名字取状态；它不知道值最终落在配置还是视图状态里。</remarks>
    ValueState ReadValue(string name)
    {
        var field = ReadField(name);
        if (field is null)
        {
            return ValueState.Empty(name);
        }

        return new ValueState(
            name,
            ValueEditorMapping.ToEditor(field.Kind),
            field.Value,
            field.Value,
            field.Choices?.Select(choice => new ValueChoice(choice.Value, choice.Title)).ToArray(),
            false,
            true,
            ValueValidity.Valid,
            0,
            IsKnown: true);
    }

    /// <summary>把用户交出的值提交给框架（控件不直接接触存储）。</summary>
    /// <param name="name">值名。</param>
    /// <param name="text">新值。</param>
    void CommitValue(string name, string text)
    {
    }

    /// <summary>
    /// 登记控件在 XAML 里声明的初值；仅在值声明没有提供默认值时使用。
    /// </summary>
    /// <param name="name">值名。</param>
    /// <param name="value">XAML 初值。</param>
    void RegisterViewDefault(string name, string value)
    {
    }

    /// <summary>读某个页签组当前选中的页（没有记录 ⇒ <see langword="null"/>）。</summary>
    /// <param name="stateKey">页签组的状态键（模块与视图共用的常量）。</param>
    int? SelectedPage(string stateKey);

    /// <summary>写某个页签组当前选中的页。</summary>
    /// <param name="stateKey">页签组的状态键。</param>
    /// <param name="page">页序号（0 基）。</param>
    void SetSelectedPage(string stateKey, int page);

    /// <summary>多行文本框的提交键档位（框架设置里那一项：<c>CtrlEnter</c> / <c>Enter</c> / <c>None</c>）。</summary>
    string MultilineSubmitKey { get; }

    /// <summary>波形曲线区滚轮每格的缩放系数（应用设置 <c>wheelZoomStep</c>；默认 1.2）。</summary>
    double WheelZoomStep { get; }
}
