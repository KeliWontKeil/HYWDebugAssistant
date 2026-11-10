using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls.Charting;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>最近一次范围拖动改变的是哪一端。</summary>
public enum RangeChangeKind
{
    /// <summary>没有拖动。</summary>
    None = 0,

    /// <summary>左端（范围起点）。</summary>
    Start = 1,

    /// <summary>右端（范围终点）。</summary>
    End = 2,

    /// <summary>整体平移。</summary>
    Body = 3,

    /// <summary>空白处重新框选。</summary>
    New = 4,
}

/// <summary>
/// **线性范围选择控件**：域轨道 + 当前范围带（可拖动）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只有一个范围</b>：<see cref="RangeStart"/> / <see cref="RangeEnd"/> 就是可视化范围本身；
///     没有独立 viewport/selection 两层状态。数字精确值由调用方的输入框承担，本控件只做可视化与拖动。</item>
///   <item><b>不知道图表/数据集</b>：只接收域、范围、刻度间隔；不读波形、不画数字标签。</item>
///   <item><b>轨道占满自身宽度</b>：本控件不读取图表绘图区，也不接收外部像素 inset；
///     轨道从自身控件边界到边界，只留描边内缩。需要与图表对齐时由组合方决定，不在控件间复制几何。</item>
/// </list>
/// </remarks>
public sealed class RangeSelector : Control
{
    /// <summary>可选域下限。</summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(Minimum));

    /// <summary>可选域上限。</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(Maximum), 1);

    /// <summary>当前范围起点（NaN = 无范围）。</summary>
    public static readonly StyledProperty<double> RangeStartProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(RangeStart), double.NaN);

    /// <summary>当前范围终点（NaN = 无范围）。</summary>
    public static readonly StyledProperty<double> RangeEndProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(RangeEnd), double.NaN);

    /// <summary>允许的最小范围跨度（与域同单位）。</summary>
    public static readonly StyledProperty<double> MinimumRangeSpanProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(MinimumRangeSpan));

    /// <summary>轨道高度（DIP）。</summary>
    public static readonly StyledProperty<double> TrackHeightProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(TrackHeight), 10);

    /// <summary>刻度线步长（0 = 自动）。</summary>
    public static readonly StyledProperty<double> TickStepProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(TickStep));

    /// <summary>是否画刻度线。</summary>
    public static readonly StyledProperty<bool> ShowTicksProperty =
        AvaloniaProperty.Register<RangeSelector, bool>(nameof(ShowTicks), true);

    /// <summary>是否显示主刻度数字。</summary>
    public static readonly StyledProperty<bool> ShowTickLabelsProperty =
        AvaloniaProperty.Register<RangeSelector, bool>(nameof(ShowTickLabels));

    /// <summary>次刻度步长（0 = 自动，约主刻度/5）。</summary>
    public static readonly StyledProperty<double> MinorTickStepProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(MinorTickStep));

    /// <summary>刻度值单位（Index = 整数，Time = 秒）。</summary>
    public static readonly StyledProperty<ChartAxisKind> XAxisKindProperty =
        AvaloniaProperty.Register<RangeSelector, ChartAxisKind>(nameof(XAxisKind));

    /// <summary>刻度数字色。</summary>
    public static readonly StyledProperty<IBrush?> TickLabelBrushProperty =
        AvaloniaProperty.Register<RangeSelector, IBrush?>(nameof(TickLabelBrush));

    /// <summary>刻度线色（与轨道边框分开，保证浅色轨道上也能看清）。</summary>
    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<RangeSelector, IBrush?>(nameof(TickBrush));

    /// <summary>边缘命中宽度（DIP；只用于拖动，不再画粗手柄）。</summary>
    public static readonly StyledProperty<double> HandleWidthProperty =
        AvaloniaProperty.Register<RangeSelector, double>(nameof(HandleWidth), 8);

    /// <summary>轨道底色。</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RangeSelector, IBrush?>(nameof(TrackBrush));

    /// <summary>范围带色。</summary>
    public static readonly StyledProperty<IBrush?> RangeBrushProperty =
        AvaloniaProperty.Register<RangeSelector, IBrush?>(nameof(RangeBrush));

    /// <summary>手柄色。</summary>
    public static readonly StyledProperty<IBrush?> HandleBrushProperty =
        AvaloniaProperty.Register<RangeSelector, IBrush?>(nameof(HandleBrush));

    /// <summary>轨道边框色。</summary>
    public static readonly StyledProperty<IBrush?> FrameBrushProperty =
        AvaloniaProperty.Register<RangeSelector, IBrush?>(nameof(FrameBrush));

    private const int DragNone = 0;
    private const int DragStart = 1;
    private const int DragEnd = 2;
    private const int DragBody = 3;
    private const int DragNew = 4;

    private static readonly Cursor EdgeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor BodyCursor = new(StandardCursorType.SizeAll);

    private Rect _trackRect;
    private int _dragMode;
    private bool _rangeChangedInGesture;
    private double _dragAnchor;
    private double _dragOffset;

    static RangeSelector()
    {
        AffectsRender<RangeSelector>(
            MinimumProperty,
            MaximumProperty,
            RangeStartProperty,
            RangeEndProperty,
            MinimumRangeSpanProperty,
            TrackHeightProperty,
            TickStepProperty,
            ShowTicksProperty,
            ShowTickLabelsProperty,
            MinorTickStepProperty,
            XAxisKindProperty,
            TickLabelBrushProperty,
            TickBrushProperty,
            HandleWidthProperty,
            TrackBrushProperty,
            RangeBrushProperty,
            HandleBrushProperty,
            FrameBrushProperty);
    }

    /// <summary>可选域下限。</summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>可选域上限。</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>当前范围起点。</summary>
    public double RangeStart
    {
        get => GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    /// <summary>当前范围终点。</summary>
    public double RangeEnd
    {
        get => GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
    }

    /// <summary>允许的最小范围跨度。</summary>
    public double MinimumRangeSpan
    {
        get => GetValue(MinimumRangeSpanProperty);
        set => SetValue(MinimumRangeSpanProperty, value);
    }

    /// <summary>轨道高度。</summary>
    public double TrackHeight
    {
        get => GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    /// <summary>刻度线步长（0 = 自动）。</summary>
    public double TickStep
    {
        get => GetValue(TickStepProperty);
        set => SetValue(TickStepProperty, value);
    }

    /// <summary>是否画刻度线。</summary>
    public bool ShowTicks
    {
        get => GetValue(ShowTicksProperty);
        set => SetValue(ShowTicksProperty, value);
    }

    /// <summary>是否显示主刻度数字。</summary>
    public bool ShowTickLabels
    {
        get => GetValue(ShowTickLabelsProperty);
        set => SetValue(ShowTickLabelsProperty, value);
    }

    /// <summary>次刻度步长（0 = 自动）。</summary>
    public double MinorTickStep
    {
        get => GetValue(MinorTickStepProperty);
        set => SetValue(MinorTickStepProperty, value);
    }

    /// <summary>刻度值单位。</summary>
    public ChartAxisKind XAxisKind
    {
        get => GetValue(XAxisKindProperty);
        set => SetValue(XAxisKindProperty, value);
    }

    /// <summary>刻度数字色。</summary>
    public IBrush? TickLabelBrush
    {
        get => GetValue(TickLabelBrushProperty);
        set => SetValue(TickLabelBrushProperty, value);
    }

    /// <summary>刻度线色。</summary>
    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    /// <summary>边缘命中宽度（只用于拖动）。</summary>
    public double HandleWidth
    {
        get => GetValue(HandleWidthProperty);
        set => SetValue(HandleWidthProperty, value);
    }

    /// <summary>轨道底色。</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>范围带色。</summary>
    public IBrush? RangeBrush
    {
        get => GetValue(RangeBrushProperty);
        set => SetValue(RangeBrushProperty, value);
    }

    /// <summary>手柄色。</summary>
    public IBrush? HandleBrush
    {
        get => GetValue(HandleBrushProperty);
        set => SetValue(HandleBrushProperty, value);
    }

    /// <summary>轨道边框色。</summary>
    public IBrush? FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    /// <summary>最近一次**实际改变范围**的是左端/右端/整体/重新框选；只按下未移动或程序设置属性不改变。</summary>
    public RangeChangeKind LastChangeKind { get; private set; }

    /// <summary>用户改变了范围（拖动产生；程序设置属性不触发）。</summary>
    public event EventHandler? RangeChanged;

    /// <summary>
    /// **按实际轨道宽度、当前标签文本和 9 号刻度字体测量**，最多能放下多少个主刻度。
    /// </summary>
    /// <returns>可读上限；布局尚未完成时返回 0（表示未知）。</returns>
    /// <remarks>
    /// 与 <see cref="ChartSurface.GetMaxReadableXTicks"/> 使用同一“标签宽度 + 最小间距”的
    /// 可读密度规则，但字号、轨道宽度不同，因此结果不同；这里不写死刻度个数。
    /// </remarks>
    public int GetMaxReadableXTicks()
    {
        var track = CalculateTrackRect();
        if (track.Width <= 1)
        {
            return 0;
        }

        // 绝对色：主题资源缺失时的标签测量兜底。
        var labelBrush = TickLabelBrush ?? Brushes.Gray;
        var startText = new FormattedText(
            ChartScale.Format(Minimum, XAxisKind),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            labelBrush);
        var endText = new FormattedText(
            ChartScale.Format(Maximum, XAxisKind),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            labelBrush);
        var labelWidth = Math.Max(startText.Width, endText.Width);
        var minSpacing = Math.Max(16, labelWidth + 6);
        return Math.Max(1, (int)(track.Width / minSpacing));
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var track = CalculateTrackRect();
        _trackRect = track;
        if (track.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        // 绝对色：主题资源缺失时的轨道/边框兜底。
        var trackBrush = TrackBrush ?? Brushes.LightGray;
        var frameBrush = FrameBrush ?? Brushes.Gray;
        var borderPen = new Pen(frameBrush, 1);
        // 绝对色：主题资源缺失时的刻度线兜底。
        var tickPen = new Pen(TickBrush ?? Brushes.Gray, 1);
        context.DrawRectangle(trackBrush, borderPen, track, 2, 2);

        if (!TryNormalizeRange(out var start, out var end))
        {
            DrawTicks(context, track, tickPen);
            return;
        }

        var range = MapRange(start, end);
        var band = new Rect(range.Left, track.Top, Math.Max(1, range.Width), track.Height);
        // 绝对色：主题资源缺失时的范围带/边界兜底。
        var rangeBrush = RangeBrush ?? Brushes.DodgerBlue;
        var borderBrush = HandleBrush ?? Brushes.DodgerBlue;
        context.DrawRectangle(rangeBrush, new Pen(borderBrush, 1), band, 2, 2);

        DrawTicks(context, track, tickPen);
    }

    private void DrawTicks(DrawingContext context, Rect track, Pen pen)
    {
        if (Maximum <= Minimum)
        {
            return;
        }

        var majorStep = TickStep > 0 && !double.IsNaN(TickStep) && !double.IsInfinity(TickStep)
            ? TickStep
            : ChartScale.NiceStep(Maximum - Minimum, Math.Max(2, (int)(track.Width / 80)));
        var minorStep = MinorTickStep > 0 && !double.IsNaN(MinorTickStep) && !double.IsInfinity(MinorTickStep)
            ? MinorTickStep
            : majorStep / 5;
        // 绝对色：主题资源缺失时的刻度数字兜底。
        var labelBrush = TickLabelBrush ?? Brushes.Gray;

        if (ShowTicks && minorStep > 0 && minorStep < majorStep)
        {
            var minorStart = (long)Math.Ceiling(Minimum / minorStep);
            var minorEnd = (long)Math.Floor(Maximum / minorStep);
            if (minorEnd - minorStart <= 400)
            {
                for (var tick = minorStart; tick <= minorEnd; tick++)
                {
                    var value = tick * minorStep;
                    var majorIndex = value / majorStep;
                    if (Math.Abs(majorIndex - Math.Round(majorIndex)) < 0.000001)
                    {
                        continue;
                    }

                    var x = ChartScale.Map(value, Minimum, Maximum, track.Left, track.Right);
                    context.DrawLine(pen, new Point(x, track.Bottom), new Point(x, track.Bottom - 4));
                }
            }
        }

        var tickStart = (long)Math.Ceiling(Minimum / majorStep);
        var tickEnd = (long)Math.Floor(Maximum / majorStep);
        if (tickEnd - tickStart > 200)
        {
            tickEnd = tickStart + 200;
        }

        var tickCount = Math.Max(1, (int)Math.Min(200, tickEnd - tickStart + 1));
        var labelChars = ChartAxisFormatter.MaxChars((track.Width / tickCount) - 4, 9);

        for (var tick = tickStart; tick <= tickEnd; tick++)
        {
            var value = tick * majorStep;
            var x = ChartScale.Map(value, Minimum, Maximum, track.Left, track.Right);
            if (ShowTicks)
            {
                context.DrawLine(pen, new Point(x, track.Bottom), new Point(x, track.Bottom - 7));
            }

            if (!ShowTickLabels)
            {
                continue;
            }

            var text = new FormattedText(
                ChartAxisFormatter.Format(value, XAxisKind, majorStep, labelChars),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9,
                labelBrush);
            var left = ChartScale.Clamp(
                x - (text.Width / 2),
                0,
                Math.Max(0, Bounds.Width - text.Width));
            context.DrawText(text, new Point(left, track.Bottom + 3));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        LastChangeKind = RangeChangeKind.None;

        var point = e.GetPosition(this);
        var track = CalculateTrackRect();
        _trackRect = track;

        if (track.Width <= 1 || point.Y < track.Top - 10 || point.Y > track.Bottom + 10)
        {
            return;
        }

        var x = ChartScale.Clamp(point.X, track.Left, track.Right);
        var value = Unmap(x);
        var hitWidth = Math.Max(8, HandleWidth + 4);

        if (TryNormalizeRange(out var start, out var end))
        {
            var startX = Map(start).X;
            var endX = Map(end).X;

            if (Math.Abs(point.X - startX) <= hitWidth)
            {
                _dragMode = DragStart;
            }
            else if (Math.Abs(point.X - endX) <= hitWidth)
            {
                _dragMode = DragEnd;
            }
            else if (point.X > startX && point.X < endX)
            {
                _dragMode = DragBody;
                _dragOffset = value - start;
            }
            else
            {
                _dragMode = DragNew;
                _dragAnchor = value;
            }
        }
        else
        {
            _dragMode = DragNew;
            _dragAnchor = value;
        }

        LastChangeKind = KindFor(_dragMode);
        // 点中新范围会立即改变选择；左端/右端/整体只有实际移动才算一次操作。
        _rangeChangedInGesture = _dragMode == DragNew;
        if (_dragMode == DragNew)
        {
            RangeStart = value;
            RangeEnd = value;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private static RangeChangeKind KindFor(int dragMode) => dragMode switch
    {
        DragStart => RangeChangeKind.Start,
        DragEnd => RangeChangeKind.End,
        DragBody => RangeChangeKind.Body,
        DragNew => RangeChangeKind.New,
        _ => RangeChangeKind.None,
    };

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragMode == DragNone)
        {
            UpdateCursor(e.GetPosition(this));
            return;
        }

        var track = _trackRect;
        if (track.Width <= 1)
        {
            return;
        }

        var point = e.GetPosition(this);
        var value = Unmap(ChartScale.Clamp(point.X, track.Left, track.Right));
        var span = Math.Max(0, MinimumRangeSpan);

        LastChangeKind = KindFor(_dragMode);
        _rangeChangedInGesture = true;
        switch (_dragMode)
        {
            case DragStart:
                RangeStart = Math.Min(value, RangeEnd - span);
                break;

            case DragEnd:
                RangeEnd = Math.Max(value, RangeStart + span);
                break;

            case DragBody when TryNormalizeRange(out var start, out var end):
                var width = Math.Max(span, end - start);
                var newStart = ChartScale.Clamp(value - _dragOffset, Minimum, Maximum - width);
                RangeStart = newStart;
                RangeEnd = newStart + width;
                break;

            case DragNew:
                RangeStart = Math.Min(_dragAnchor, value);
                RangeEnd = Math.Max(_dragAnchor, value);
                if (RangeEnd - RangeStart < span)
                {
                    RangeEnd = Math.Min(Maximum, RangeStart + span);
                    RangeStart = Math.Max(Minimum, RangeEnd - span);
                }

                break;
        }

        RangeChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragMode == DragNone)
        {
            return;
        }

        LastChangeKind = KindFor(_dragMode);
        var changed = _rangeChangedInGesture;
        _dragMode = DragNone;
        _rangeChangedInGesture = false;
        e.Pointer.Capture(null);

        if (!TryNormalizeRange(out _, out _))
        {
            // **选择带常驻**：单击/短拖动不能把范围退回 NaN（那会让选择框凭空消失）。
            // 以操作位置为中心扩展成最小可操作跨度；没有锚点时用域中点。
            EnsureMinimumRange();
            changed = true;
        }

        if (changed)
        {
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // 按了但没移动：这不是一次范围变化，别把本次按下的"意图"留给下一次事件误读。
            LastChangeKind = RangeChangeKind.None;
        }

        UpdateCursor(e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>范围无效时，围绕最后操作位置生成一个最小跨度的选择带。</summary>
    private void EnsureMinimumRange()
    {
        if (Maximum <= Minimum)
        {
            return;
        }

        var domain = Maximum - Minimum;
        var requested = Math.Max(MinimumRangeSpan, domain / 100);
        var span = Math.Min(domain, Math.Max(requested, domain * 1e-6));
        var center = double.IsFinite(_dragAnchor) ? _dragAnchor : Minimum + (domain / 2);
        var start = ChartScale.Clamp(center - (span / 2), Minimum, Maximum - span);
        RangeStart = start;
        RangeEnd = Math.Min(Maximum, start + span);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragMode = DragNone;
        _rangeChangedInGesture = false;
        Cursor = null;
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragMode == DragNone)
        {
            Cursor = null;
        }
    }

    private void UpdateCursor(Point point)
    {
        var track = CalculateTrackRect();
        if (track.Width <= 1)
        {
            Cursor = null;
            return;
        }

        if (point.Y < track.Top - 10 || point.Y > track.Bottom + 10
            || point.X < track.Left - 4 || point.X > track.Right + 4)
        {
            Cursor = null;
            return;
        }

        if (!TryNormalizeRange(out var start, out var end))
        {
            Cursor = null;
            return;
        }

        var startX = Map(start).X;
        var endX = Map(end).X;
        var hitWidth = Math.Max(8, HandleWidth + 4);
        if (Math.Abs(point.X - startX) <= hitWidth || Math.Abs(point.X - endX) <= hitWidth)
        {
            Cursor = EdgeCursor;
        }
        else if (point.X > startX && point.X < endX)
        {
            Cursor = BodyCursor;
        }
        else
        {
            Cursor = null;
        }
    }

    private Rect CalculateTrackRect()
    {
        // 只内缩半个描边，避免轨道边框在控件边界被裁掉；这不是“图表绘图区 inset”。
        const double edge = 1;
        var left = Math.Min(edge, Math.Max(0, Bounds.Width / 2));
        var right = Math.Max(left, Bounds.Width - left);
        var height = Math.Min(TrackHeight, Math.Max(1, Bounds.Height));
        // 轨道靠上，给主刻度数字留出底部空间。
        var top = 2;
        return new Rect(left, top, Math.Max(0, right - left), height);
    }

    private bool TryNormalizeRange(out double start, out double end)
    {
        start = Math.Min(RangeStart, RangeEnd);
        end = Math.Max(RangeStart, RangeEnd);
        return !double.IsNaN(start)
            && !double.IsNaN(end)
            && Maximum > Minimum
            && end > start;
    }

    private Rect MapRange(double start, double end)
    {
        var left = Map(Math.Min(start, end), Minimum, Maximum, _trackRect.Left, _trackRect.Right);
        var right = Map(Math.Max(start, end), Minimum, Maximum, _trackRect.Left, _trackRect.Right);
        return new Rect(left, _trackRect.Top, Math.Max(0, right - left), _trackRect.Height);
    }

    private Point Map(double value) =>
        new(Map(value, Minimum, Maximum, _trackRect.Left, _trackRect.Right), _trackRect.Center.Y);

    private double Unmap(double x) =>
        ChartScale.Unmap(x, Minimum, Maximum, _trackRect.Left, _trackRect.Right);

    private static double Map(double value, double min, double max, double pixelMin, double pixelMax) =>
        ChartScale.Map(value, min, max, pixelMin, pixelMax);
}
