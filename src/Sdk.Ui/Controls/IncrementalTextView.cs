using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Text;

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
/// 显示刷新由外部按全局节拍调用 <see cref="ApplySnapshot"/>；没有新内容时一行都不重排。
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

    /// <summary>追加内容后是否自动滚到尾部。</summary>
    public static readonly StyledProperty<bool> AutoScrollProperty =
        AvaloniaProperty.Register<IncrementalTextView, bool>(nameof(AutoScroll), defaultValue: true);

    /// <summary>文本水平对齐。</summary>
    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<IncrementalTextView, TextAlignment>(nameof(TextAlignment));

    private const int MaxAppendChunk = 512;

    private readonly List<RawLine> _raw = [];
    private readonly List<DisplayLine> _display = [];
    private readonly List<VisualLine> _visuals = [];

    private double _layoutWidth = -1;
    private double _contentHeight;
    private long _revision = -1;
    private int _selectionStart;
    private int _selectionEnd;
    private int _selectionAnchor;
    private bool _selecting;
    private ScrollViewer? _scroll;
    private FormattedText? _markText;
    private double _markFontSize;
    private int _layoutBuildCount;

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
    public string Text => BuildText().Replace("\n", Environment.NewLine, StringComparison.Ordinal);

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
            return selected.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }
    }

    /// <summary>是否有可复制内容。</summary>
    public bool CanCopy => _selectionStart != _selectionEnd;

    /// <summary>当前真实换行标记数（测试/诊断用）。</summary>
    public int MarkCount => ShowNewlineMarks ? _display.Count(line => line.RealBreak) : 0;

    /// <summary>视觉行数（测试/诊断用）。</summary>
    public int VisualLineCount => _visuals.Count;

    /// <summary>每个视觉行的字符数（测试/诊断用；用于断言折行连续、没有块边界硬断）。</summary>
    public IReadOnlyList<int> VisualLineLengths => _visuals.Select(visual => visual.Text.Length).ToArray();

    /// <summary>已创建的 <see cref="TextLayout"/> 数（增量性探针用）。</summary>
    public int LayoutBuildCount => _layoutBuildCount;

    /// <summary>当前扁平文本长度。</summary>
    public int TextLength
    {
        get
        {
            var length = 0;
            foreach (var line in _display)
            {
                length += line.Text.Length + (line.HasBreak ? 1 : 0);
            }

            return length;
        }
    }

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
    /// 应用一次显示缓冲快照（每帧调用；变更号相同 ⇒ 立即返回，不做任何工作）。
    /// </summary>
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

        var appended = false;

        if (_raw.Count == 0)
        {
            Rebuild(lines);
            _revision = revision;
            InvalidateMeasure();
            InvalidateVisual();
            return true;
        }

        if (TryApplyAppend(lines, out appended))
        {
            _revision = revision;
            RecomputeLayout();
            InvalidateMeasure();
            InvalidateVisual();
            ScrollToEndIfNeeded(appended);
            return true;
        }

        if (TryApplyRolling(lines))
        {
            appended = true;
            _revision = revision;
            RecomputeLayout();
            InvalidateMeasure();
            InvalidateVisual();
            ScrollToEndIfNeeded(appended);
            return true;
        }

        // 非追加/尾部裁剪（清空、替换、任意修改）⇒ 重建；这类操作不在热路径上。
        Rebuild(lines);
        _revision = revision;
        InvalidateMeasure();
        InvalidateVisual();
        return true;
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

        if (width > 0 && Math.Abs(width - _layoutWidth) > 0.01)
        {
            _layoutWidth = width;
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

        if (change.Property == ForegroundProperty
            || change.Property == FontFamilyProperty
            || change.Property == FontSizeProperty
            || change.Property == FontWeightProperty
            || change.Property == FontStyleProperty)
        {
            _markText = null;
            RelayoutAll();
            InvalidateMeasure();
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
        var selStart = Math.Min(_selectionStart, _selectionEnd);
        var selEnd = Math.Max(_selectionStart, _selectionEnd);

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            foreach (var visual in _visuals)
            {
                var top = Padding.Top + visual.Top;
                var height = visual.Line.Height;

                if (top + height < offset || top > offset + viewport)
                {
                    continue;
                }

                var y = top;
                var x = Padding.Left + AlignmentOffset(contentWidth, visual.Line.Width);
                DrawSelection(context, visual, x, y, selection, selStart, selEnd);
                visual.Line.Draw(context, new Point(x, y));

                if (ShowNewlineMarks && visual.Owner.RealBreak && ReferenceEquals(visual, visual.Owner.Visuals[^1]))
                {
                    DrawMark(context, x + visual.Line.Width + 2, y + ((height - MarkFormatted(brush).Height) / 2), brush);
                }
            }
        }
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
        _raw.Clear();
        _display.Clear();
        _visuals.Clear();

        for (var i = 0; i < lines.Count; i++)
        {
            var raw = new RawLine { Text = lines[i] ?? string.Empty, Index = i };
            AddSegments(raw);
            _raw.Add(raw);
        }

        for (var i = 0; i < _raw.Count - 1; i++)
        {
            SetChannelBreak(_raw[i], true);
        }

        RecomputeLayout();
    }

    private void AddSegments(RawLine raw)
    {
        var text = raw.Text;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            var segmentText = text[start..i];
            if (segmentText.EndsWith('\r'))
            {
                segmentText = segmentText[..^1];
            }

            AddDisplayLine(raw, segmentText, realBreak: true);
            start = i + 1;
        }

        var last = text[start..];
        if (last.EndsWith('\r'))
        {
            last = last[..^1];
        }

        AddDisplayLine(raw, last, realBreak: false);
    }

    private void AddDisplayLine(RawLine raw, string text, bool realBreak)
    {
        var line = new DisplayLine
        {
            Text = text,
            RealBreak = realBreak,
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

    private bool TryApplyAppend(IReadOnlyList<string> lines, out bool appended)
    {
        appended = false;

        var oldCount = _raw.Count;
        var newCount = lines.Count;

        if (newCount < oldCount)
        {
            return false;
        }

        // 前面的原始行必须一一相同；只允许“最后一行变长 / 尾部新增行”。
        for (var i = 0; i < oldCount - 1; i++)
        {
            if (!string.Equals(_raw[i].Text, lines[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (string.Equals(_raw[^1].Text, lines[oldCount - 1], StringComparison.Ordinal))
        {
            if (newCount == oldCount)
            {
                return true; // 变更号变了但内容一样（例如只改了丢弃计数）
            }

            for (var i = oldCount; i < newCount; i++)
            {
                AppendRawLine(lines[i]);
            }

            appended = true;
            return true;
        }

        if (!lines[oldCount - 1].StartsWith(_raw[^1].Text, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = lines[oldCount - 1][_raw[^1].Text.Length..];
        AppendToRaw(_raw[^1], suffix);
        _raw[^1].Text = lines[oldCount - 1];

        for (var i = oldCount; i < newCount; i++)
        {
            AppendRawLine(lines[i]);
        }

        appended = true;
        return true;
    }

    private bool TryApplyRolling(IReadOnlyList<string> lines)
    {
        var oldCount = _raw.Count;
        var newCount = lines.Count;

        if (newCount == 0 || newCount > oldCount)
        {
            return false;
        }

        if (newCount == oldCount && string.Equals(_raw[0].Text, lines[0], StringComparison.Ordinal))
        {
            return false; // 等长且头部没变：这是“最后一行变化”，不是删头+追尾
        }

        // 容量到顶时缓冲会“删头 + 追尾”同时发生；只允许从头部裁掉少量行（正常每帧 1 行）。
        // 这里做的是“先找到旧列表的一个后缀 == 新列表的前缀”，再删头、把余下部分按追加处理；
        // 不能退回 Rebuild——那会在 20k 行时每追加一行都重排全文。
        var maxRemove = Math.Min(oldCount - 1, 64);

        for (var remove = 1; remove <= maxRemove; remove++)
        {
            var remaining = oldCount - remove;
            var compare = Math.Min(remaining, newCount);
            var match = compare > 0;

            for (var i = 0; i < compare; i++)
            {
                if (!string.Equals(_raw[remove + i].Text, lines[i], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (!match)
            {
                continue;
            }

            RemoveFrontRaw(remove);
            return TryApplyAppend(lines, out _);
        }

        return false;
    }

    private void RemoveFrontRaw(int count)
    {
        var removedChars = 0;

        for (var i = 0; i < count; i++)
        {
            foreach (var segment in _raw[i].Segments)
            {
                removedChars += segment.Text.Length + (segment.HasBreak ? 1 : 0);
                _ = _display.Remove(segment);
            }
        }

        _raw.RemoveRange(0, count);

        // 选择偏移随头部删除一起左移（随后 RecomputeLayout 会再 clamp 一次）。
        _selectionStart = Math.Max(0, _selectionStart - removedChars);
        _selectionEnd = Math.Max(0, _selectionEnd - removedChars);
        _selectionAnchor = Math.Max(0, _selectionAnchor - removedChars);
    }

    private void AppendRawLine(string text)
    {
        if (_raw.Count > 0)
        {
            SetChannelBreak(_raw[^1], true);
        }

        var raw = new RawLine { Text = text ?? string.Empty, Index = _raw.Count };
        AddSegments(raw);
        _raw.Add(raw);
    }

    private void AppendToRaw(RawLine raw, string suffix)
    {
        if (suffix.Length == 0)
        {
            return;
        }

        var last = raw.Segments.Count > 0 ? raw.Segments[^1] : null;

        if (last is null)
        {
            AddDisplayLine(raw, string.Empty, realBreak: false);
            last = raw.Segments[^1];
        }

        var start = 0;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (suffix[i] != '\n')
            {
                continue;
            }

            var part = suffix[start..i];
            if (part.EndsWith('\r'))
            {
                part = part[..^1];
            }

            AppendToDisplayLine(last, part);
            last.RealBreak = true;
            last.ChannelBreak = false;

            last = new DisplayLine { Text = string.Empty, Owner = raw };
            raw.Segments.Add(last);
            _display.Add(last);
            LayoutLine(last, append: false);
            start = i + 1;
        }

        var tail = suffix[start..];
        if (tail.EndsWith('\r'))
        {
            tail = tail[..^1];
        }

        AppendToDisplayLine(last, tail);
    }

    private void AppendToDisplayLine(DisplayLine line, string suffix)
    {
        if (suffix.Length == 0)
        {
            return;
        }

        line.Text += suffix;
        line.RealBreak = false;
        line.ChannelBreak = false;
        LayoutLine(line, append: true);
    }

    private void LayoutLine(DisplayLine line, bool append)
    {
        if (_layoutWidth <= 0)
        {
            line.Visuals.Clear();
            return;
        }

        if (!append || line.Visuals.Count == 0)
        {
            line.Visuals.Clear();
            AppendChunk(line, line.Text, startInLine: 0);
            return;
        }

        // 只重建最后一个视觉行：前面的视觉行已经固定。
        var last = line.Visuals[^1];
        var pendingText = line.Text[last.StartInLine..];
        line.Visuals.RemoveAt(line.Visuals.Count - 1);
        AppendChunk(line, pendingText, last.StartInLine);
    }

    private void AppendChunk(DisplayLine line, string text, int startInLine)
    {
        if (text.Length == 0)
        {
            AddVisualLine(line, string.Empty, startInLine);
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
                var slice = combined.Substring(sourceStart, textLength);

                if (i == layoutLines.Count - 1)
                {
                    // 最后一行还可能继续增长，留到下一块或追加时再布局。
                    pendingText = slice;
                    pendingStart += sourceStart;
                    break;
                }

                AddVisualLine(line, slice, pendingStart + sourceStart);
            }
        }

        AddVisualLine(line, pendingText, pendingStart);
    }

    private void AddVisualLine(DisplayLine line, string text, int startInLine)
    {
        // **每个视觉行一个独立的 NoWrap 布局**：命中位置/选择矩形都以“本行起点”为 0，
        // 不从多行 TextLayout 的全局偏移推算（那条路径在折行后会算错选择偏移）。
        var visualLayout = CreateLayout(text, wrap: false);
        line.Visuals.Add(new VisualLine
        {
            Layout = visualLayout,
            Line = visualLayout.TextLines[0],
            Text = text,
            StartInLine = startInLine,
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
            line.Visuals.Clear();
            LayoutLine(line, append: false);
        }

        RecomputeLayout();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RecomputeLayout()
    {
        _visuals.Clear();
        var flat = 0;
        var top = 0.0;

        foreach (var line in _display)
        {
            line.FlatStart = flat;
            flat += line.Text.Length + (line.HasBreak ? 1 : 0);

            foreach (var visual in line.Visuals)
            {
                visual.Top = top;
                top += visual.Line.Height;
                _visuals.Add(visual);
            }
        }

        _contentHeight = top;
        _selectionStart = Math.Clamp(_selectionStart, 0, flat);
        _selectionEnd = Math.Clamp(_selectionEnd, 0, flat);
    }

    private TextLayout CreateLayout(string text, bool wrap)
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
            double.PositiveInfinity);
    }

    private static void DrawSelection(
        DrawingContext context,
        VisualLine visual,
        double x,
        double y,
        IBrush brush,
        int selStart,
        int selEnd)
    {
        if (selEnd <= selStart || visual.Text.Length == 0)
        {
            return;
        }

        var flatStart = visual.Owner.FlatStart + visual.StartInLine;
        var flatEnd = flatStart + visual.Text.Length;
        var start = Math.Max(selStart, flatStart);
        var end = Math.Min(selEnd, flatEnd);

        if (end <= start)
        {
            return;
        }

        var localStart = start - flatStart;
        var localEnd = end - flatStart;

        try
        {
            foreach (var bounds in visual.Line.GetTextBounds(localStart, localEnd - localStart))
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

    private void DrawMark(DrawingContext context, double x, double y, IBrush brush)
    {
        var mark = MarkFormatted(brush);
        context.DrawText(mark, new Point(x, y));
    }

    private FormattedText MarkFormatted(IBrush brush)
    {
        var size = Math.Max(1, FontSize * 0.85);

        if (_markText is not null && Math.Abs(_markFontSize - size) < 0.01)
        {
            return _markText;
        }

        _markFontSize = size;
        _markText = new FormattedText(
            "↵",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight),
            size,
            brush);

        return _markText;
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

        foreach (var visual in _visuals)
        {
            var top = visual.Top;
            var bottom = top + visual.Line.Height;

            if (y < top || y > bottom)
            {
                continue;
            }

            var x = point.X - Padding.Left - AlignmentOffset(contentWidth, visual.Line.Width);
            var hit = visual.Line.GetCharacterHitFromDistance(Math.Max(0, x));
            var local = Math.Clamp(hit.FirstCharacterIndex, 0, visual.Text.Length);
            return visual.Owner.FlatStart + visual.StartInLine + local;
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
            builder.Append(line.Text);

            if (line.HasBreak)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private sealed class RawLine
    {
        public string Text = string.Empty;

        public int Index;

        public List<DisplayLine> Segments { get; } = [];
    }

    private sealed class DisplayLine
    {
        public string Text = string.Empty;

        public bool RealBreak;

        public bool ChannelBreak;

        public bool HasBreak => RealBreak || ChannelBreak;

        public int FlatStart;

        public RawLine Owner = null!;

        public List<VisualLine> Visuals { get; } = [];
    }

    private sealed class VisualLine
    {
        public TextLayout Layout = null!;

        public TextLine Line = null!;

        public string Text = string.Empty;

        public int StartInLine;

        public double Top;

        public DisplayLine Owner = null!;
    }
}
