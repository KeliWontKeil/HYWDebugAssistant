using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **文本框**（<c>textBox</c>）—— 模块里<b>唯一</b>的一块文本面，输入与输出都是它。
/// </summary>
/// <remarks>
/// <para>
/// <b>输入与输出是同一种东西</b>：一块能选中、能复制、能折行、能滚的文本面。它们的差别只有三条，
/// 全部用属性表达，而不是两种控件：
/// </para>
/// <list type="number">
///   <item><b>数据往哪走</b>：<c>input</c>（用户打的字提交给模块）还是 <c>data</c>（模块写进来的行）；</item>
///   <item><b>可不可编辑</b>：<c>readonly</c>；</item>
///   <item><b>滚法</b>：只读面走"分页窗口 + 自建竖条"（尾部跟随），可编辑面走文本框内层滚动。</item>
/// </list>
/// <para>
/// 以前这是两个类（<c>textInput</c> / <c>textLog</c>）外加一个 <c>hexView</c>，于是同一个界面里
/// 出现两种文本行为；声明面也要模块作者先决定"我这块算输入还是输出"。现在只有一个 kind。
/// </para>
/// <para>
/// <b>高度由声明给</b>（<c>height</c> 的 <c>pixels</c> / <c>fill</c> / <c>adaptive</c>）：
/// 多行框不该自己"按内容长高"把下面的控件挤走 —— 声明多高就多高，内容多了在框里滚。
/// </para>
/// </remarks>
public sealed class SurfaceTextBoxControl : SurfaceElementControl
{
    private readonly ISurfaceHost _host;
    private readonly TextSurface _box;

    /// <summary>上一帧看到的文本（用来把"用户改动"与"框架自己写进去的"分开）。</summary>
    private string _lastText;

    private IReadOnlyList<string> _buffer = [];
    private long _sequence;
    private int _lastCount = -1;
    private int _lastTailLength = -1;

    /// <summary>造一个文本框控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceTextBoxControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Slot = node.Input ?? string.Empty;
        Source = node.DataSource ?? string.Empty;

        _box = new TextSurface
        {
            IsReadOnly = node.ReadOnly,

            // **文本层的滚动机制由声明说了算**（`scrollBar` 是必写键，框架不按 `readonly` 推）：
            // `window` = 自建竖条 + 分页窗口（尾部跟随，长流用它）；
            // `native` = 文本框自己的滚动条（光标在哪滚到哪，输入面用它）。
            // 它与**元素层**的溢出（框比格子高时由外层滚动 / 裁剪）是两级不同的事实。
            ScrollMode = node.ScrollBar,
            IsMultiline = node.Multiline,

            // **显示参数一律照声明**：`wrap` / `showNewlineMark` / `textAlign` 输入面与输出面同等对待。
            // 按"这是输入还是输出"替它们做决定，等于声明面写了也不生效 ——
            // 那种静默覆盖比"没这个参数"更难查（用户改了 json、界面纹丝不动）。
            ShowNewlineMarks = node.ShowNewlineMark,
            Wraps = node.Wrap,

            // 横向对齐落在**内层编辑器**上（它才是排版文字的那一层）：
            // 把类名挂在本控件上、让主题选择器去找内层文本框，是这套对齐至今没生效的成因。
            Align = node.TextAlign switch
            {
                "center" => TextAlignment.Center,
                "right" => TextAlignment.Right,
                _ => TextAlignment.Left,
            },

            Text = node.ReadOnly ? string.Empty : RecallPending(),
        };

        _lastText = _box.Text ?? string.Empty;

        // 空框最小高**只按声明**：不声明就没有下限（框高只由声明与内容决定）。
        if (node.MinEditorHeight > 0)
        {
            _box.EditorMinHeight = node.MinEditorHeight;
        }

        _box.Classes.Add(SurfaceClasses.Text);

        // **主文字的外观在构造期就落一次**（声明是初值；运行期变化走 `OnMainTextLookChanged`）。
        OnMainTextLookChanged();
        _box.Classes.Add(SurfaceClasses.Monospace);

        // 明确拉伸：文本面的期望高是"内容高"，父层分给它更多空间时**它得吃掉**，
        // 否则元素有 388 高、正文只有 18 高（屏幕上就是"显示框没渲染出来"）。
        _box.VerticalAlignment = VerticalAlignment.Stretch;
        _box.HorizontalAlignment = HorizontalAlignment.Stretch;

        // **声明的尺寸就是这一块文本面的尺寸**（两轴都要，理由同一条）。
        // 不设的话它会按内容报一个期望尺寸，而它的内层文本框再按这个期望尺寸定死自己：
        // 声明 `height: 28` 的间隔框实测只剩 7 高（字都放不下），而面板明明分给了它 28 ——
        // 声明与实得对不上，且**没有任何报错**。宽度同理：不设下限时框会缩到文字宽度。
        if (node.Width.Pixels is { } declaredWidth)
        {
            // 写了固定像素宽度 ⇒ 就是那么宽（`fill` / `adaptive` 才由排布决定）。
            _box.Width = declaredWidth;
        }

        if (node.Height.Pixels is { } declaredHeight)
        {
            _box.MinHeight = declaredHeight;
        }

        Content = BuildChrome(_box, BuildDroppedHint());
    }

    /// <summary>本控件提交到的输入槽名（声明面 <c>input</c>；空 = 没绑）。</summary>
    public string Slot { get; }

    /// <summary>本控件读的数据源名（声明面 <c>data</c>；空 = 不读）。</summary>
    public string Source { get; }

    /// <summary>表面宿主（取数经它）。</summary>
    private ISurfaceHost Host => _host;

    /// <summary>当前缓冲里的行数。</summary>
    public int LineCount => _buffer.Count;

    /// <summary>已丢弃的行数（由显示缓冲记账）。</summary>
    public long DroppedLines => State.Dropped;

    /// <summary>用户是否改过这一格（"改过"与"提交"是两件事 —— 提交由**显式操作**触发，不由编辑触发）。</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// **标红走文本面自己的显示属性**（`FrameAlert`），不走外层元素的边框。
    /// </summary>
    /// <param name="argb">颜色；<see langword="null"/> = 清掉。</param>
    /// <remarks>
    /// 文本面那一圈边框有四个状态（常态 / 可写 / 聚焦 / 标红），**由文本面统一算** ——
    /// 外层元素直接改它的 `BorderBrush` 会把"聚焦加粗"和"可写深浅"一起抹掉
    /// （那正是"选中加粗没了、读写分不出来"的成因）。
    /// </remarks>
    protected override void ApplyOutlineColorCore(uint? argb)
    {
        _box.FrameAlert = argb;
        _box.UpdateFrame();
    }

    /// <summary>
    /// **角色词版**的标红：同样走文本面自己的显示属性，只是颜色取主题里那一支（随主题 / 用户改色跟随）。
    /// </summary>
    /// <param name="roleKey">主题资源键（已解析）；<see langword="null"/> = 清掉。</param>
    protected override void ApplyOutlineRoleCore(string? roleKey)
    {
        _box.FrameAlertRole = roleKey;
        _box.UpdateFrame();
    }

    /// <summary>
    /// **主文字的外观落到内层编辑器上**（真正显示文字的是它）。
    /// </summary>
    /// <remarks>
    /// 同 <see cref="ApplyOutlineColorCore"/> 的理由：编辑器自带一条样式前景（Fluent 的半透明黑）
    /// 与字号，而**样式值压过继承** ⇒ 画在外层元素上等于没设（用户实测两句：
    /// "文字是深灰色而不是黑色"、"模块设了颜色也不生效"）。
    /// `null` ⇒ 交还主题（`TextBox.surface` 那一档）——**主题是缺省的唯一来源**。
    /// <para>
    /// **角色词也要传下去**：文本面是"自带文字外观的控件"，基类那条 <c>ApplyLook</c> 到不了内层编辑器 ——
    /// 只传色值会让"声明/运行期写了角色词"在这一类控件上静默失效（正是本类当初踩过的那个坑的同一形状）。
    /// </para>
    /// </remarks>
    protected override void OnMainTextLookChanged()
    {
        var look = MainTextLook();
        _box.TextColor = look.Color?.ToUInt32();
        _box.TextColorRole = look.Role;
        _box.TextSize = look.Size;
    }

    /// <summary>
    /// **运行期只读**：模块说"这一格现在不让改" ⇒ 直接把文本面设成只读。
    /// </summary>
    /// <param name="isReadOnly">只读；<see langword="null"/> = 没设 ⇒ 回到声明那一档。</param>
    /// <remarks>
    /// 只读的标准做法已经落在文本面里（<c>TextSurface.IsReadOnly</c>：不粗框、不显光标、
    /// 文字仍可选中）—— 这里只负责把模块的意图转过去，不另造一套外观。
    /// **声明里的 `readonly` 是这一格的固有条款**：它说只读就一直是只读，模块改不动它。
    /// </remarks>
    protected override void ApplyReadOnly(bool? isReadOnly)
    {
        _box.IsReadOnly = Node.ReadOnly || isReadOnly == true;
    }

    /// <summary>已选/已输入的文本（测试与模块回填用）。</summary>
    public string Text => _box.Text ?? string.Empty;

    /// <summary>
    /// 刷新：可编辑面看"用户有没有改字"，只读面看"缓冲有没有换新"。
    /// </summary>
    public sealed override void Refresh()
    {
        if (Node.ReadOnly)
        {
            RefreshBuffer();
            return;
        }

        RefreshEditable();
    }

    /// <summary>可编辑面：只在"框里的字与上帧记下的不同"时当成用户改动。</summary>
    /// <remarks>
    /// **不能借用 <c>TextChanged</c> 来发现"用户改了字"**：文本面把键入的文本同步到
    /// <see cref="TextSurface.Text"/> 上，而那条属性正是本控件写进去的那一份 ⇒ 事件会回环。
    /// 每帧比一次"框里的字"与"上帧记下的那一份"，只在**真的不同**时才当成用户改动。
    /// </remarks>
    private void RefreshEditable()
    {
        var text = _box.Text ?? string.Empty;
        if (!string.Equals(text, _lastText, StringComparison.Ordinal))
        {
            _lastText = text;
            IsDirty = true;
            _host.Remember(Node.StateKey, Node.Input, SurfaceStateSlot.PendingText, text);
        }
    }

    /// <summary>
    /// 把显示缓冲取进来。
    /// </summary>
    /// <remarks>
    /// 判据三条，任一成立才重建：<b>世代号变了</b>（模块清了屏 ⇒ 本地副本作废）、
    /// <b>行数变了</b>、<b>末行长度变了</b>（缓冲把半行接长了）。三者都不成立时一个字都不动 ——
    /// 静止的日志每帧零开销。
    /// </remarks>
    private void RefreshBuffer()
    {
        if (Source.Length == 0)
        {
            return;
        }

        var view = _host.ReadBuffer(Source);
        var tailLength = view.Lines.Count == 0 ? 0 : view.Lines[^1].Length;
        var stale = view.Sequence.Value != _sequence;
        var changed = view.Lines.Count != _lastCount || tailLength != _lastTailLength;

        if (!stale && !changed)
        {
            return;
        }

        _sequence = view.Sequence.Value;
        _lastCount = view.Lines.Count;
        _lastTailLength = tailLength;
        _buffer = view.Lines;
        State.Dropped = view.Dropped;
        _capacity = view.Capacity;

        _box.Lines = _buffer;
        ApplyDroppedHint();
    }

    /// <summary>清空本地副本（模块清了屏时，下一帧的世代号变化会把它整块换掉）。</summary>
    public void Clear()
    {
        _buffer = [];
        _lastCount = -1;
        _lastTailLength = -1;
        _box.Lines = _buffer;
        ApplyDroppedHint();
    }

    /// <summary>直接把文本当作已输入（测试与模块回填用）。</summary>
    /// <param name="text">文本。</param>
    public void SetText(string text)
    {
        _box.Text = text;
        _lastText = text ?? string.Empty;
        IsDirty = false;
    }

    /// <summary>构造时取回"用户打了一半的文本"（没有 ⇒ 空串）。</summary>
    /// <returns>未提交文本。</returns>
    private string RecallPending() =>
        _host.Recall(Node.StateKey, Node.Input, SurfaceStateSlot.PendingText) ?? string.Empty;

    /// <summary>造"已丢弃 N 行"的脚注（只读面才有；0 行时整块隐藏）。</summary>
    /// <returns>脚注控件；可编辑面 ⇒ <see langword="null"/>。</returns>
    private TextBlock? BuildDroppedHint()
    {
        if (!Node.ReadOnly)
        {
            return null;
        }

        _hint = new TextBlock { Text = string.Empty, IsVisible = false };
        _hint.Classes.Add(SurfaceClasses.DroppedHint);
        return _hint;
    }

    private TextBlock? _hint;

    /// <summary>这块缓冲的行容量（由框架随快照带出来；0 = 还没读到）。</summary>
    private int _capacity;

    /// <summary>刷新"已丢弃 N 行"的提示块（0 行 ⇒ 整块隐藏，故不改变布局）。</summary>
    private void ApplyDroppedHint()
    {
        if (_hint is null)
        {
            return;
        }

        var dropped = State.Dropped;
        var text = dropped <= 0
            ? null
            : string.Create(CultureInfo.CurrentCulture, $"已丢弃 {dropped} 行（缓冲上限 {_capacity} 行）");

        _hint.Text = text ?? string.Empty;
        _hint.IsVisible = text is not null;
    }
}
