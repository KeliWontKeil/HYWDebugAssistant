using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **增量只读文本视图**：按行缓存 <see cref="TextLayout"/>，追加只布局新增行/最后一个视觉行，
/// 只绘制可见行；保留自动折行、完整回看、跨行选择/复制与真实换行标记。
/// </summary>
/// <remarks>
/// <para>
/// 这不是“把全文塞进一个 TextBlock 再调折行参数”：Avalonia 的 <see cref="TextBlock"/> /
/// <see cref="SelectableTextBlock"/> 只要 <c>Text</c>/<c>Inlines</c> 变化就重建整篇
/// <see cref="TextLayout"/>，长文本下是超线性成本。本控件把文本拆成“逻辑行 + 视觉行”，
/// 每个视觉行缓存自己的 <see cref="TextLayout"/>；追加只重建最后一个视觉行，旧行完全不动。
/// </para>
/// <para>
/// 绘制只遍历与滚动窗口相交的视觉行；选择/复制用扁平文本偏移，跨视觉行/硬换行都按真实文本处理。
/// 显示刷新由外部按全局节拍调用 <see cref="ApplyWindow"/>；没有新内容时一行都不重排。
/// </para>
/// </remarks>
public sealed class IncrementalTextView : TemplatedControl
{
    /// <summary>选择底色。</summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<IncrementalTextView, IBrush?>(nameof(SelectionBrush));

    /// <summary>选择文字色（当前保留，绘制时沿用正文色；供主题/测试读取）。</summary>
    public static readonly StyledProperty<IBrush?> SelectionForegroundBrushProperty =
        AvaloniaProperty.Register<IncrementalTextView, IBrush?>(nameof(SelectionForegroundBrush));

    /// <summary>是否画真实换行字符的 `↵`。</summary>
    public static readonly StyledProperty<bool> ShowNewlineMarksProperty =
        AvaloniaProperty.Register<IncrementalTextView, bool>(nameof(ShowNewlineMarks), defaultValue: true);

    /// <summary>不可显示字符标记 `⟨XX⟩` 的颜色；null = 与正文同色。</summary>
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<IncrementalTextView, IBrush?>(nameof(MarkerBrush));

    /// <summary>不可显示字符显示为警示色标记（true）还是方框占位（false）。</summary>
    public static readonly StyledProperty<bool> ShowControlCharacterMarkersProperty =
        AvaloniaProperty.Register<IncrementalTextView, bool>(nameof(ShowControlCharacterMarkers), defaultValue: true);

    /// <summary>行末换行标记的颜色；null = 主题的 TextMuted，再退到灰色。</summary>
    public static readonly StyledProperty<IBrush?> NewlineMarkBrushProperty =
        AvaloniaProperty.Register<IncrementalTextView, IBrush?>(nameof(NewlineMarkBrush));

    /// <summary>追加内容后是否自动滚到尾部。</summary>
    public static readonly StyledProperty<bool> AutoScrollProperty =
        AvaloniaProperty.Register<IncrementalTextView, bool>(nameof(AutoScroll), defaultValue: true);

    /// <summary>文本水平对齐。</summary>
    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<IncrementalTextView, TextAlignment>(nameof(TextAlignment));

    /// <summary>
    /// 一次折行布局最多吃多少字符。
    /// </summary>
    /// <remarks>
    /// 折行布局按块做（每块一个 `TextLayout`，块尾的最后一行留给下一块避免硬断）。块越大，
    /// 同样多的字符产生的布局对象越少（对象头/缓存/字形数组的固定开销被摊薄）；
    /// 块过大会让单次布局的延迟变差。512 是"分配速率"与"单帧延迟"之间的折中。
    /// </remarks>
    private const int MaxAppendChunk = 512;

    private readonly List<RawLine> _raw = [];
    private readonly List<DisplayLine> _display = [];
    private readonly List<VisualLine> _visuals = [];

    private double _layoutWidth = -1;
    private double _contentHeight;
    private int _flatEnd;
    private long _revision = -1;
    private int _selectionStart;
    private int _selectionEnd;
    private int _selectionAnchor;
    private bool _selecting;
    private ScrollViewer? _scroll;
    private readonly Dictionary<string, FormattedText> _newlineMarks = [];
    private int _layoutBuildCount;

    /// <summary>
    /// 有"字体/字号变了，需要按新度量重排"这件待办。
    /// </summary>
    /// <remarks>
    /// 重排是 O(缓冲内容) 的；主题在控件离开可视树时会把样式值退回默认，
    /// 若每个属性变化都当场重排，关闭一个满缓冲的显示区会让界面卡住。
    /// 因此这里只记待办，真正重排推到下一次测量（离开树之后就不会再测）。
    /// </remarks>
    private bool _relayoutPending;

    /// <summary>单行高度（同一字体下恒定；用来给"没有布局的视觉行"定位，避免为每一行都建布局）。</summary>
    private double _lineHeight;

    /// <summary>当前真正持有 <see cref="TextLayout"/> 的视觉行（只有可见窗口附近的行；其余行只留文本切片）。</summary>
    private readonly List<VisualLine> _laidOut = [];

    /// <summary>已应用的"已封口行总数"（绝对计数；尾部跳读后仍以它为绝对坐标）。</summary>
    private int _sealedApplied;

    /// <summary>已应用的内容版本（清空/整块替换时变）。</summary>
    private long _version;

    /// <summary>已应用的当前行世代号（变了 ⇒ 当前行整行换掉，而不是接后缀）。</summary>
    private long _openVersion;

    /// <summary>已应用的当前行字符数（只用于一致性检查）。</summary>
    private int _openLength;


    /// <summary>当前行对应的那条 raw（没有内容时为 <see langword="null"/>）。</summary>
    private RawLine? _openRaw;

    /// <summary>创建增量文本视图。</summary>
    public IncrementalTextView()
    {
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Transparent;   // 整块可命中（否则只有字形处能选中，空白处点不到）
        Cursor = new Cursor(StandardCursorType.Ibeam);

        var copyItem = new MenuItem { Header = TextCatalog.Get(typeof(IncrementalTextView).Assembly, "moduleText.contextMenu.copy") };
        copyItem.Click += (_, _) => Copy();
        var selectAllItem = new MenuItem { Header = TextCatalog.Get(typeof(IncrementalTextView).Assembly, "moduleText.contextMenu.selectAll") };
        selectAllItem.Click += (_, _) => SelectAll();
        ContextMenu = new ContextMenu { Items = { copyItem, selectAllItem } };
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <summary>选择底色。</summary>
    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>选择文字色。</summary>
    public IBrush? SelectionForegroundBrush
    {
        get => GetValue(SelectionForegroundBrushProperty);
        set => SetValue(SelectionForegroundBrushProperty, value);
    }

    /// <summary>是否画真实换行字符的 `↵`。</summary>
    public bool ShowNewlineMarks
    {
        get => GetValue(ShowNewlineMarksProperty);
        set => SetValue(ShowNewlineMarksProperty, value);
    }

    /// <summary>不可显示字符标记 `⟨XX⟩` 的绘制颜色。</summary>
    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>不可显示字符显示为标记（true）还是方框（false）。</summary>
    public bool ShowControlCharacterMarkers
    {
        get => GetValue(ShowControlCharacterMarkersProperty);
        set => SetValue(ShowControlCharacterMarkersProperty, value);
    }

    /// <summary>行末换行标记的绘制颜色；默认跟随 TextMuted。</summary>
    public IBrush? NewlineMarkBrush
    {
        get => GetValue(NewlineMarkBrushProperty);
        set => SetValue(NewlineMarkBrushProperty, value);
    }

    /// <summary>追加内容后是否自动滚到尾部。</summary>
    public bool AutoScroll
    {
        get => GetValue(AutoScrollProperty);
        set => SetValue(AutoScrollProperty, value);
    }

    /// <summary>文本水平对齐。</summary>
    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>完整文本（按“行 + 真实/通道换行”拼出；只在复制/测试等按需调用时生成）。</summary>
    public string Text => BuildText();

    /// <summary>选择起点（扁平字符偏移）。</summary>
    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            _selectionStart = Math.Clamp(value, 0, TextLength);
            InvalidateVisual();
        }
    }

    /// <summary>选择终点（扁平字符偏移）。</summary>
    public int SelectionEnd
    {
        get => _selectionEnd;
        set
        {
            _selectionEnd = Math.Clamp(value, 0, TextLength);
            InvalidateVisual();
        }
    }

    /// <summary>选中文本。</summary>
    public string SelectedText
    {
        get
        {
            var start = Math.Min(_selectionStart, _selectionEnd);
            var end = Math.Max(_selectionStart, _selectionEnd);
            if (end <= start)
            {
                return string.Empty;
            }

            var text = BuildText();
            var selected = end <= text.Length ? text[start..end] : text[start..];
            return selected;
        }
    }

    /// <summary>是否有可复制内容。</summary>
    public bool CanCopy => _selectionStart != _selectionEnd;

    /// <summary>当前真实换行标记数（测试/诊断用）。</summary>
    public int MarkCount => ShowNewlineMarks ? _display.Count(line => line.RealBreak) : 0;

    /// <summary>视觉行数（测试/诊断用）。</summary>
    public int VisualLineCount => _visuals.Count;

    /// <summary>每个视觉行的字符数（测试/诊断用；用于断言折行连续、没有块边界硬断）。</summary>
    public IReadOnlyList<int> VisualLineLengths => _visuals.Select(visual => visual.SourceLength).ToArray();

    /// <summary>已创建的 <see cref="TextLayout"/> 数（增量性探针用）。</summary>
    public int LayoutBuildCount => _layoutBuildCount;

    /// <summary>
    /// **当前仍持有布局的视觉行数**（诊断/测试用）。
    /// </summary>
    /// <remarks>
    /// 它应当只与"可见窗口附近"有关，而不是与缓冲里有多少行有关 ——
    /// 这是"长缓冲不按行数爆内存"的直接判据。
    /// </remarks>
    public int LiveLayoutCount => _laidOut.Count;

    /// <summary>当前扁平文本长度。</summary>
    public int TextLength => _flatEnd;

    /// <summary>把内容替换成一条完整文本（静态只读文本用；内部按 `\n` 拆段）。</summary>
    /// <param name="text">完整文本。</param>
    public void SetText(string text)
    {
        Rebuild(text is null ? [] : [text]);
        _revision = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// 应用一次显示缓冲全量快照（设计期/未实现窗口的宿主回退用；变更号相同 ⇒ 立即返回）。
    /// </summary>
    /// <remarks>热路径的增量只有 <see cref="ApplyWindow"/>；本方法每次变更都全量重建。</remarks>
    /// <param name="lines">缓冲当前保留的全部原始行（行间是通道换行，行内可含真实 `\n`）。</param>
    /// <param name="revision">缓冲变更号。</param>
    /// <returns>是否应用了新内容。</returns>
    public bool ApplySnapshot(IReadOnlyList<string> lines, long revision)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (revision == _revision)
        {
            return false;
        }

        // 快照路径只服务设计期/未实现窗口的宿主：全量重建即可。
        // 热路径的增量只有 ApplyWindow 一条实现。
        Rebuild(lines);
        _revision = revision;
        InvalidateMeasure();
        InvalidateVisual();
        ScrollToEndIfNeeded(appended: true);
        return true;
    }

    /// <summary>
    /// **应用一次窗口**（每帧热路径）：只处理新增的已封口行、当前行的新增后缀/整行与头部裁剪。
    /// </summary>
    /// <param name="window">缓冲给出的窗口（<see cref="TextWindow.Reset"/> 时随附全部内容）。</param>
    /// <param name="sealedLines">与窗口配套的已封口行。</param>
    /// <returns>是否应用成功；<see langword="false"/> = 控件内部状态与窗口对不上，调用方应改用全量快照。</returns>
    /// <remarks>
    /// 它替代"每帧取全量快照再比对"：追加只碰最后一条，头部裁剪只删最旧的若干条，
    /// 代价与**这一拍新增的数据量**成正比，与缓冲里已有多少内容无关。
    /// </remarks>
    public bool ApplyWindow(TextWindow window, IReadOnlyList<string> sealedLines)
    {
        ArgumentNullException.ThrowIfNull(sealedLines);

        if (window.Reset)
        {
            ResetAll();

            foreach (var text in sealedLines)
            {
                AppendSealedRawLine(text);
            }

            ReplaceOpenLine(window.OpenText ?? string.Empty);

            _version = window.Version;
            _sealedApplied = window.SealedCount;
            _openLength = window.OpenLength;
            _openVersion = window.OpenVersion;
            _revision = window.Revision;

            RecomputeLayout();
            InvalidateMeasure();
            InvalidateVisual();
            ScrollToEndIfNeeded(appended: true);
            return true;
        }

        var sealedRawCount = _raw.Count - (_openRaw is null ? 0 : 1);
        var firstRawAbsolute = _sealedApplied - sealedRawCount;
        var removed = window.Dropped - firstRawAbsolute;

        if (removed < 0 || window.SealedCount < _sealedApplied)
        {
            return false;
        }

        removed = Math.Min(removed, sealedRawCount);

        var oldDisplayCount = _display.Count;
        var oldVisualCount = _visuals.Count;
        var changedOpen = _openRaw is not null;
        var oldOpenSegments = _openRaw?.Segments.Count ?? 0;
        var oldOpenVisualStart = oldOpenSegments > 0
            ? _openRaw!.Segments[0].VisualStart
            : oldVisualCount;

        if (removed > 0)
        {
            RemoveFrontRaw(removed);
        }

        var index = 0;

        if (_openRaw is not null && sealedLines.Count > 0)
        {
            // 旧当前行封口：用缓冲给的最终文本替换它（封口/裁剪都可能改过它）。
            ReplaceRawText(_openRaw, sealedLines[0]);
            _openRaw = null;
            index = 1;
        }

        for (; index < sealedLines.Count; index++)
        {
            AppendSealedRawLine(sealedLines[index]);
        }

        if (window.OpenText is not null)
        {
            ReplaceOpenLine(window.OpenText);
        }
        else if (!string.IsNullOrEmpty(window.OpenSuffix))
        {
            AppendOpenText(window.OpenSuffix!);
        }

        _version = window.Version;
        _openVersion = window.OpenVersion;
        _openLength = window.OpenLength;
        _sealedApplied = window.SealedCount;
        _revision = window.Revision;

        if (removed == 0 && oldDisplayCount > 0 && window.OpenText is null)
        {
            var startLine = changedOpen
                ? oldDisplayCount - oldOpenSegments
                : oldDisplayCount;

            if (startLine >= 0 && startLine < _display.Count)
            {
                var startVisual = changedOpen
                    ? oldOpenVisualStart
                    : oldVisualCount;

                RecomputeLayoutFrom(startLine, startVisual);
            }
            else
            {
                RecomputeLayout();
            }
        }
        else
        {
            RecomputeLayout();
        }

        InvalidateMeasure();
        InvalidateVisual();
        ScrollToEndIfNeeded(appended: true);
        return true;
    }

    /// <summary>记下"全量快照/尾部窗口已经应用到哪个游标"（<see cref="ApplySnapshot"/> 之后调用）。</summary>
    /// <param name="cursor">快照对应的游标。</param>
    public void SetCursor(TextCursor cursor)
    {
        _version = cursor.Version;
        _sealedApplied = cursor.SealedCount;
        _openLength = cursor.OpenLength;
        _openVersion = cursor.OpenVersion;

        // 快照里最后一条就是"当前行"（它非空时才会被写进快照）。
        _openRaw = cursor.OpenLength > 0 && _raw.Count > 0 ? _raw[^1] : null;
    }

    /// <summary>清空视图的全部文本/布局状态（重置窗口用）。</summary>
    private void ResetAll()
    {
        foreach (var line in _display)
        {
            DisposeVisuals(line);
        }

        _raw.Clear();
        _display.Clear();
        _visuals.Clear();
        _laidOut.Clear();
        _openRaw = null;
    }

    /// <summary>清除选择。</summary>
    public void ClearSelection()
    {
        _selectionStart = 0;
        _selectionEnd = 0;
        _selectionAnchor = 0;
        InvalidateVisual();
    }

    /// <summary>全选。</summary>
    public void SelectAll()
    {
        _selectionStart = 0;
        _selectionEnd = TextLength;
        _selectionAnchor = 0;
        InvalidateVisual();
    }

    /// <summary>复制选中文本到剪贴板。</summary>
    public void Copy()
    {
        var text = SelectedText;
        if (text.Length == 0)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width is double.PositiveInfinity or double.NaN
            ? Bounds.Width
            : availableSize.Width;

        width = Math.Max(0, width - Padding.Left - Padding.Right);


        var widthChanged = width > 0 && Math.Abs(width - _layoutWidth) > 0.01;

        if (width > 0 && (widthChanged || _relayoutPending))
        {
            _layoutWidth = width;
            _relayoutPending = false;
            RelayoutAll();
        }

        var height = _contentHeight + Padding.Top + Padding.Bottom;
        return new Size(availableSize.Width is double.PositiveInfinity ? width : availableSize.Width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize) => finalSize;



    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);


        if (change.Property == FontFamilyProperty
            || change.Property == FontSizeProperty
            || change.Property == FontWeightProperty
            || change.Property == FontStyleProperty)
        {
            _newlineMarks.Clear();
            _lineHeight = 0;
            _relayoutPending = true;
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        if (change.Property == MarkerBrushProperty)
        {
            RelayoutAll();
            return;
        }

        if (change.Property == ShowControlCharacterMarkersProperty)
        {
            InvalidateProjections();
            RelayoutAll();
            return;
        }

        if (change.Property == NewlineMarkBrushProperty)
        {
            _newlineMarks.Clear();
            InvalidateVisual();
            return;
        }

        if (change.Property == ForegroundProperty)
        {
            // 换色只影响"画出来的颜色"：布局对象里烤着画笔，所以要丢缓存，但折行位置没变。
            _newlineMarks.Clear();
            ReleaseAllLayouts();
            InvalidateVisual();
            return;
        }

        if (change.Property == SelectionBrushProperty
            || change.Property == ShowNewlineMarksProperty
            || change.Property == TextAlignmentProperty)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);

        if (Background is { } background)
        {
            // 透明底也要真的画一笔：Avalonia 的命中测试基于渲染几何，不画就只剩字形处可命中。
            context.FillRectangle(background, new Rect(Bounds.Size));
        }

        if (_visuals.Count == 0 || _layoutWidth <= 0)
        {
            return;
        }

        var offset = _scroll?.Offset.Y ?? 0;
        var viewport = _scroll?.Viewport.Height ?? Bounds.Height;
        var contentWidth = Math.Max(0, Bounds.Width - Padding.Left - Padding.Right);
        // 绝对色：主题缺失时的兜底墨色；正常路径由 Foreground 主题 brush 提供。
        var brush = Foreground ?? Brushes.Black;
        // 绝对色：主题缺失时的兜底选择色；正常路径由 SelectionBrush（OverlaySelection）提供。
        var selection = SelectionBrush ?? Brushes.LightBlue;
        var newlineBrush = NewlineMarkBrush;

        if (newlineBrush is null
            && ResourceNodeExtensions.TryFindResource(this, "TextMuted", out var muted)
            && muted is IBrush themedMuted)
        {
            newlineBrush = themedMuted;
        }

        // 绝对色：主题缺失时的兜底灰色；正常路径由 TextMuted 提供。
        newlineBrush ??= Brushes.Gray;
        var selStart = Math.Min(_selectionStart, _selectionEnd);
        var selEnd = Math.Max(_selectionStart, _selectionEnd);

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            // **只画可见行**：`_visuals` 的 Top 单调递增，二分定位第一个可见行；
            // 行高恒定，所以"可见窗口"不需要任何布局就能算出来。
            var first = FindFirstVisible(offset);
            var last = first;

            for (; last < _visuals.Count; last++)
            {
                var top = Padding.Top + _visuals[last].Top;

                if (top > offset + viewport)
                {
                    break;
                }
            }

            for (var index = first; index < last; index++)
            {
                var visual = _visuals[index];
                var line = EnsureLine(visual);
                var y = Padding.Top + visual.Top;
                var x = Padding.Left + AlignmentOffset(contentWidth, line.Width);

                DrawSelection(context, visual, line, x, y, selection, selStart, selEnd);
                line.Draw(context, new Point(x, y));

                if (ShowNewlineMarks && visual.Owner.RealBreak && ReferenceEquals(visual, visual.Owner.Visuals[^1]))
                {
                    var mark = NewlineEncoding.MarkOf(visual.Owner.BreakKind);
                    if (mark.Length > 0)
                    {
                        DrawMark(
                            context,
                            x + line.Width + 2,
                            y + ((line.Height - MarkFormatted(newlineBrush, mark).Height) / 2),
                            newlineBrush,
                            mark);
                    }
                }
            }

            // 窗口外的布局随手下手：不给"滚过去看过一眼"的行永久留一份整形结果。
            ReleaseLayoutsOutside(first - 32, last + 32);
        }
    }

    /// <summary>释放可见窗口之外（含较大余量）的视觉行布局。</summary>
    private void ReleaseLayoutsOutside(int from, int to)
    {
        for (var index = _laidOut.Count - 1; index >= 0; index--)
        {
            var visual = _laidOut[index];

            if (visual.Index >= from && visual.Index <= to)
            {
                continue;
            }

            ReleaseLayout(visual);
        }
    }

    /// <summary>二分找到第一条"与可见窗口相交"的视觉行（Top 单调递增、行高恒定）。</summary>
    private int FindFirstVisible(double offset)
    {
        var lineHeight = LineHeight;
        var low = 0;
        var high = _visuals.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var visual = _visuals[middle];

            if (Padding.Top + visual.Top + lineHeight < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        _selecting = true;
        e.Pointer.Capture(this);

        var position = e.GetPosition(this);
        var offset = HitTestOffset(position);

        if (e.ClickCount == 2)
        {
            SelectWord(offset);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _selectionEnd = offset;
        }
        else
        {
            _selectionAnchor = offset;
            _selectionStart = offset;
            _selectionEnd = offset;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_selecting)
        {
            return;
        }

        _selectionEnd = HitTestOffset(e.GetPosition(this));
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_selecting)
        {
            _selecting = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            Copy();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            MoveCaret(-1, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            MoveCaret(1, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            SetCaret(0, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            SetCaret(TextLength, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _scroll = this.FindAncestorOfType<ScrollViewer>();

        if (_scroll is not null)
        {
            _scroll.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_scroll is not null)
        {
            _scroll.ScrollChanged -= OnScrollChanged;
            _scroll = null;
        }

        // 正在离开可视树：样式退回默认会连着触发几次"字体变了"，
        // 这时候重排看不见的内容既没意义又会让关闭卡顿。
        _relayoutPending = false;
        ReleaseAllLayouts();
        _newlineMarks.Clear();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private void MoveCaret(int delta, bool extend)
    {
        var caret = extend ? _selectionEnd : _selectionStart;
        SetCaret(caret + delta, extend);
    }

    private void SetCaret(int offset, bool extend)
    {
        offset = Math.Clamp(offset, 0, TextLength);

        if (extend)
        {
            _selectionEnd = offset;
        }
        else
        {
            _selectionStart = offset;
            _selectionEnd = offset;
            _selectionAnchor = offset;
        }

        InvalidateVisual();
    }

    private void SelectWord(int offset)
    {
        var text = BuildText();
        if (text.Length == 0)
        {
            return;
        }

        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset;
        var end = offset;

        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        _selectionStart = start;
        _selectionEnd = end;
        _selectionAnchor = start;
        InvalidateVisual();
    }

    private void Rebuild(IReadOnlyList<string> lines)
    {
        foreach (var line in _display)
        {
            DisposeVisuals(line);
        }

        _raw.Clear();
        _display.Clear();
        _visuals.Clear();
        _laidOut.Clear();

        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i] ?? string.Empty;
            var raw = i == lines.Count - 1
                ? new RawLine { Text = new StringBuilder(text), Index = i }
                : new RawLine { SealedText = text, Index = i };
            AddSegments(raw, text);
            _raw.Add(raw);
        }

        for (var i = 0; i < _raw.Count - 1; i++)
        {
            SetChannelBreak(_raw[i], true);
        }

        _openRaw = _raw.Count > 0 ? _raw[^1] : null;
        RecomputeLayout();
    }

    private void AddSegments(RawLine raw, string text)
    {
        var start = 0;
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    // CRLF 交给下面的 LF 分支统一处理（保留 ↵ 种类）。
                    index++;
                    continue;
                }

                // 单独 CR 也是真实换行；不从显示行里吞掉。
                AddDisplayLine(raw, start, index - start, realBreak: true, NewlineKind.Cr);
                start = index + 1;
                index++;
                continue;
            }

            if (text[index] == '\n')
            {
                var length = index - start;
                var kind = NewlineKind.Lf;

                if (length > 0 && NewlineEncoding.IsSentinel(text[index - 1]))
                {
                    kind = NewlineEncoding.KindOf(text[index - 1]);
                    length--;
                }
                else if (length > 0 && text[index - 1] == '\r')
                {
                    kind = NewlineKind.CrLf;
                    length--;
                }

                AddDisplayLine(raw, start, length, realBreak: true, kind);
                start = index + 1;
            }

            index++;
        }

        var tailLength = text.Length - start;
        while (tailLength > 0 && NewlineEncoding.IsSentinel(text[start + tailLength - 1]))
        {
            tailLength--;
        }

        AddDisplayLine(raw, start, tailLength, realBreak: false, NewlineKind.None);
    }

    private void AddDisplayLine(RawLine raw, int startInRaw, int length, bool realBreak, NewlineKind kind)
    {
        var line = new DisplayLine
        {
            StartInRaw = startInRaw,
            Length = length,
            RealBreak = realBreak,
            BreakKind = kind,
            Owner = raw,
        };

        raw.Segments.Add(line);
        _display.Add(line);
        LayoutLine(line, append: false);
    }

    private static void SetChannelBreak(RawLine raw, bool value)
    {
        if (raw.Segments.Count == 0)
        {
            return;
        }

        var last = raw.Segments[^1];
        if (last.RealBreak)
        {
            // 末尾真实换行后的空段也属于这一条；通道换行落在它之后。
        }

        last.ChannelBreak = value;
    }

    private void RemoveFrontRaw(int count)
    {
        var removedChars = 0;
        var removedSegments = 0;

        for (var i = 0; i < count; i++)
        {
            foreach (var segment in _raw[i].Segments)
            {
                removedChars += segment.Length + segment.BreakLength;
                removedSegments++;
                DisposeVisuals(segment);
            }
        }

        // 头部删除对应 `_display` 最前面的一段连续元素：一次 RemoveRange 顶掉 N 次 O(n) 查找。
        // 用首尾引用核对"这段确实连续"，异常形态（不该出现）退回逐条删除，保证正确性优先。
        if (removedSegments > 0
            && removedSegments <= _display.Count
            && _raw[0].Segments.Count > 0
            && _raw[count - 1].Segments.Count > 0
            && ReferenceEquals(_display[0], _raw[0].Segments[0])
            && ReferenceEquals(_display[removedSegments - 1], _raw[count - 1].Segments[^1]))
        {
            _display.RemoveRange(0, removedSegments);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                foreach (var segment in _raw[i].Segments)
                {
                    _ = _display.Remove(segment);
                }
            }
        }

        _raw.RemoveRange(0, count);

        // 选择偏移随头部删除一起左移（随后 RecomputeLayout 会再 clamp 一次）。
        _selectionStart = Math.Max(0, _selectionStart - removedChars);
        _selectionEnd = Math.Max(0, _selectionEnd - removedChars);
        _selectionAnchor = Math.Max(0, _selectionAnchor - removedChars);
    }

    private void AppendRawLine(string text, bool sealedLine = false)
    {
        if (_raw.Count > 0)
        {
            SetChannelBreak(_raw[^1], true);
        }

        var value = text ?? string.Empty;
        var raw = sealedLine
            ? new RawLine { SealedText = value, Index = _raw.Count }
            : new RawLine { Text = new StringBuilder(value), Index = _raw.Count };

        AddSegments(raw, value);
        _raw.Add(raw);

        if (!sealedLine)
        {
            _openRaw = raw;
        }
    }

    private void AppendSealedRawLine(string text) => AppendRawLine(text, sealedLine: true);

    private void AppendToRaw(RawLine raw, string suffix)
    {
        if (suffix.Length == 0)
        {
            return;
        }

        var baseOffset = raw.Length;
        raw.EnsureOpen();
        raw.Text!.Append(suffix);

        var last = raw.Segments.Count > 0 ? raw.Segments[^1] : null;

        if (last is null)
        {
            AddDisplayLine(raw, 0, 0, realBreak: false, NewlineKind.None);
            last = raw.Segments[^1];
        }

        var start = 0;
        var rawText = baseOffset > 0 ? raw.GetText() : string.Empty;

        for (var i = 0; i < suffix.Length; i++)
        {
            if (suffix[i] == '\r')
            {
                if (i + 1 < suffix.Length && suffix[i + 1] == '\n')
                {
                    // CRLF 交给后面的 LF 分支统一处理。
                    continue;
                }

                AppendToDisplayLine(last, suffix.Substring(start, i - start));
                last.RealBreak = true;
                last.BreakKind = NewlineKind.Cr;
                last.ChannelBreak = false;

                last = new DisplayLine
                {
                    StartInRaw = baseOffset + i + 1,
                    Owner = raw,
                };

                raw.Segments.Add(last);
                _display.Add(last);
                LayoutLine(last, append: false);
                start = i + 1;
                continue;
            }

            if (suffix[i] != '\n')
            {
                continue;
            }

            var length = i - start;
            var kind = NewlineKind.Lf;

            if (i > 0 && NewlineEncoding.IsSentinel(suffix[i - 1]))
            {
                kind = NewlineEncoding.KindOf(suffix[i - 1]);
                length--;
            }
            else if (i > 0 && suffix[i - 1] == '\r')
            {
                kind = NewlineKind.CrLf;
                length--;
            }
            else if (i == 0
                     && baseOffset > 0
                     && baseOffset - 1 < rawText.Length
                     && NewlineEncoding.IsSentinel(rawText[baseOffset - 1]))
            {
                kind = NewlineEncoding.KindOf(rawText[baseOffset - 1]);
            }
            else if (i == 0
                     && baseOffset > 0
                     && baseOffset - 1 < rawText.Length
                     && rawText[baseOffset - 1] == '\r')
            {
                kind = NewlineKind.CrLf;
            }

            AppendToDisplayLine(last, suffix.Substring(start, length));
            last.RealBreak = true;
            last.BreakKind = kind;
            last.ChannelBreak = false;

            last = new DisplayLine
            {
                StartInRaw = baseOffset + i + 1,
                Owner = raw,
            };

            raw.Segments.Add(last);
            _display.Add(last);
            LayoutLine(last, append: false);
            start = i + 1;
        }

        var tailLength = suffix.Length - start;
        while (tailLength > 0
               && NewlineEncoding.IsSentinel(suffix[start + tailLength - 1]))
        {
            tailLength--;
        }

        AppendToDisplayLine(last, suffix.Substring(start, tailLength));
    }

    private void AppendToDisplayLine(DisplayLine line, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        line.Length += text.Length;
        line.AppendProjection(text, ShowControlCharacterMarkers);
        line.RealBreak = false;
        line.BreakKind = NewlineKind.None;
        line.ChannelBreak = false;
        LayoutLine(line, append: true);
    }

    /// <summary>把"当前行"整行换成 <paramref name="text"/>（封口、替换、头部裁剪后都走这里）。</summary>
    private void ReplaceOpenLine(string text)
    {
        if (_openRaw is not null)
        {
            RemoveLastRaw();
            _openRaw = null;
        }

        if (text.Length == 0)
        {
            return;
        }

        AppendRawLine(text, sealedLine: false);
        _openRaw = _raw[^1];
    }

    /// <summary>把一段文本接到"当前行"后面（没有当前行时它就是当前行）。</summary>
    private void AppendOpenText(string suffix)
    {
        if (_openRaw is null)
        {
            AppendRawLine(suffix, sealedLine: false);
            _openRaw = _raw[^1];
            return;
        }

        AppendToRaw(_openRaw, suffix);
    }

    /// <summary>用缓冲里的文本整体替换一条 raw（已封口的"当前行"以缓冲为准）。</summary>
    private void ReplaceRawText(RawLine raw, string text)
    {
        var value = text ?? string.Empty;

        RemoveRawSegments(raw);
        raw.Segments.Clear();
        raw.SealedText = value;
        raw.Text = null;
        AddSegments(raw, value);
    }

    /// <summary>移除最后一条 raw（当前行被整行换掉时用）。</summary>
    private void RemoveLastRaw()
    {
        if (_raw.Count == 0)
        {
            return;
        }

        RemoveRawSegments(_raw[^1]);
        _raw.RemoveAt(_raw.Count - 1);

        if (_raw.Count > 0)
        {
            // 上一条现在是最后一条：它后面不再有换行。
            SetChannelBreak(_raw[^1], false);
        }
    }

    /// <summary>
    /// 把一条 raw 的显示行从 `_display` 里摘掉（**尾部整段删除**，一次搬移）。
    /// </summary>
    /// <param name="raw">要清掉显示行的 raw。</param>
    /// <remarks>
    /// 这条 raw 的显示行总是 `_display` 的末尾连续一段（raw 按顺序尾插、替换/移除也只发生在最后一条）：
    /// 逐条 `Remove` 会把"删一行"变成 O(总行数 × 本行行数)。
    /// </remarks>
    private void RemoveRawSegments(RawLine raw)
    {
        var count = raw.Segments.Count;
        if (count == 0)
        {
            return;
        }

        foreach (var segment in raw.Segments)
        {
            DisposeVisuals(segment);
        }

        if (count <= _display.Count
            && ReferenceEquals(_display[^1], raw.Segments[^1])
            && ReferenceEquals(_display[_display.Count - count], raw.Segments[0]))
        {
            _display.RemoveRange(_display.Count - count, count);
            return;
        }

        foreach (var segment in raw.Segments)
        {
            _ = _display.Remove(segment);
        }
    }

    private void LayoutLine(DisplayLine line, bool append)
    {
        if (_layoutWidth <= 0)
        {
            DisposeVisuals(line);
            return;
        }

        var projection = line.EnsureProjection(ShowControlCharacterMarkers);

        if (!append || line.Visuals.Count == 0)
        {
            DisposeVisuals(line);
            AppendChunk(line, projection.Display, startInLine: 0);
            return;
        }

        // 只重建最后一个视觉行：前面的视觉行已经固定。
        var last = line.Visuals[^1];
        var pendingStart = Math.Clamp(last.ProjectedStartInLine, 0, projection.DisplayLength);
        var pendingText = projection.Display.Substring(
            pendingStart,
            projection.DisplayLength - pendingStart);
        ReleaseLayout(last);
        line.Visuals.RemoveAt(line.Visuals.Count - 1);
        AppendChunk(line, pendingText, pendingStart);
    }

    private void AppendChunk(DisplayLine line, string text, int startInLine)
    {
        if (text.Length == 0)
        {
            AddVisualLine(line, startInLine, 0);
            return;
        }

        var pendingText = string.Empty;
        var pendingStart = startInLine;
        var offset = 0;

        while (offset < text.Length)
        {
            var length = Math.Min(MaxAppendChunk, text.Length - offset);
            var chunk = text.Substring(offset, length);
            offset += length;

            // **把上一块的“最后一个视觉行”拼到这一块前面再布局**：否则每 512 字符都会
            // 在块边界硬断一次，拉宽后就会看到没有规律的长短行。
            var combined = pendingText + chunk;
            var layout = CreateLayout(combined, wrap: true);
            var layoutLines = layout.TextLines;

            for (var i = 0; i < layoutLines.Count; i++)
            {
                var textLine = layoutLines[i];
                var sourceStart = Math.Clamp(textLine.FirstTextSourceIndex, 0, combined.Length);
                var textLength = Math.Clamp(textLine.Length, 0, combined.Length - sourceStart);

                if (i == layoutLines.Count - 1)
                {
                    // 最后一行还可能继续增长，留到下一块或追加时再布局。
                    pendingText = combined.Substring(sourceStart, textLength);
                    pendingStart += sourceStart;
                    break;
                }

                // **只记区间，不复制字符串**：可见窗口要画时才物化（见 `EnsureLine`）。
                // 每帧几百个 `Substring` 既产生垃圾，又把同一段文本在视图里复制一份。
                AddVisualLine(line, pendingStart + sourceStart, textLength);
            }
        }

        AddVisualLine(line, pendingStart, pendingText.Length);
    }

    private void AddVisualLine(DisplayLine line, int projectedStartInLine, int projectedLength)
    {
        // **只留区间，不建布局**：布局对象（TextLayout + TextLine，含整形后的字形位置）
        // 是这里最贵的东西；长缓冲下"每一行都缓存一份"会把内存推到与显示字符数成正比。
        // 真正要画/要命中的那几行才建（见 `EnsureLine`），窗口外的旧布局随手释放。
        var projection = line.EnsureProjection(ShowControlCharacterMarkers);
        var sourceStart = projection.DisplayToRaw(projectedStartInLine, preferRight: false);
        var sourceEnd = projection.DisplayToRaw(projectedStartInLine + projectedLength, preferRight: true);

        line.Visuals.Add(new VisualLine
        {
            ProjectedStartInLine = projectedStartInLine,
            ProjectedLength = projectedLength,
            SourceStartInLine = sourceStart,
            SourceLength = Math.Max(0, sourceEnd - sourceStart),
            Owner = line,
        });
    }

    private void RelayoutAll()
    {

        if (_layoutWidth <= 0)
        {
            return;
        }

        foreach (var line in _display)
        {
            DisposeVisuals(line);
            LayoutLine(line, append: false);
        }

        RecomputeLayout();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void InvalidateProjections()
    {
        foreach (var line in _display)
        {
            line.InvalidateProjection();
        }
    }

    private void RecomputeLayout()
    {
        _visuals.Clear();
        var flat = 0;
        var top = 0.0;
        var lineHeight = LineHeight;

        foreach (var line in _display)
        {
            line.FlatStart = flat;
            flat += line.Length + line.BreakLength;
            line.VisualStart = _visuals.Count;

            foreach (var visual in line.Visuals)
            {
                visual.Top = top;
                visual.Index = _visuals.Count;
                top += lineHeight;
                _visuals.Add(visual);
            }
        }

        _contentHeight = top;
        _flatEnd = flat;
        _selectionStart = Math.Clamp(_selectionStart, 0, flat);
        _selectionEnd = Math.Clamp(_selectionEnd, 0, flat);
    }

    /// <summary>
    /// 只重排从 <paramref name="startLine"/> 起新增/变化的显示行；追加时不必扫描整段文本。
    /// </summary>
    private void RecomputeLayoutFrom(int startLine, int startVisual)
    {
        if (startLine <= 0
            || startLine >= _display.Count
            || startVisual < 0
            || startVisual > _visuals.Count)
        {
            RecomputeLayout();
            return;
        }

        _visuals.RemoveRange(startVisual, _visuals.Count - startVisual);

        var previous = _display[startLine - 1];
        var flat = previous.FlatStart + previous.Length + previous.BreakLength;
        var lineHeight = LineHeight;
        var top = previous.Visuals.Count > 0
            ? previous.Visuals[^1].Top + lineHeight
            : _contentHeight;

        for (var index = startLine; index < _display.Count; index++)
        {
            var line = _display[index];
            line.FlatStart = flat;
            flat += line.Length + line.BreakLength;
            line.VisualStart = _visuals.Count;

            foreach (var visual in line.Visuals)
            {
                visual.Top = top;
                visual.Index = _visuals.Count;
                top += lineHeight;
                _visuals.Add(visual);
            }
        }

        _contentHeight = top;
        _flatEnd = flat;
        _selectionStart = Math.Clamp(_selectionStart, 0, flat);
        _selectionEnd = Math.Clamp(_selectionEnd, 0, flat);
    }

    /// <summary>单行高度（同字体下恒定）：只量一次，用来给没有布局的视觉行定位。</summary>
    private double LineHeight
    {
        get
        {
            if (_lineHeight > 0)
            {
                return _lineHeight;
            }

            if (_layoutWidth <= 0)
            {
                return FontSize * 1.4;
            }

            using var probe = CreateLayout("Ag", wrap: false);
            _lineHeight = probe.TextLines.Count > 0 ? probe.TextLines[0].Height : FontSize * 1.4;
            return _lineHeight;
        }
    }

    /// <summary>取得（必要时创建）某个视觉行的布局。</summary>
    private TextLine EnsureLine(VisualLine visual)
    {
        if (visual.Line is not null)
        {
            return visual.Line;
        }

        var projection = visual.Owner.EnsureProjection(ShowControlCharacterMarkers);
        visual.Materialized ??= visual.ProjectedLength == 0
            ? string.Empty
            : projection.Display.Substring(visual.ProjectedStartInLine, visual.ProjectedLength);

        var layout = CreateLayout(
            visual.Materialized,
            wrap: false,
            BuildMarkerOverrides(visual, projection));
        visual.Layout = layout;
        visual.Line = layout.TextLines[0];
        _laidOut.Add(visual);
        return visual.Line;
    }

    /// <summary>把当前视觉行里的标记段变成布局级前景色覆盖，避免再叠画一次造成重影。</summary>
    private List<ValueSpan<TextRunProperties>>? BuildMarkerOverrides(
        VisualLine visual,
        TextProjection projection)
    {
        if (MarkerBrush is null || !projection.HasMarkers || visual.ProjectedLength == 0)
        {
            return null;
        }

        List<ValueSpan<TextRunProperties>>? overrides = null;
        var visualStart = visual.ProjectedStartInLine;
        var visualEnd = visualStart + visual.ProjectedLength;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        var properties = new GenericTextRunProperties(
            typeface,
            FontSize,
            foregroundBrush: MarkerBrush);

        foreach (var segment in projection.Segments)
        {
            if (segment.Kind != TextProjectionKind.Marker)
            {
                continue;
            }

            var start = Math.Max(segment.DisplayStart, visualStart);
            var end = Math.Min(segment.DisplayEnd, visualEnd);

            if (end <= start)
            {
                continue;
            }

            overrides ??= [];
            overrides.Add(new ValueSpan<TextRunProperties>(
                start - visualStart,
                end - start,
                properties));
        }

        return overrides;
    }

    /// <summary>释放某个视觉行的布局（区间保留，下次要画再建）。</summary>
    private void ReleaseLayout(VisualLine visual)
    {
        if (visual.Layout is not IDisposable disposable)
        {
            visual.Layout = null;
            visual.Line = null;
            visual.Materialized = null;
            return;
        }

        disposable.Dispose();
        visual.Layout = null;
        visual.Line = null;
        visual.Materialized = null;
        _ = _laidOut.Remove(visual);
    }

    /// <summary>释放全部视觉行当前持有的布局（文本切片保留，下次要画再建）。</summary>
    private void ReleaseAllLayouts()
    {
        for (var index = _laidOut.Count - 1; index >= 0; index--)
        {
            ReleaseLayout(_laidOut[index]);
        }
    }

    /// <summary>释放一条显示行下所有视觉行的布局。</summary>
    private void DisposeVisuals(DisplayLine line)
    {
        foreach (var visual in line.Visuals)
        {
            ReleaseLayout(visual);
        }

        line.Visuals.Clear();
    }

    private TextLayout CreateLayout(
        string text,
        bool wrap,
        IReadOnlyList<ValueSpan<TextRunProperties>>? textStyleOverrides = null)
    {
        _layoutBuildCount++;

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        // 绝对色：主题缺失时的兜底墨色；正常路径由 Foreground 主题 brush 提供。
        var brush = Foreground ?? Brushes.Black;

        return new TextLayout(
            text,
            typeface,
            FontSize,
            brush,
            TextAlignment.Left,
            wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming.None,
            null,
            FlowDirection.LeftToRight,
            wrap ? _layoutWidth : double.PositiveInfinity,
            double.PositiveInfinity,
            textStyleOverrides: textStyleOverrides);
    }

    private void DrawSelection(
        DrawingContext context,
        VisualLine visual,
        TextLine line,
        double x,
        double y,
        IBrush brush,
        int selStart,
        int selEnd)
    {
        if (selEnd <= selStart || visual.SourceLength == 0)
        {
            return;
        }

        var flatStart = visual.Owner.FlatStart + visual.SourceStartInLine;
        var flatEnd = flatStart + visual.SourceLength;
        var start = Math.Max(selStart, flatStart);
        var end = Math.Min(selEnd, flatEnd);

        if (end <= start)
        {
            return;
        }

        var projection = visual.Owner.EnsureProjection(ShowControlCharacterMarkers);
        var localSourceStart = start - flatStart;
        var localSourceEnd = end - flatStart;
        var projectedStart = projection.RawToDisplay(
            visual.SourceStartInLine + localSourceStart,
            preferRight: false) - visual.ProjectedStartInLine;
        var projectedEnd = projection.RawToDisplay(
            visual.SourceStartInLine + localSourceEnd,
            preferRight: true) - visual.ProjectedStartInLine;

        projectedStart = Math.Clamp(projectedStart, 0, visual.ProjectedLength);
        projectedEnd = Math.Clamp(projectedEnd, projectedStart, visual.ProjectedLength);

        if (projectedEnd <= projectedStart)
        {
            return;
        }

        try
        {
            foreach (var bounds in line.GetTextBounds(projectedStart, projectedEnd - projectedStart))
            {
                var rect = bounds.Rectangle.Translate(new Vector(x, y));
                context.FillRectangle(brush, rect);
            }
        }
        catch (ArgumentException)
        {
            // 极端选区/布局边界：宁可少画一块高亮，不让界面崩。
        }
    }

    private void DrawMark(DrawingContext context, double x, double y, IBrush brush, string text)
    {
        var mark = MarkFormatted(brush, text);
        context.DrawText(mark, new Point(x, y));
    }

    private FormattedText MarkFormatted(IBrush brush, string text)
    {
        var size = Math.Max(1, FontSize * 0.85);

        if (_newlineMarks.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight),
            size,
            brush);

        _newlineMarks[text] = formatted;
        return formatted;
    }

    private double AlignmentOffset(double width, double lineWidth) => TextAlignment switch
    {
        TextAlignment.Center => Math.Max(0, (width - lineWidth) / 2),
        TextAlignment.Right => Math.Max(0, width - lineWidth),
        _ => 0,
    };

    private int HitTestOffset(Point point)
    {
        if (_visuals.Count == 0)
        {
            return 0;
        }

        var contentWidth = Math.Max(0, Bounds.Width - Padding.Left - Padding.Right);
        var y = point.Y - Padding.Top;
        var lineHeight = LineHeight;

        foreach (var visual in _visuals)
        {
            var top = visual.Top;
            var bottom = top + lineHeight;

            if (y < top || y > bottom)
            {
                continue;
            }

            var line = EnsureLine(visual);
            var x = point.X - Padding.Left - AlignmentOffset(contentWidth, line.Width);
            var hit = line.GetCharacterHitFromDistance(Math.Max(0, x));
            var projectedLocal = Math.Clamp(hit.FirstCharacterIndex, 0, visual.ProjectedLength);
            var projection = visual.Owner.EnsureProjection(ShowControlCharacterMarkers);
            var rawLocal = projection.DisplayToRaw(
                visual.ProjectedStartInLine + projectedLocal,
                preferRight: false) - visual.SourceStartInLine;

            rawLocal = Math.Clamp(rawLocal, 0, visual.SourceLength);
            return visual.Owner.FlatStart + visual.SourceStartInLine + rawLocal;
        }

        return TextLength;
    }

    private void ScrollToEndIfNeeded(bool appended)
    {
        if (!appended || !AutoScroll || _scroll is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _scroll?.ScrollToEnd(), DispatcherPriority.Render);
    }

    private string BuildText()
    {
        if (_display.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();

        foreach (var line in _display)
        {
            builder.Append(line.GetText());

            if (line.RealBreak)
            {
                builder.Append(NewlineEncoding.SequenceOf(line.BreakKind));
            }
            else if (line.ChannelBreak)
            {
                // 通道换行不是数据；复制/保存时按当前平台的换行惯例落成真实换行。
                builder.Append(Environment.NewLine);
            }
        }

        return builder.ToString();
    }

    private sealed class RawLine
    {
        /// <summary>已封口行：直接引用缓冲给出的字符串，不复制。</summary>
        public string? SealedText;

        /// <summary>当前行（未封口）的追加缓冲；封口行不分配。</summary>
        public StringBuilder? Text;

        public int Index;

        public List<DisplayLine> Segments { get; } = [];

        public int Length => SealedText?.Length ?? Text?.Length ?? 0;

        public string GetText() => SealedText ?? Text?.ToString() ?? string.Empty;

        public string Slice(int start, int length) =>
            SealedText is { } text
                ? text.Substring(start, length)
                : Text?.ToString(start, length) ?? string.Empty;

        /// <summary>把封口行转回可追加的当前行（只对真正要追加的行调用）。</summary>
        public void EnsureOpen()
        {
            if (SealedText is { } text)
            {
                Text = new StringBuilder(text);
                SealedText = null;
                return;
            }

            Text ??= new StringBuilder();
        }
    }

    private sealed class DisplayLine
    {
        /// <summary>本条显示行在 <see cref="Owner"/> 的 raw 文本里的起点。</summary>
        public int StartInRaw;

        /// <summary>本条显示行的字符数（不含真实换行符）。</summary>
        public int Length;

        public bool RealBreak;

        public NewlineKind BreakKind;

        public bool ChannelBreak;

        public bool HasBreak => RealBreak || ChannelBreak;

        /// <summary>这个换行在源文本里占几个字符（CRLF=2，CR/LF=1，通道换行=平台换行长度）。</summary>
        public int BreakLength => RealBreak
            ? BreakKind switch
            {
                NewlineKind.CrLf => 2,
                NewlineKind.Cr or NewlineKind.Lf => 1,
                _ => 0,
            }
            : ChannelBreak ? Environment.NewLine.Length : 0;

        public int FlatStart;

        public int VisualStart;

        public RawLine Owner = null!;

        /// <summary>本显示行的显示投影（不可显示字符 → 标记/方框）。</summary>
        private TextProjection? _projection;

        private bool _projectionMarkers;

        public List<VisualLine> Visuals { get; } = [];

        /// <summary>取整条显示行文本（复制；仅 Text/复制等非热路径用）。</summary>
        public string GetText() => Owner.Slice(StartInRaw, Length);

        /// <summary>取显示行内从 <paramref name="start"/> 起的 <paramref name="length"/> 个字符（复制）。</summary>
        public string GetText(int start, int length) =>
            Owner.Slice(StartInRaw + start, length);

        /// <summary>取得（必要时重建）显示投影。</summary>
        public TextProjection EnsureProjection(bool markers)
        {
            if (_projection is null || _projectionMarkers != markers)
            {
                _projection = TextProjection.Create(GetText(), markers);
                _projectionMarkers = markers;
            }

            return _projection;
        }

        /// <summary>追加后缀到投影（只对当前行尾追加调用）。</summary>
        public void AppendProjection(string text, bool markers)
        {
            if (_projection is null || _projectionMarkers != markers)
            {
                _projection = TextProjection.Create(GetText(), markers);
                _projectionMarkers = markers;
                return;
            }

            _projection.Append(text);
        }

        /// <summary>内容整体变化后丢弃投影。</summary>
        public void InvalidateProjection() => _projection = null;
    }

    private sealed class VisualLine
    {
        /// <summary>布局（**懒建**：只有可见/命中用到的行才有；其余为 <see langword="null"/>）。</summary>
        public TextLayout? Layout;

        /// <summary>本行的排版结果（与 <see cref="Layout"/> 同生同灭）。</summary>
        public TextLine? Line;

        /// <summary>本行在**投影文本**里的起点。</summary>
        public int ProjectedStartInLine;

        /// <summary>本行在**投影文本**里的字符数。</summary>
        public int ProjectedLength;

        /// <summary>本行对应的源文本起点（显示行内）。</summary>
        public int SourceStartInLine;

        /// <summary>本行对应的源文本字符数。</summary>
        public int SourceLength;

        /// <summary>可见窗口内才物化的字符串（与布局同生命周期；滚出即释放）。</summary>
        public string? Materialized;

        /// <summary>在 `_visuals` 里的下标（每次 `RecomputeLayout` 重写；释放窗口外布局时用）。</summary>
        public int Index;

        public double Top;

        public DisplayLine Owner = null!;
    }
}
