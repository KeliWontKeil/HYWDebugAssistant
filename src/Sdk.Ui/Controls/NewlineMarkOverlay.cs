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

    private readonly List<(Point Position, string Text)> _marks = [];
    private HashSet<int>? _unmarked;
    private Control? _subscribed;
    private ScrollViewer? _scrollViewer;
    private FormattedText? _mark;
    private double _markFontSize;

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
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _marks.Clear();

        if (!IsActive || Target is not { } target || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        EnsureScrollViewerIfMissing();

        if (!TryGetLayout(target, out var layout, out var text, out var origin))
        {
            return;
        }

        var font = MarkFormatted(target);
        var marks = MarkLines(layout, text, _unmarked);
        var top = origin.Y;

        for (var index = 0; index < layout.TextLines.Count; index++)
        {
            var line = layout.TextLines[index];
            var height = line.Height;

            if (marks.TryGetValue(index, out var count))
            {
                var x = origin.X + line.Width + MarkGap;
                var y = top + ((height - font.Height) / 2);

                if (y + font.Height >= 0 && y <= Bounds.Height)
                {
                    context.DrawText(font, new Point(x, y));

                    for (var i = 0; i < count; i++)
                    {
                        _marks.Add((new Point(x + (i * font.Width), y), NewlineMark));
                    }
                }
            }

            top += height;
        }
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

    private FormattedText MarkFormatted(Control target)
    {
        var size = Math.Max(1, FontSizeOf(target) * MarkFontScale);

        if (_mark is not null && Math.Abs(_markFontSize - size) < 0.01)
        {
            return _mark;
        }

        _markFontSize = size;

        var ink = target.TryFindResource("TextMuted", out var resource) && resource is IBrush themed
            ? themed
            : AbsoluteMarkInk;

        var family = target is TextBlock block ? block.FontFamily : FontFamilyOf(target);

        _mark = new FormattedText(
            NewlineMark,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(family),
            size,
            ink);

        return _mark;
    }

    /// <summary>逐视觉行算“每行该画几个 ↵”。</summary>
    private static Dictionary<int, int> MarkLines(
        Avalonia.Media.TextFormatting.TextLayout layout,
        string text,
        HashSet<int>? unmarked)
    {
        var marks = new Dictionary<int, int>();
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

            if (!slice.EndsWith('\n'))
            {
                continue;
            }

            if (unmarked is not null && unmarked.Contains(end - 1))
            {
                continue;
            }

            Add(index);
        }

        return marks;

        void Add(int line) => marks[line] = marks.TryGetValue(line, out var count) ? count + 1 : 1;
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

