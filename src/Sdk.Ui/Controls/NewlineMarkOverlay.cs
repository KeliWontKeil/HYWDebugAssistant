using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **行末换行标记（`↵`）的绘制层**。
/// </summary>
/// <remarks>
/// <para>
/// 目标可以是可编辑的 <see cref="TextBox"/>，也可以是只读的 <see cref="SelectableTextBlock"/>：
/// 两者都提供 `TextLayout` 与真实文本，标记算法只有一份。只读面没有光标，但文本仍可选中/复制。
/// </para>
/// <para>
/// `↵` 是纯装饰：不进文本、不参与选择、光标跳过、删不掉。通道换行（显示层拼行时插进去的 `\n`）
/// 由 <see cref="UnmarkedNewlines"/> 排除；只有数据里真实存在的换行字符才画。
/// </para>
/// </remarks>
public sealed class NewlineMarkOverlay : Control
{
    /// <summary>**行末换行标记**（与显示区同一个字符）。</summary>
    public const string NewlineMark = "↵";

    private const double MarkGap = 2.0;
    private const double MarkFontScale = 0.85;
    // 绝对色：主题缺失时的兜底墨色；正常路径读 TextMuted。
    private static readonly IBrush AbsoluteMarkInk = Brushes.Gray;

    /// <summary>要标注的文本呈现目标（TextBox 或 SelectableTextBlock）。</summary>
    public static readonly StyledProperty<Control?> TargetProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, Control?>(nameof(Target));

    /// <summary>是否启用。</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, bool>(nameof(IsActive), defaultValue: true);

    /// <summary>不该画 `↵` 的换行符偏移（通道换行）。</summary>
    public static readonly StyledProperty<IReadOnlyCollection<int>?> UnmarkedNewlinesProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, IReadOnlyCollection<int>?>(nameof(UnmarkedNewlines));

    /// <summary>不可显示字符标记的绘制颜色。</summary>
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, IBrush?>(nameof(MarkerBrush));

    /// <summary>C 转义组的绘制颜色。</summary>
    public static readonly StyledProperty<IBrush?> EscapeBrushProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, IBrush?>(nameof(EscapeBrush));

    private readonly List<(Point Position, string Text)> _marks = [];
    private readonly List<(Point Position, string Text)> _spanMarks = [];
    private IReadOnlyList<TextMarkerSpan>? _markerSpans;
    private IReadOnlyList<TextMarkerSpan>? _escapeSpans;
    private HashSet<int>? _unmarked;
    private Control? _subscribed;
    private ScrollViewer? _scrollViewer;
    private readonly Dictionary<NewlineKind, FormattedText> _markTexts = [];
    private readonly Dictionary<string, FormattedText> _markerTexts = [];
    private double _markFontSize;
    private double _markerFontSize;
    private IBrush? _markerBrush;

    /// <summary>创建绘制层（不参与命中、不参与布局输入）。</summary>
    public NewlineMarkOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        AttachedToVisualTree += (_, _) => EnsureScrollViewer();
    }

    /// <summary>要标注的文本呈现目标。</summary>
    public Control? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>是否启用。</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>不该画 `↵` 的换行符偏移（通道换行）。</summary>
    public IReadOnlyCollection<int>? UnmarkedNewlines
    {
        get => GetValue(UnmarkedNewlinesProperty);
        set => SetValue(UnmarkedNewlinesProperty, value);
    }

    /// <summary>当前画出的标记（测试/调试读取）。</summary>
    public IReadOnlyList<(Point Position, string Text)> Marks => _marks;

    /// <summary>当前画出的着色文本段（测试/调试读取）。</summary>
    internal IReadOnlyList<(Point Position, string Text)> SpanMarks => _spanMarks;

    /// <summary>不可显示字符标记的绘制颜色。</summary>
    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>C 转义组的绘制颜色。</summary>
    public IBrush? EscapeBrush
    {
        get => GetValue(EscapeBrushProperty);
        set => SetValue(EscapeBrushProperty, value);
    }

    /// <summary>需要警示色叠画的标记区间（显示坐标）。</summary>
    internal IReadOnlyList<TextMarkerSpan>? MarkerSpans
    {
        get => _markerSpans;
        set
        {
            _markerSpans = value;
            InvalidateVisual();
        }
    }

    /// <summary>需要蓝色叠画的 C 转义组（显示坐标）。</summary>
    internal IReadOnlyList<TextMarkerSpan>? EscapeSpans
    {
        get => _escapeSpans;
        set
        {
            _escapeSpans = value;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TargetProperty)
        {
            Resubscribe(change.GetNewValue<Control?>());
            return;
        }

        if (change.Property == IsActiveProperty)
        {
            InvalidateVisual();
            return;
        }

        if (change.Property == UnmarkedNewlinesProperty)
        {
            var offsets = change.GetNewValue<IReadOnlyCollection<int>?>();
            _unmarked = offsets is { Count: > 0 } ? [.. offsets] : null;
            InvalidateVisual();
            return;
        }

        if (change.Property == MarkerBrushProperty || change.Property == EscapeBrushProperty)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _marks.Clear();
        _spanMarks.Clear();

        if (Target is not { } target || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        EnsureScrollViewerIfMissing();

        if (!TryGetLayout(target, out var layout, out var text, out var origin))
        {
            return;
        }

        if (IsActive)
        {
            var marks = MarkLines(layout, text, _unmarked);
            var top = origin.Y;

            for (var index = 0; index < layout.TextLines.Count; index++)
            {
                var line = layout.TextLines[index];
                var height = line.Height;

                if (marks.TryGetValue(index, out var kinds))
                {
                    var x = origin.X + line.Width + MarkGap;

                    foreach (var kind in kinds)
                    {
                        var font = MarkFormatted(target, kind);
                        var y = top + ((height - font.Height) / 2);

                        if (y + font.Height >= 0 && y <= Bounds.Height)
                        {
                            context.DrawText(font, new Point(x, y));
                            _marks.Add((new Point(x, y), NewlineEncoding.MarkOf(kind)));
                            x += font.Width;
                        }
                    }
                }

                top += height;
            }
        }

        DrawTextSpans(context, target, layout, text, origin, _markerSpans, MarkerBrush);
        DrawTextSpans(context, target, layout, text, origin, _escapeSpans, EscapeBrush);
    }

    private void DrawTextSpans(
        DrawingContext context,
        Control target,
        Avalonia.Media.TextFormatting.TextLayout layout,
        string text,
        Point origin,
        IReadOnlyList<TextMarkerSpan>? spans,
        IBrush? brush)
    {
        if (spans is not { Count: > 0 } || brush is null || text.Length == 0)
        {
            return;
        }

        var top = origin.Y;

        for (var index = 0; index < layout.TextLines.Count; index++)
        {
            var line = layout.TextLines[index];
            var lineStart = line.FirstTextSourceIndex;
            var lineEnd = Math.Min(lineStart + line.Length, text.Length);

            if (lineEnd > lineStart)
            {
                foreach (var span in spans)
                {
                    var spanStart = Math.Max(span.Start, lineStart);
                    var spanEnd = Math.Min(span.Start + span.Length, lineEnd);

                    if (spanEnd <= spanStart)
                    {
                        continue;
                    }

                    var length = spanEnd - spanStart;
                    var piece = text.Substring(spanStart, length);
                    var formatted = MarkerFormatted(target, piece, brush);

                    try
                    {
                        // GetTextBounds 的索引是整段 TextLayout 的源索引，不是行内索引；
                        // 同一段只画一次，避免每个 bounds 把整段重复画一遍造成“重影/叠字”。
                        var bounds = line.GetTextBounds(spanStart, length);

                        if (bounds.Count > 0)
                        {
                            var left = bounds[0].Rectangle.Left;
                            var y = top + line.Baseline - formatted.Baseline;
                            var position = new Point(origin.X + left, y);
                            context.DrawText(formatted, position);
                            _spanMarks.Add((position, piece));
                        }
                    }
                    catch (ArgumentException)
                    {
                        // 极端布局边界：宁可少画一段颜色，不让界面崩。
                    }
                }
            }

            top += line.Height;
        }
    }

    private FormattedText MarkerFormatted(Control target, string text, IBrush brush)
    {
        var size = FontSizeOf(target);

        if (_markerTexts.TryGetValue(text, out var cached)
            && Math.Abs(_markerFontSize - size) < 0.01
            && ReferenceEquals(_markerBrush, brush))
        {
            return cached;
        }

        _markerFontSize = size;
        _markerBrush = brush;

        var family = target is TextBlock block ? block.FontFamily : FontFamilyOf(target);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(family),
            size,
            brush);

        _markerTexts[text] = formatted;
        return formatted;
    }

    private bool TryGetLayout(
        Control target,
        out Avalonia.Media.TextFormatting.TextLayout layout,
        out string text,
        out Point origin)
    {
        layout = null!;
        text = string.Empty;
        origin = default;

        if (target is TextBox box)
        {
            var presenter = FindPresenter(box);

            if (presenter?.TextLayout is not { } boxLayout || box.Text is not { } boxText)
            {
                return false;
            }

            if (presenter.TranslatePoint(default, this) is not { } boxOrigin)
            {
                return false;
            }

            layout = boxLayout;
            text = boxText;
            origin = boxOrigin;
            return true;
        }

        if (target is TextBlock block)
        {
            if (block.TextLayout is not { } blockLayout || block.Text is not { } blockText)
            {
                return false;
            }

            if (block.TranslatePoint(new Point(block.Padding.Left, block.Padding.Top), this) is not { } blockOrigin)
            {
                return false;
            }

            layout = blockLayout;
            text = blockText;
            origin = blockOrigin;
            return true;
        }

        return false;
    }

    /// <summary>找到文本框里的文本呈现器。</summary>
    private static TextPresenter? FindPresenter(TextBox editor) =>
        editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();

    /// <summary>找到承载滚动的那一层。</summary>
    private static ScrollViewer? FindScrollViewer(Control target)
    {
        if (target is TextBox box)
        {
            return box.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        }

        return target.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
    }

    private FormattedText MarkFormatted(Control target, NewlineKind kind)
    {
        var size = Math.Max(1, FontSizeOf(target) * MarkFontScale);

        if (_markTexts.TryGetValue(kind, out var cached) && Math.Abs(_markFontSize - size) < 0.01)
        {
            return cached;
        }

        _markFontSize = size;

        var ink = target.TryFindResource("TextMuted", out var resource) && resource is IBrush themed
            ? themed
            : AbsoluteMarkInk;

        var family = target is TextBlock block ? block.FontFamily : FontFamilyOf(target);

        var mark = new FormattedText(
            NewlineEncoding.MarkOf(kind),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(family),
            size,
            ink);

        _markTexts[kind] = mark;
        return mark;
    }

    /// <summary>逐视觉行算“该画哪种换行标记”。</summary>
    private static Dictionary<int, List<NewlineKind>> MarkLines(
        Avalonia.Media.TextFormatting.TextLayout layout,
        string text,
        HashSet<int>? unmarked)
    {
        var marks = new Dictionary<int, List<NewlineKind>>();
        var offset = 0;

        for (var index = 0; index < layout.TextLines.Count; index++)
        {
            var length = layout.TextLines[index].Length;
            var end = Math.Min(offset + length, text.Length);

            if (length <= 0 || end <= offset)
            {
                continue;
            }

            var slice = text[offset..end];
            offset = end;

            NewlineKind kind;
            int newlineIndex;

            if (slice.EndsWith('\n'))
            {
                newlineIndex = end - 1;
                kind = slice.Length >= 2
                    ? slice[^2] switch
                    {
                        NewlineEncoding.CrLfSentinel => NewlineKind.CrLf,
                        NewlineEncoding.CrSentinel => NewlineKind.Cr,
                        NewlineEncoding.LfSentinel => NewlineKind.Lf,
                        '\r' => NewlineKind.CrLf,
                        _ => NewlineKind.Lf,
                    }
                    : NewlineKind.Lf;
            }
            else if (slice.EndsWith('\r'))
            {
                newlineIndex = end - 1;
                kind = NewlineKind.Cr;
            }
            else
            {
                continue;
            }

            if (unmarked is not null && unmarked.Contains(newlineIndex))
            {
                continue;
            }

            if (!marks.TryGetValue(index, out var kinds))
            {
                kinds = [];
                marks[index] = kinds;
            }

            kinds.Add(kind);
        }

        return marks;
    }

    private static double FontSizeOf(Control target) =>
        target is TextBlock block ? block.FontSize : ((TemplatedControl)target).FontSize;

    private static FontFamily FontFamilyOf(Control target) =>
        target is TextBlock block ? block.FontFamily : ((TemplatedControl)target).FontFamily;

    private void Resubscribe(Control? target)
    {
        if (ReferenceEquals(_subscribed, target))
        {
            return;
        }

        if (_subscribed is { } previous)
        {
            previous.SizeChanged -= OnTargetSizeChanged;
            previous.PropertyChanged -= OnTargetPropertyChanged;

            if (previous is TextBox previousBox)
            {
                previousBox.TextChanged -= OnTextBoxTextChanged;
                previousBox.TemplateApplied -= OnTargetTemplateApplied;
            }
        }

        _subscribed = target;

        if (target is not null)
        {
            target.SizeChanged += OnTargetSizeChanged;
            target.PropertyChanged += OnTargetPropertyChanged;

            if (target is TextBox box)
            {
                box.TextChanged += OnTextBoxTextChanged;
                box.TemplateApplied += OnTargetTemplateApplied;
            }
        }

        EnsureScrollViewer();
        InvalidateVisual();
    }

    private void OnTargetSizeChanged(object? sender, SizeChangedEventArgs e) => InvalidateVisual();

    private void OnTextBoxTextChanged(object? sender, TextChangedEventArgs e) => InvalidateVisual();

    private void OnTargetTemplateApplied(object? sender, TemplateAppliedEventArgs e) => EnsureScrollViewer();

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty
            || e.Property == TextBox.SelectionStartProperty
            || e.Property == TextBox.SelectionEndProperty
            || e.Property == TextBlock.TextProperty
            || e.Property == TextBlock.FontSizeProperty)
        {
            InvalidateVisual();
        }
    }

    private void EnsureScrollViewer()
    {
        var viewer = Target is { } target ? FindScrollViewer(target) : null;

        if (ReferenceEquals(_scrollViewer, viewer))
        {
            return;
        }

        if (_scrollViewer is { } previous)
        {
            previous.ScrollChanged -= OnTargetScrollChanged;
        }

        _scrollViewer = viewer;

        if (viewer is not null)
        {
            viewer.ScrollChanged += OnTargetScrollChanged;
        }
    }

    private void EnsureScrollViewerIfMissing()
    {
        if (_scrollViewer is null || !_scrollViewer.IsAttachedToVisualTree())
        {
            EnsureScrollViewer();
        }
    }

    private void OnTargetScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();
}

