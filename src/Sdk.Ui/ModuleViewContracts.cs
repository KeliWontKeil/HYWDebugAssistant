using Avalonia.Controls;
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
/// <param name="Dropped">因超出容量被丢弃的旧行累计数。</param>
/// <param name="Capacity">这块缓冲的行容量。</param>
/// <param name="Sequence">世代号（每次清空递增；变了说明手里的副本过期）。</param>
/// <param name="Revision">缓冲变更号（追加/替换/清空都会递增；同一个号 ⇒ 内容没变，可以跳过重排）。</param>
public readonly record struct TextBufferSnapshot(
    IReadOnlyList<string> Lines,
    int Dropped,
    int Capacity,
    long Sequence,
    long Revision = 0)
{
    /// <summary>空缓冲。</summary>
    public static readonly TextBufferSnapshot Empty = new([], 0, 0, 0, 0);
}

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

    /// <summary>取某显示源的最新数值（无值 ⇒ <see langword="null"/>）。</summary>
    /// <param name="source">显示源名（模块声明过）。</param>
    double? ReadLatest(string source);

    /// <summary>取某显示源的序列（等间隔降采样后；无数据 ⇒ 空）。</summary>
    /// <param name="source">显示源名。</param>
    IReadOnlyList<double> ReadSeries(string source);

    /// <summary>取某显示缓冲的全量行（文本日志 / 十六进制视图共用）。</summary>
    /// <param name="source">显示源名。</param>
    TextBufferSnapshot ReadText(string source);

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

    /// <summary>读某个页签组当前选中的页（没有记录 ⇒ <see langword="null"/>）。</summary>
    /// <param name="stateKey">页签组的状态键（模块与视图共用的常量）。</param>
    int? SelectedPage(string stateKey);

    /// <summary>写某个页签组当前选中的页。</summary>
    /// <param name="stateKey">页签组的状态键。</param>
    /// <param name="page">页序号（0 基）。</param>
    void SetSelectedPage(string stateKey, int page);

    /// <summary>多行文本框的提交键档位（框架设置里那一项：<c>CtrlEnter</c> / <c>Enter</c> / <c>None</c>）。</summary>
    string MultilineSubmitKey { get; }
}
