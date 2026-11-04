using System.Text;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **显示文本缓冲**：一个显示元素（A5 文本日志）当前保留的文本 —— **UI 渲染与模块回读是同一份**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>语义对齐 WinForms 的 `TextBox`</b>（WinForms 的文本框**不会丢弃数据**，
///     不清空会一直保存）：只有 <see cref="Clear"/> 才清；界面只显示放得下的部分，
///     更早的内容**用滚动条回看**；</item>
///   <item><b>两个追加入口 = 换行归谁表达</b>：
///     <see cref="AppendText"/>（**不换行** —— 数据直接接在后面，一行多长由**文本框宽度**决定）与
///     <see cref="AppendLine"/>（**通道换行** —— 换行是调用，不是文本里的字符）；
///     历史成员 <see cref="Append"/> 保持原样（老的"发布定长样本"路径照常工作）；</item>
///   <item><b>为什么是「同一份」</b>：模块「保存显示内容」时**读文本框自己的内存** ⇒
///     不设第二份副本（否则两份文本会不一致）；</item>
///   <item><b>容量与到顶策略</b>：行数与**总字符数**双重封顶，超出**丢弃最旧**并累计
///     <see cref="DroppedCount"/>（**不静默丢** —— 界面据此提示"已丢弃 N 行"）；</item>
///   <item><b>线程</b>：写方是**实例线程**（模块经 <c>IViewDisplayAccess</c> **直接写**；唯一写者）；
///     读方是 UI（增量窗口）与模块（<c>GetDisplayLines</c> 保存快照）⇒ 锁保护，读侧只取快照/引用。</item>
/// </list>
/// <para>
/// <b>为什么当前行用 <see cref="StringBuilder"/> 而不是一条字符串</b>：不换行的那条路会把整场会话
/// 写成**同一条行**，对字符串做 <c>+=</c> 每追加一次就复制一遍全文（总代价平方级）——
/// 高频数据下那是内存与卡顿的主因。把当前行做成可追加缓冲后，追加是摊还常数；
/// 行数与字符数封顶保证内存有上界；视图经 <see cref="ReadDelta"/> 只取新增部分。
/// </para>
/// </remarks>
public sealed class DisplayBuffer : IDisplayBufferReader
{
    /// <summary>
    /// 默认容量（行）：**20 000 行**（量级参考：2 万行 × 48 字符 ≈ 2 MB）。
    /// </summary>
    /// <remarks>阶段 3 落地时按**实测**（只读文本框的布局耗时与内存）再调；**先不做应用级设置项**。</remarks>
    public const int DefaultCapacity = 20_000;

    /// <summary>
    /// 默认**总字符预算**（1 048 576 字符 ≈ 2 MB）。
    /// </summary>
    /// <remarks>
    /// 只按"行"封顶挡不住"整场会话只有一行"的形态（不换行显示就是那种形态）⇒ 另设字符预算，
    /// 超出时从最旧的已封口行开始丢。它与 <see cref="DefaultCapacity"/> 是**两个独立上限**，取先到者。
    /// </remarks>
    public const int DefaultMaxChars = 1_048_576;

    /// <summary>当前行封口后，超过这个容量就把大 <see cref="StringBuilder"/> 换掉（不保留同规模空缓冲）。</summary>
    private const int OpenBuilderReleaseCapacity = 64 * 1024;

    private readonly List<string> _sealed = [];
    private StringBuilder _open = new();
    private readonly Lock _gate = new();

    /// <summary>已封口行的有效起点（丢头是整体前移，避免每行一次 <c>RemoveAt(0)</c> 的整表搬运）。</summary>
    private int _head;

    /// <summary>被丢弃的旧**行**累计数（绝对计数：视图用它判断"该删几行头"）。</summary>
    private int _dropped;

    /// <summary>因总字符预算被截掉的行内字符数（诊断用；不改变"行"的记账）。</summary>
    private long _droppedChars;

    private int _totalChars;
    private long _version;
    private long _openVersion;
    private long _revision;

    /// <summary>创建缓冲。</summary>
    /// <param name="capacity">行容量；&lt;= 0 ⇒ 用 <see cref="DefaultCapacity"/>。</param>
    /// <param name="maxChars">总字符预算；&lt;= 0 ⇒ 用 <see cref="DefaultMaxChars"/>。</param>
    /// <param name="maxOpenChars">单行字符上限（超过即强制封口，界面上是换行）；&lt;= 0 ⇒ 等于字符预算。</param>
    public DisplayBuffer(
        int capacity = DefaultCapacity,
        int maxChars = DefaultMaxChars,
        int maxOpenChars = 0)
    {
        Capacity = capacity > 0 ? capacity : DefaultCapacity;
        MaxChars = maxChars > 0 ? maxChars : DefaultMaxChars;
        MaxOpenChars = maxOpenChars > 0 ? Math.Min(maxOpenChars, MaxChars) : MaxChars;
        _sealed.Capacity = Math.Min(Capacity, 4096);
    }

    /// <summary>容量（行）。</summary>
    public int Capacity { get; }

    /// <summary>总字符预算。</summary>
    public int MaxChars { get; }

    /// <summary>单行字符上限（超过即强制封口 ⇒ 通道换行）。</summary>
    public int MaxOpenChars { get; }

    /// <summary>
    /// **变更号**：追加/替换/清空/裁剪都会递增。读方用同一个号判断“内容没变” ⇒ 可跳过重排。
    /// </summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>当前保留的字符数（已封口行 + 当前行）。</summary>
    public int TotalChars
    {
        get
        {
            lock (_gate)
            {
                return _totalChars;
            }
        }
    }

    /// <summary>因超出容量而被丢弃的**旧行**累计数（界面提示"已丢弃 N 行"）。</summary>
    public int DroppedCount
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    /// <summary>
    /// **追加文本（不换行）**：直接接在当前行的末尾 —— 这就是 WinForms 的"追加数据直接追加在后面"。
    /// </summary>
    /// <param name="text">文本（**原样追加**：空白也是数据，绝不被吞掉）。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>缓冲区为空 ⇒ 本条成为第一条；</item>
    ///   <item><b>不引入任何换行</b>：一行多长由**文本框宽度**决定（自动折行），数据侧不规定字节数；
    ///     只有超过 <see cref="MaxOpenChars"/> 才强制封口（那是内存上界，不是数据语义）；</item>
    ///   <item>把内容真的接长的那一条经 <see cref="TextAppended"/> 告诉界面（重写窗口要跟着走）。</item>
    /// </list>
    /// </remarks>
    public void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        int trimmed;

        lock (_gate)
        {
            AppendToOpenLocked(text, forceSeal: false);
            trimmed = TrimLocked();
            _ = Interlocked.Increment(ref _revision);
        }

        if (trimmed > 0)
        {
            Trimmed?.Invoke(trimmed);
        }

        TextAppended?.Invoke(text);
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

        int trimmed;

        lock (_gate)
        {
            AppendToOpenLocked(text, forceSeal: true);
            trimmed = TrimLocked();
            _ = Interlocked.Increment(ref _revision);
        }

        if (trimmed > 0)
        {
            Trimmed?.Invoke(trimmed);
        }

        Appended?.Invoke(text);
    }

    /// <summary>**清空**（唯一会丢内容的入口 —— 对应 `TextBox.Clear()`）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _sealed.Clear();
            _sealed.TrimExcess();
            _head = 0;
            _dropped = 0;
            _totalChars = 0;
            _ = Interlocked.Exchange(ref _droppedChars, 0);
            _open.Clear();
            _open.Capacity = 0;
            _openVersion++;
            _version++;
            _ = Interlocked.Increment(ref _revision);
        }

        Cleared?.Invoke();
    }

    /// <summary>
    /// **把内容替换成这一句**（消息 / 状态这类**只有当前值**的内容）。
    /// </summary>
    /// <param name="text">新的全部内容；空串 ⇒ 等价清空。</param>
    /// <remarks>
    /// 与 <see cref="Append"/> / <see cref="AppendText"/> 的分工：那两条是**只增**（日志、流），
    /// 本方法是**替换**。替换会**重置**丢弃计数 —— 丢弃数是"这块缓冲装不下过"的记账，
    /// 换了内容之后它说的是另一份内容，留着就骗人了。
    /// </remarks>
    public void SetText(string text)
    {
        lock (_gate)
        {
            _sealed.Clear();
            _sealed.TrimExcess();
            _head = 0;
            _dropped = 0;
            _open.Clear();
            _open.Capacity = 0;
            _totalChars = 0;
            _ = Interlocked.Exchange(ref _droppedChars, 0);

            if (!string.IsNullOrEmpty(text))
            {
                // 替换语义下超出预算只保留尾部：这是"只有当前值"的内容，头部没有回看价值。
                var keep = text.Length > MaxChars ? text[^MaxChars..] : text;
                _open.Append(keep);
                _totalChars = keep.Length;
            }

            _openVersion++;
            _version++;
            _ = Interlocked.Increment(ref _revision);
        }

        Cleared?.Invoke();
        Appended?.Invoke(text ?? string.Empty);
    }

    /// <summary>**快照**（调用时复制；模块回读与"保存"用）。</summary>
    /// <returns>当前保留的全部行（副本）。</returns>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            var count = LiveSealedCount + (_open.Length > 0 ? 1 : 0);
            if (count == 0)
            {
                return [];
            }

            var lines = new List<string>(count);

            for (var index = 0; index < LiveSealedCount; index++)
            {
                lines.Add(_sealed[_head + index]);
            }

            if (_open.Length > 0)
            {
                lines.Add(_open.ToString());
            }

            return lines;
        }
    }

    /// <summary>
    /// **取窗口**：从 <paramref name="cursor"/> 之后新增的已封口行 + 当前行的后缀/整行。
    /// </summary>
    /// <param name="cursor">视图上一拍读到的位置。</param>
    /// <param name="sealedLines">接收新增已封口行的缓冲（**调用方持有**，本方法先清空再填）。</param>
    /// <returns>窗口；游标失效时 <see cref="TextWindow.Reset"/> 为真（用随附行表与当前行重建）。</returns>
    /// <remarks>
    /// 返回的字符串**不可变且稳定**（已封口行一旦产生就不再变），可安全跨帧持有；
    /// 当前行的后缀是本次复制出来的新串，量级 = 这一拍新增的字符数。
    /// </remarks>
    public TextWindow ReadWindow(TextCursor cursor, List<string> sealedLines, int maxChars = 0)
    {
        ArgumentNullException.ThrowIfNull(sealedLines);

        lock (_gate)
        {
            sealedLines.Clear();

            var sealedCount = _dropped + LiveSealedCount;

            // 被整块换掉（清空 / 替换）或游标越界 ⇒ 用当前全部内容重建。
            if (cursor.Version != _version
                || cursor.SealedCount > sealedCount
                || cursor.Dropped > _dropped)
            {
                if (maxChars > 0 && _totalChars > maxChars)
                {
                    return new TextWindow(
                        _version, _dropped, sealedCount, _open.Length, _openVersion,
                        Reset: true, OpenSuffix: null, OpenText: null, Revision, Overflow: true);
                }

                for (var index = _head; index < _sealed.Count; index++)
                {
                    sealedLines.Add(_sealed[index]);
                }

                return new TextWindow(
                    _version, _dropped, sealedCount, _open.Length, _openVersion,
                    Reset: true, OpenSuffix: null, OpenText: _open.ToString(), Revision);
            }

            // 游标落后于裁剪点：从当前第一条封口行重新给全部；视图删掉它手里的旧行。
            var from = cursor.SealedCount < _dropped
                ? _head
                : _head + (cursor.SealedCount - _dropped);

            var incoming = cursor.OpenVersion == _openVersion
                ? _open.Length - cursor.OpenLength
                : _open.Length;

            for (var index = from; index < _sealed.Count; index++)
            {
                incoming += _sealed[index].Length;
            }

            if (maxChars > 0 && incoming > maxChars)
            {
                return new TextWindow(
                    _version, _dropped, sealedCount, _open.Length, _openVersion,
                    Reset: false, OpenSuffix: null, OpenText: null, Revision, Overflow: true);
            }

            for (var index = from; index < _sealed.Count; index++)
            {
                sealedLines.Add(_sealed[index]);
            }

            string? suffix = null;
            string? full = null;

            if (cursor.OpenVersion == _openVersion)
            {
                suffix = cursor.OpenLength < _open.Length
                    ? _open.ToString(cursor.OpenLength, _open.Length - cursor.OpenLength)
                    : string.Empty;
            }
            else
            {
                full = _open.ToString();
            }

            return new TextWindow(
                _version, _dropped, sealedCount, _open.Length, _openVersion,
                Reset: false, suffix, full, Revision);
        }
    }

    /// <summary>
    /// **读尾部窗口**：只取最近若干行、且总字符不超过 <paramref name="maxChars"/>（含当前行），
    /// 并给出对应游标。
    /// </summary>
    /// <param name="maxLines">最多取多少行；&lt;= 0 ⇒ 只取当前行。</param>
    /// <param name="maxChars">最多取多少字符；&lt;= 0 ⇒ 不按字符限制。</param>
    /// <param name="cursor">尾部游标：已封口行按**全部已应用**记（视图只持有尾部，不重发旧行）。</param>
    /// <returns>尾部行（当前行在最后，若它非空）；首行可能被截成后缀。</returns>
    /// <remarks>
    /// 用于**呈现超预算时跳读**：视图不必为这一拍的全部新增数据付出排版代价，
    /// 只重建可见尾部；数据与保存仍以缓冲为准。它不改变缓冲内容。
    /// </remarks>
    public IReadOnlyList<string> ReadTail(int maxLines, int maxChars, out TextCursor cursor)
    {
        lock (_gate)
        {
            var open = _open.Length > 0 ? 1 : 0;
            var live = LiveSealedCount;
            var lineLimit = Math.Max(1, maxLines);
            var charLimit = maxChars > 0 ? maxChars : int.MaxValue;
            var remaining = charLimit;

            var lines = new List<string>(Math.Min(lineLimit, live + open));
            string? openText = null;

            if (open > 0)
            {
                openText = _open.Length > remaining
                    ? _open.ToString(_open.Length - remaining, remaining)
                    : _open.ToString();

                remaining -= openText.Length;
            }

            var sealedKeep = Math.Min(live, Math.Max(0, lineLimit - open));
            var firstKept = _sealed.Count - sealedKeep;

            for (var index = _sealed.Count - 1; index >= firstKept && remaining > 0; index--)
            {
                var line = _sealed[index];

                if (line.Length <= remaining)
                {
                    lines.Add(line);
                    remaining -= line.Length;
                }
                else
                {
                    lines.Add(line[^remaining..]);
                    remaining = 0;
                }
            }

            lines.Reverse();

            if (openText is not null)
            {
                lines.Add(openText);
            }

            cursor = new TextCursor(
                _version, _dropped, _dropped + live, _open.Length, _openVersion);

            return lines;
        }
    }

    /// <summary>
    /// 缓冲是否**满过**（自上次清空/替换以来丢过旧内容）。
    /// </summary>
    /// <remarks>
    /// 界面只需要"满过没有"这一位用于提示；具体丢了多少行/多少字符，
    /// 在不同丢弃形态（整行 / 当前行被截）下口径不一致，不作为界面事实呈现。
    /// </remarks>
    public bool HasTrimmed => _dropped > 0 || Interlocked.Read(ref _droppedChars) > 0;

    /// <summary>追加一行（实例线程；供视图同步）。</summary>
    public event Action<string>? Appended;

    /// <summary>
    /// **把内容接长**（不换行的那条路）：给出**本次接上去的那一段**（实例线程；供视图同步）。
    /// </summary>
    /// <remarks>名字刻意与 <see cref="Appended"/> 分开：前者"多了一条"（集合要 Add），后者"最后一条变长了"
    /// （集合要 Replace）—— 两个动作在界面上是两件不同的事。只发新增段而不是整行全文：
    /// 当前行可以很长，每拍复制全文等于把已解决的问题又引回来。</remarks>
    public event Action<string>? TextAppended;

    /// <summary>因容量丢掉了最旧的 N 行（实例线程）。</summary>
    public event Action<int>? Trimmed;

    /// <summary>已清空（实例线程）。</summary>
    public event Action? Cleared;

    private int LiveSealedCount => _sealed.Count - _head;

    /// <summary>
    /// 把一段文本接进当前行；<paramref name="forceSeal"/> 时随后封口（通道换行）。
    /// </summary>
    private void AppendToOpenLocked(string text, bool forceSeal)
    {
        var offset = 0;
        _totalChars += text.Length;

        while (offset < text.Length)
        {
            var room = MaxOpenChars - _open.Length;
            if (room <= 0)
            {
                // 单行超过上限：封口成新的一行（这是内存上界，不是数据里的换行）。
                SealOpenLocked();
                room = MaxOpenChars;
            }

            var take = Math.Min(room, text.Length - offset);

            // 不把代理对切成两半（半个代理对既画不出来、也会让保存的文本坏掉）。
            if (take < text.Length - offset && take > 0 && char.IsHighSurrogate(text[offset + take - 1]))
            {
                take--;
            }

            if (take <= 0)
            {
                take = 1;
            }

            _open.Append(text, offset, take);
            offset += take;
        }

        if (forceSeal)
        {
            SealOpenLocked();
        }
    }

    /// <summary>把当前行封口成一条（空行也算一条），并开一条新的当前行。</summary>
    private void SealOpenLocked()
    {
        _sealed.Add(_open.ToString());

        // 大当前行封口后不要留下同样大的空 StringBuilder：否则已封口字符串与旧容量会同时驻留。
        if (_open.Capacity > OpenBuilderReleaseCapacity)
        {
            _open = new StringBuilder();
        }
        else
        {
            _open.Clear();
        }

        _openVersion++;
    }

    /// <summary>按行数与字符预算丢最旧的行；返回本次丢掉的行数。</summary>
    private int TrimLocked()
    {
        var trimmed = 0;
        var openLine = _open.Length > 0 ? 1 : 0;

        // 两个上限取先到者：行数挡"每包一行"，字符预算挡"整场会话只有一行"。
        while ((LiveSealedCount + openLine > Capacity || _totalChars > MaxChars) && LiveSealedCount > 0)
        {
            _totalChars -= _sealed[_head].Length;
            _sealed[_head] = string.Empty;
            _head++;
            _dropped++;
            trimmed++;
        }

        // 回收头部空洞：摊还 O(1)，避免 List 无限增长。
        if (_head > 1024 && _head * 2 >= _sealed.Count)
        {
            _sealed.RemoveRange(0, _head);
            _head = 0;
        }

        // 极端：只有当前行、且它自己就超过预算 ⇒ 截掉它的头部（整行换掉，视图按世代重建）。
        if (_totalChars > MaxChars && _open.Length > MaxChars)
        {
            var excess = _open.Length - MaxChars;
            _open.Remove(0, excess);
            _totalChars -= excess;
            _ = Interlocked.Add(ref _droppedChars, excess);
            _openVersion++;
        }

        return trimmed;
    }
}
