using Avalonia.Controls;

namespace HYWDebugAssistant.Ui.ModuleSurface.Layout;

/// <summary>
/// **表面宿主** —— 排布层与控件层向"编排它的那一方"要东西的唯一入口。
/// </summary>
/// <remarks>
/// <para>
/// 为什么是<b>一个接口</b>而不是一组回调：控件将来要多要一样能力（多一种状态槽、多一种取数口径）时，
/// 只改这一个接口与它的实现；若写成构造参数列表，每加一样就要改所有控件的构造签名。
/// </para>
/// <para>
/// <b>它刻意不认识控件类型</b>：<see cref="Visual"/> 收的是一个排布项（节点 + 度量），
/// 造出什么控件由实现决定 —— 这就是"面板不认识控件"的落点。
/// </para>
/// </remarks>
public interface ISurfaceHost
{
    /// <summary>
    /// 多行文本框的提交键档位（框架设置里的那一项：<c>CtrlEnter</c> / <c>Enter</c> / <c>None</c>）。
    /// </summary>
    /// <remarks>
    /// <b>它是框架设置，不是声明面键</b>：同一个多行框该用哪个键提交是用户的习惯，
    /// 不是这个模块的属性 —— 所以逐控件声明它没有意义，读设置才有。控件每次按键都读它，
    /// 于是设置改了不必重建控件树（重建会丢掉焦点与未提交文本）。
    /// </remarks>
    string MultilineSubmitKey { get; }

    /// <summary>造（或复用）一个排布项的可视化。</summary>
    /// <param name="item">排布项。</param>
    /// <returns>该排布项的控件。</returns>
    Control Visual(ISurfaceLayoutItem item);

    /// <summary>取一个排布项的排布度量（填满性、最小高）。</summary>
    /// <param name="item">排布项。</param>
    /// <returns>度量。</returns>
    SurfaceMetrics Metrics(ISurfaceLayoutItem item);

    /// <summary>取某个数据源的最新读数。</summary>
    /// <param name="sourceName">声明面的 <c>data</c> 取值。</param>
    /// <returns>读数；无值 ⇒ <c>null</c>。</returns>
    double? ReadLatest(string sourceName);

    /// <summary>取某个数据源的序列（等间隔降采样后）。</summary>
    /// <param name="sourceName">声明面的 <c>data</c> 取值。</param>
    /// <returns>序列；无值 ⇒ 空。</returns>
    IReadOnlyList<double> ReadSeries(string sourceName);

    /// <summary>
    /// 取某个**显示缓冲**当前的全量行（文本日志 / 十六进制视图 / 表格共用这一条通道）。
    /// </summary>
    /// <param name="sourceName">声明面的 <c>data</c> 取值。</param>
    /// <returns>
    /// 缓冲快照 + 丢弃计数 + **序号**。序号在缓冲被清空时变化 —— 读到不同的序号说明
    /// 缓存的副本已经过期，必须整块重建（这就是"模块清了屏，界面却还留着旧行"的防线）。
    /// </returns>
    /// <remarks>
    /// <b>为什么读全量而不是"读增量"</b>：显示缓冲里既有"通道换行"的整行，也有"不换行、直接接长
    /// 最后一条"的追加（<c>DisplayBuffer.AppendText</c>）—— 后者不增加行数，只让末端那一条变长。
    /// "读增量"就得让每一方各自维护游标，而游标是界面状态、会随控件重建而丢失（切页、改设置都会重建）。
    /// 缓冲本身只有两万个封顶、且已经在 UI 侧，读全量是可控的；控件只在
    /// <b>行数变化或末端行变长</b>时才取快照，静止时一次都不取。
    /// </remarks>
    SurfaceBufferView ReadBuffer(string sourceName);

    /// <summary>把一个输入值提交给模块（就地反馈由框架回填，控件不自己编结果）。</summary>
    /// <param name="input">声明面的 <c>input</c> 取值（输入源名）；空 = 没绑输入 ⇒ 不提交。</param>
    /// <param name="text">提交的文本。</param>
    void SubmitInput(string? input, string text);

    /// <summary>触发一条命令，并把框架给的结论回给调用方。</summary>
    /// <param name="command">声明面的 <c>command</c> 取值；空 = 没声明命令名。</param>
    /// <returns>框架的结论（成功或失败，以及可显示的一句话）。</returns>
    /// <remarks>
    /// 返回结论而不是让控件自己去查：命令按钮是"点一下、立刻看到结果"的交互，
    /// 结论属于这次调用本身。控件**不解析结果文本里的字样** —— 判错靠
    /// <see cref="SurfaceCommandOutcome.IsError"/>，不靠"文案里有没有某个前缀"。
    /// </remarks>
    SurfaceCommandOutcome InvokeCommand(string? command);

    /// <summary>读一条本控件自己的持久状态（跨重建、跨窗口尺寸变化都留住）。</summary>
    /// <param name="stateKey">声明面的 <c>stateKey</c> 取值（折叠态、选中页用它）。</param>
    /// <param name="input">声明面的 <c>input</c> 取值（未提交文本用它）。</param>
    /// <param name="slot">状态槽。</param>
    /// <returns>存过的值；没有 ⇒ <c>null</c>。</returns>
    /// <remarks>
    /// 收的是**两个键名**而不是整个节点：宿主只做"名字 → 值"的路由，
    /// 不需要也不该认识声明树里的某一个节点（那样它就得维护一份"节点 → 状态"的映射表）。
    /// </remarks>
    string? Recall(string? stateKey, string? input, SurfaceStateSlot slot);

    /// <summary>写一条本控件自己的持久状态。</summary>
    /// <param name="stateKey">声明面的 <c>stateKey</c> 取值。</param>
    /// <param name="input">声明面的 <c>input</c> 取值。</param>
    /// <param name="slot">状态槽。</param>
    /// <param name="value">要存的值；<c>null</c> = 清除。</param>
    void Remember(string? stateKey, string? input, SurfaceStateSlot slot, string? value);
}

/// <summary>表面控件可以持久化的状态槽。</summary>
/// <remarks>
/// 是<b>枚举</b>而不是字符串：槽名拼错在编译期就发现，且"哪些东西会被记住"一眼可数。
/// 具体存到哪里（框架状态存储 / 视图状态服务）由宿主实现决定，控件不关心。
/// </remarks>
public enum SurfaceStateSlot
{
    /// <summary>折叠区的展开态。</summary>
    Collapsed,

    /// <summary>标签页选中的页号。</summary>
    SelectedPage,

    /// <summary>文本框里尚未提交的文本。</summary>
    PendingText,
}

/// <summary>一次命令调用的结论（面向界面，不暴露框架的内部结构）。</summary>
/// <param name="IsError">是否失败。</param>
/// <param name="Message">可显示的一句话；空 = 没什么好说的。</param>
/// <remarks>
/// 刻意是<b>一句话 + 一个布尔</b>，而不是照抄框架的 <c>CommandResult</c>（那是
/// <c>Core.Api</c> 的形状，界面层不该把它当成自己的数据模型）：界面要的是"红还是绿、
/// 写哪一行字"，不是"拒绝原因与提示各几条"。翻译发生在宿主里，也只发生在那一处。
/// </remarks>
public readonly record struct SurfaceCommandOutcome(bool IsError, string Message);

/// <summary>一块显示缓冲在某次清空之后的世代号。</summary>
/// <param name="Value">世代号（每次清空递增；从 0 起）。</param>
/// <remarks>
/// 控件把它连同自己缓存的副本一起记住；下次读到不同的值就说明副本已过期。
/// 这是"模块清了屏"能被界面察觉到的最小机制 —— 比让每个控件去订阅清空事件简单，
/// 也比"每帧都与快照逐行比对"便宜。
/// </remarks>
public readonly record struct SurfaceBufferSequence(long Value);

/// <summary>一次显示缓冲读取的结果。</summary>
/// <param name="Lines">当前保留的全部行（只读快照，可安全跨帧持有）。</param>
/// <param name="Dropped">因超出容量被丢弃的旧行累计数。</param>
/// <param name="Capacity">这块缓冲的**行容量**（超出即丢最旧）。</param>
/// <param name="Sequence">世代号（见 <see cref="SurfaceBufferSequence"/>）。</param>
/// <remarks>
/// <paramref name="Capacity"/> 由**框架**带出来而不是让控件自己猜：容量是
/// <c>DisplayBuffer</c> 的属性，而那块缓冲**不是控件建的**（控件只是读它）。
/// 控件若自己写一个数字印在提示里，就会出现"提示说 100 行、实际丢在 20000 行"。
/// </remarks>
public readonly record struct SurfaceBufferView(
    IReadOnlyList<string> Lines,
    int Dropped,
    int Capacity,
    SurfaceBufferSequence Sequence)
{
    /// <summary>空缓冲（未声明 / 没数据）。</summary>
    public static readonly SurfaceBufferView Empty = new([], 0, 0, default);
}
