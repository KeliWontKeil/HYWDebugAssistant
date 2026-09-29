namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **显示文本缓冲**：一个显示元素（A5 文本日志）当前保留的文本 —— **UI 渲染与模块回读是同一份**
/// 〔2026-09 用户裁定 · `decisions.md` 7.31〕。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>语义对齐 WinForms 的 `TextBox`</b>（用户 2026-09："文本框在 winforms 中**不会丢弃数据**，
///     不清空会一直保存"）：**只增**，只有 <see cref="Clear"/> 才清；界面只显示放得下的部分，
///     更早的内容**用滚动条回看**；</item>
///   <item><b>两个追加入口 = 换行归谁表达</b>〔2026-09 用户裁定〕：
///     <see cref="AppendText"/>（**不换行** —— 数据直接接在后面，一行多长由**文本框宽度**决定）与
///     <see cref="AppendLine"/>（**通道换行** —— 换行是调用，不是文本里的字符）；
///     历史成员 <see cref="Append"/> 保持原样（老的"发布定长样本"路径照常工作）；</item>
///   <item><b>为什么是"同一份"</b>：模块"保存显示内容"时**读文本框自己的内存**（用户原话）⇒
///     不设第二份副本（否则两份文本会不一致）；</item>
///   <item><b>容量与到顶策略</b>：<see cref="Capacity"/> **条**封顶，超出**丢弃最旧**并累计
///     <see cref="DroppedCount"/>（**不静默丢** —— 界面据此提示"已丢弃 N 行"）；</item>
///   <item><b>线程</b>：写方是 **UI 线程**（唯一写者 —— 模块经 <c>IViewDisplayAccess</c> 入队、
///     UI 在刷新节拍里落地）；读方是**模块线程**（<c>GetDisplayLines</c> 快照：缓冲 + 还没落地的那一段）
///     ⇒ 用锁保护，读侧**复制快照**（不在读时持锁做重活）。</item>
/// </list>
/// </remarks>
public sealed class DisplayBuffer
{
    /// <summary>
    /// 默认容量（行）—— 用户 2026-09 裁定 **20 000 行**（量级参考：2 万行 × 48 字符 ≈ 2 MB）。
    /// </summary>
    /// <remarks>阶段 3 落地时按**实测**（只读文本框的布局耗时与内存）再调；**先不做应用级设置项**。</remarks>
    public const int DefaultCapacity = 20_000;

    private readonly List<string> _lines;
    private readonly Lock _gate = new();

    /// <summary>创建缓冲。</summary>
    /// <param name="capacity">容量（行）；&lt;= 0 ⇒ 用 <see cref="DefaultCapacity"/>。</param>
    public DisplayBuffer(int capacity = DefaultCapacity)
    {
        Capacity = capacity > 0 ? capacity : DefaultCapacity;
        _lines = new List<string>(Math.Min(Capacity, 4096));
    }

    /// <summary>容量（行）。</summary>
    public int Capacity { get; }

    /// <summary>因超出容量而被丢弃的**旧行累计数**（界面提示"已丢弃 N 行"）。</summary>
    public int DroppedCount { get; private set; }

    /// <summary>当前保留的行数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }
    }

    /// <summary>**追加一行**（空/纯空白行忽略 —— 与既有 A5 行为一致）。</summary>
    /// <param name="line">行文本。</param>
    /// <remarks>
    /// <b>历史成员（保持原样）</b>：这是"发布定长样本"那条老路径用的入口（老模块零改动）。
    /// 新代码请用语义更清楚的 <see cref="AppendText"/>（不换行）/ <see cref="AppendLine"/>（通道换行）。
    /// </remarks>
    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        AddEntry(line, raiseTextAppended: false);
    }

    /// <summary>
    /// **追加文本（不换行）**：直接接在当前最后一条的末尾 —— 这就是 WinForms 的"追加数据直接追加在后面"。
    /// </summary>
    /// <param name="text">文本（**原样追加**：空白也是数据，绝不被吞掉）。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>缓冲区为空 ⇒ 本条成为第一条；</item>
    ///   <item><b>不引入任何换行</b>：一行多长由**文本框宽度**决定（自动折行），数据侧不规定字节数；</item>
    ///   <item>不新增"条"⇒ 不触发容量裁剪（容量按**条**计）；把内容真的接长的那一条经
    ///     <see cref="TextAppended"/> 告诉界面（重写窗口要跟着走）。</item>
    /// </list>
    /// </remarks>
    public void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        string current;

        lock (_gate)
        {
            var lines = _lines;
            DisplayCompose.Apply(lines, text, lineBreak: false);
            current = lines[^1];
        }

        TextAppended?.Invoke(current);
    }

    /// <summary>
    /// **追加一行（通道换行）**：换行由**调用方**（通道）表达，**不往文本里塞换行字符、也不加任何标记**。
    /// </summary>
    /// <param name="text">本行文本（原样；空串 = 一个空行）。</param>
    /// <remarks>
    /// 与老 <see cref="Append"/> 的差别有两条，都是有意的：①**不过滤空白**（空白也是数据 —— 数据完整性优先）；
    /// ②换行是"条与条之间"，不是文本里的字符。
    /// </remarks>
    public void AppendLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        AddEntry(text, raiseTextAppended: false);
    }

    /// <summary>追加一条（容量裁剪与事件在同一处 —— 两个入口共用）。</summary>
    private void AddEntry(string text, bool raiseTextAppended)
    {
        int trimmed;

        lock (_gate)
        {
            _lines.Add(text);
            trimmed = 0;

            while (_lines.Count > Capacity)
            {
                _lines.RemoveAt(0);
                trimmed++;
            }

            DroppedCount += trimmed;
        }

        if (trimmed > 0)
        {
            Trimmed?.Invoke(trimmed);
        }

        if (raiseTextAppended)
        {
            TextAppended?.Invoke(text);
            return;
        }

        Appended?.Invoke(text);
    }

    /// <summary>**清空**（唯一会丢内容的入口 —— 对应 `TextBox.Clear()`）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            DroppedCount = 0;
        }

        Cleared?.Invoke();
    }

    /// <summary>**快照**（调用时复制；模块回读用）。</summary>
    /// <returns>当前保留的全部行（副本）。</returns>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _lines.Count == 0 ? [] : [.. _lines];
        }
    }

    /// <summary>追加一行（UI 线程；供视图同步）。</summary>
    public event Action<string>? Appended;

    /// <summary>
    /// **把内容接长**（不换行的那条路）：给出**接长之后**的那一条全文（UI 线程；供视图同步）。
    /// </summary>
    /// <remarks>名字刻意与 <see cref="Appended"/> 分开：前者"多了一条"（集合要 Add），后者"最后一条变长了"
    /// （集合要 Replace）—— 两个动作在界面上是两件不同的事。</remarks>
    public event Action<string>? TextAppended;

    /// <summary>因容量丢掉了最旧的 N 行（UI 线程）。</summary>
    public event Action<int>? Trimmed;

    /// <summary>已清空（UI 线程）。</summary>
    public event Action? Cleared;
}
