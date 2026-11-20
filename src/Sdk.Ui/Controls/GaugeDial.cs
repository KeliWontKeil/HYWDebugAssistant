using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls.Charting;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>指针式仪表盘：一个目标值 → 弧线刻度、指针与读数。</summary>
/// <remarks>
/// <para>
/// <b>只画 <see cref="Value"/></b>：不读显示源、不解析设置、不判断有没有数据。
/// "数据停了一会儿该保持还是回零"是模块的目标值发布事实（自动回零设置），
/// 本控件只把收到的目标值平滑地追上去；因此没有第二套"无数据"显示分支。
/// </para>
/// <para>
/// 平滑只改"显示值"：目标值不变且已经对齐时 <see cref="Refresh"/> 不重绘。
/// </para>
/// </remarks>
public sealed class GaugeDial : Control, IModuleViewElement, IModulePreviewBoundsTarget
{
    /// <summary>量程下限。</summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(Minimum));

    /// <summary>量程上限。</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(Maximum), 100);

    /// <summary>目标值（有限实数；合法性由模块保证）。</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(Value));

    /// <summary>表盘文字（标题；空 = 不占位）。</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<GaugeDial, string>(nameof(Title), string.Empty);

    /// <summary>单位（空 = 不画）。</summary>
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<GaugeDial, string>(nameof(Unit), string.Empty);

    /// <summary>
    /// **四周留白**（DIP；0 = 刻度环外沿紧贴控件边框）；标题/读数/单位在圆内绘制，不占这段留白。
    /// </summary>
    public static readonly StyledProperty<double> PaddingProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(Padding), GaugeGeometry.DefaultPadding);

    /// <summary>弧线总张角（度；以正上方为中心）。</summary>
    public static readonly StyledProperty<double> SweepAngleProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(SweepAngle), 240);

    /// <summary>读数小数位。</summary>
    public static readonly StyledProperty<int> ValueDecimalsProperty =
        AvaloniaProperty.Register<GaugeDial, int>(nameof(ValueDecimals), 2);

    /// <summary>主刻度步长（&lt;= 0 = 自动）。</summary>
    public static readonly StyledProperty<double> MajorTickStepProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(MajorTickStep));

    /// <summary>每个主刻度间的次刻度数（0 = 不画）。</summary>
    public static readonly StyledProperty<int> MinorTickCountProperty =
        AvaloniaProperty.Register<GaugeDial, int>(nameof(MinorTickCount), 4);

    /// <summary>填充起点：从量程下限，还是从零开始（零在量程外时夹到端点）。</summary>
    public static readonly StyledProperty<ScaleFillOrigin> FillOriginProperty =
        AvaloniaProperty.Register<GaugeDial, ScaleFillOrigin>(nameof(FillOrigin), ScaleFillOrigin.Zero);

    /// <summary>是否画中间读数。</summary>
    public static readonly StyledProperty<bool> ShowValueProperty =
        AvaloniaProperty.Register<GaugeDial, bool>(nameof(ShowValue), true);

    /// <summary>是否平滑。</summary>
    public static readonly StyledProperty<bool> SmoothingEnabledProperty =
        AvaloniaProperty.Register<GaugeDial, bool>(nameof(SmoothingEnabled), true);

    /// <summary>平滑时间常数（毫秒）。</summary>
    public static readonly StyledProperty<double> SmoothingTimeMsProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(SmoothingTimeMs), 120);

    /// <summary>整个控件的基准字号（刻度和标题由它派生，读数放大一档）。</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(FontSize), 10);

    /// <summary>弧线底色。</summary>
    public static readonly StyledProperty<IBrush?> DialBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(DialBrush));

    /// <summary>弧线填充（当前值覆盖的那一段）。</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(TrackBrush));

    /// <summary>刻度线色。</summary>
    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(TickBrush));

    /// <summary>刻度数字色。</summary>
    public static readonly StyledProperty<IBrush?> TickLabelBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(TickLabelBrush));

    /// <summary>指针色。</summary>
    public static readonly StyledProperty<IBrush?> NeedleBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(NeedleBrush));

    /// <summary>中心轴色。</summary>
    public static readonly StyledProperty<IBrush?> HubBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(HubBrush));

    /// <summary>标题色。</summary>
    public static readonly StyledProperty<IBrush?> TitleBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(TitleBrush));

    /// <summary>读数色。</summary>
    public static readonly StyledProperty<IBrush?> ValueBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(ValueBrush));

    /// <summary>单位色。</summary>
    public static readonly StyledProperty<IBrush?> UnitBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(UnitBrush));

    /// <summary>超出量程时的读数色。</summary>
    public static readonly StyledProperty<IBrush?> OutOfRangeBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush?>(nameof(OutOfRangeBrush));

    /// <summary>弧线采样步进（度）：决定采样点数，不表达任何业务取值。</summary>
    private const double ArcSampleDegrees = 2;

    private readonly AnimatedNumber _display = new();
    private readonly Dictionary<(IBrush Brush, double Thickness), IPen> _pens = [];

    /// <summary>端点标签槽：先算好文字与角度，供主刻度标签避让。</summary>
    private readonly record struct EndLabelSlot(double Angle, double Radius, FormattedText Text);

    static GaugeDial()
    {
        AffectsRender<GaugeDial>(
            MinimumProperty,
            MaximumProperty,
            ValueProperty,
            TitleProperty,
            UnitProperty,
            PaddingProperty,
            SweepAngleProperty,
            ValueDecimalsProperty,
            MajorTickStepProperty,
            MinorTickCountProperty,
            FillOriginProperty,
            ShowValueProperty,
            SmoothingEnabledProperty,
            SmoothingTimeMsProperty,
            FontSizeProperty,
            DialBrushProperty,
            TrackBrushProperty,
            TickBrushProperty,
            TickLabelBrushProperty,
            NeedleBrushProperty,
            HubBrushProperty,
            TitleBrushProperty,
            ValueBrushProperty,
            UnitBrushProperty,
            OutOfRangeBrushProperty);
    }

    /// <summary>量程下限。</summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>量程上限。</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>目标值。</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>表盘文字。</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>单位。</summary>
    public string Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>四周留白（DIP；0 = 刻度环外沿紧贴控件边框）。</summary>
    public double Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>弧线总张角（度）。</summary>
    public double SweepAngle
    {
        get => GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    /// <summary>读数小数位。</summary>
    public int ValueDecimals
    {
        get => GetValue(ValueDecimalsProperty);
        set => SetValue(ValueDecimalsProperty, value);
    }

    /// <summary>主刻度步长（&lt;= 0 = 自动）。</summary>
    public double MajorTickStep
    {
        get => GetValue(MajorTickStepProperty);
        set => SetValue(MajorTickStepProperty, value);
    }

    /// <summary>每个主刻度间的次刻度数。</summary>
    public int MinorTickCount
    {
        get => GetValue(MinorTickCountProperty);
        set => SetValue(MinorTickCountProperty, value);
    }

    /// <summary>填充起点（量程下限 / 零）。</summary>
    public ScaleFillOrigin FillOrigin
    {
        get => GetValue(FillOriginProperty);
        set => SetValue(FillOriginProperty, value);
    }

    /// <summary>是否画中间读数。</summary>
    public bool ShowValue
    {
        get => GetValue(ShowValueProperty);
        set => SetValue(ShowValueProperty, value);
    }

    /// <summary>是否平滑。</summary>
    public bool SmoothingEnabled
    {
        get => GetValue(SmoothingEnabledProperty);
        set => SetValue(SmoothingEnabledProperty, value);
    }

    /// <summary>平滑时间常数（毫秒）。</summary>
    public double SmoothingTimeMs
    {
        get => GetValue(SmoothingTimeMsProperty);
        set => SetValue(SmoothingTimeMsProperty, value);
    }

    /// <summary>基准字号。</summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>弧线底色。</summary>
    public IBrush? DialBrush
    {
        get => GetValue(DialBrushProperty);
        set => SetValue(DialBrushProperty, value);
    }

    /// <summary>弧线填充色。</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>刻度线色。</summary>
    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    /// <summary>刻度数字色。</summary>
    public IBrush? TickLabelBrush
    {
        get => GetValue(TickLabelBrushProperty);
        set => SetValue(TickLabelBrushProperty, value);
    }

    /// <summary>指针色。</summary>
    public IBrush? NeedleBrush
    {
        get => GetValue(NeedleBrushProperty);
        set => SetValue(NeedleBrushProperty, value);
    }

    /// <summary>中心轴色。</summary>
    public IBrush? HubBrush
    {
        get => GetValue(HubBrushProperty);
        set => SetValue(HubBrushProperty, value);
    }

    /// <summary>标题色。</summary>
    public IBrush? TitleBrush
    {
        get => GetValue(TitleBrushProperty);
        set => SetValue(TitleBrushProperty, value);
    }

    /// <summary>读数色。</summary>
    public IBrush? ValueBrush
    {
        get => GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    /// <summary>单位色。</summary>
    public IBrush? UnitBrush
    {
        get => GetValue(UnitBrushProperty);
        set => SetValue(UnitBrushProperty, value);
    }

    /// <summary>超量程读数色。</summary>
    public IBrush? OutOfRangeBrush
    {
        get => GetValue(OutOfRangeBrushProperty);
        set => SetValue(OutOfRangeBrushProperty, value);
    }

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var span = NormalizedMaximum() - NormalizedMinimum();
        var epsilon = span > 0 && double.IsFinite(span) ? span / 2000 : 0.001;
        if (_display.Advance(Value, SmoothingEnabled, SmoothingTimeMs, epsilon))
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// **报方形尺寸**：控件边界 = 圆的直径，圆心即控件中心；标题/读数/单位在圆内绘制，
    /// 不参与尺寸计算，因此改文字不会改变表盘大小。
    /// </summary>
    /// <param name="availableSize">可用尺寸。</param>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : 300;
        var height = double.IsFinite(availableSize.Height) && availableSize.Height > 0
            ? availableSize.Height
            : width;

        var side = Math.Min(width, height);
        return side < 24
            ? new Size(Math.Min(width, 24), Math.Min(height, 24))
            : new Size(side, side);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 24 || height < 24)
        {
            return;
        }

        var fontSize = NormalizedFontSize();
        var minimum = NormalizedMinimum();
        var maximum = NormalizedMaximum();
        var span = maximum - minimum;

        var padding = NormalizedPadding();
        var metrics = GaugeGeometry.Compute(width, height, padding, SweepAngle, 2);
        if (metrics.Radius <= 6)
        {
            return;
        }

        var half = metrics.HalfAngle;
        var radius = metrics.Radius;
        var ringThickness = metrics.Thickness;
        var centerX = metrics.CenterX;
        var centerY = metrics.CenterY;
        var displayed = Math.Clamp(_display.Value, minimum, maximum);
        var fraction = span > 0 ? (displayed - minimum) / span : 0;
        var needleAngle = -half + (fraction * 2 * half);
        var origin = FillOrigin == ScaleFillOrigin.Zero && span > 0
            ? Math.Clamp((0 - minimum) / span, 0, 1)
            : 0;
        var originAngle = -half + (origin * 2 * half);
        var endLabels = BuildEndLabels(radius, ringThickness, half, minimum, maximum, fontSize);

        DrawRing(context, centerX, centerY, radius, ringThickness, half, needleAngle, originAngle);
        DrawTicks(context, centerX, centerY, radius, ringThickness, half, minimum, maximum, fontSize, endLabels);
        DrawEndLabels(context, centerX, centerY, endLabels);
        DrawNeedle(context, centerX, centerY, radius, needleAngle);

        // 文字最后画：标题/读数/单位压在指针与中心轴之上，且始终在圆内。
        DrawText(context, centerX, centerY, radius, ringThickness, fontSize, minimum, maximum);
    }

    private double NormalizedPadding() =>
        double.IsFinite(Padding) && Padding > 0 ? Padding : 0;

    private double NormalizedMinimum() => double.IsFinite(Minimum) ? Minimum : 0;

    private double NormalizedMaximum()
    {
        var minimum = NormalizedMinimum();
        var maximum = Maximum;
        return double.IsFinite(maximum) && maximum > minimum ? maximum : minimum + 1;
    }

    private double NormalizedFontSize() =>
        double.IsFinite(FontSize) && FontSize > 0 ? FontSize : 10;

    private void DrawRing(
        DrawingContext context,
        double centerX,
        double centerY,
        double radius,
        double thickness,
        double half,
        double needleAngle,
        double originAngle)
    {
        if (DialBrush is { } dialBrush)
        {
            DrawArc(context, GetPen(dialBrush, thickness), centerX, centerY, radius, -half, half);
        }

        if (TrackBrush is not { } trackBrush
            || Math.Abs(needleAngle - originAngle) < 1e-4)
        {
            return;
        }

        DrawArc(
            context,
            GetPen(trackBrush, Math.Max(1.5, thickness * 0.55)),
            centerX,
            centerY,
            radius,
            Math.Min(originAngle, needleAngle),
            Math.Max(originAngle, needleAngle));
    }

    private void DrawTicks(
        DrawingContext context,
        double centerX,
        double centerY,
        double radius,
        double ringThickness,
        double half,
        double minimum,
        double maximum,
        double fontSize,
        IReadOnlyList<EndLabelSlot> endLabels)
    {
        if (TickBrush is not { } tickBrush)
        {
            return;
        }

        var span = maximum - minimum;
        var majorStep = MajorTickStep > 0 && double.IsFinite(MajorTickStep)
            ? MajorTickStep
            : ChartScale.NiceStep(span, 8);
        if (!(majorStep > 0))
        {
            return;
        }

        var pen = GetPen(tickBrush, 1);
        var labelPen = TickLabelBrush is { } labelBrush ? GetPen(labelBrush, 1) : null;
        var outer = radius - ringThickness - 1;
        var majorLength = Math.Max(4, radius * 0.05);
        var minorLength = Math.Max(2, radius * 0.025);
        var minorCount = Math.Clamp(MinorTickCount, 0, 9);
        var majorTicks = (int)Math.Floor((span / majorStep) + 1e-9);

        if (minorCount > 0 && majorTicks <= 40)
        {
            var minorStep = majorStep / (minorCount + 1);
            var minorStart = (long)Math.Ceiling(minimum / minorStep);
            var minorEnd = (long)Math.Floor(maximum / minorStep);
            if (minorEnd - minorStart <= 400)
            {
                for (var tick = minorStart; tick <= minorEnd; tick++)
                {
                    var value = tick * minorStep;
                    var majorIndex = value / majorStep;
                    if (Math.Abs(majorIndex - Math.Round(majorIndex)) < 1e-6)
                    {
                        continue;
                    }

                    var angle = AngleForValue(value, minimum, maximum, half);
                    var from = OnArc(centerX, centerY, outer, angle);
                    var to = OnArc(centerX, centerY, outer - minorLength, angle);
                    context.DrawLine(pen, from, to);
                }
            }
        }

        var tickStart = (long)Math.Ceiling(minimum / majorStep);
        var tickEnd = (long)Math.Floor(maximum / majorStep);
        if (tickEnd - tickStart > 60)
        {
            tickEnd = tickStart + 60;
        }

        var labelRadius = outer - majorLength - (fontSize * 0.9);
        var tickCount = (int)Math.Min(61, tickEnd - tickStart + 1);
        var labelStep = LabelStride(radius, half, fontSize, tickCount);

        for (var index = 0; index < tickCount; index++)
        {
            var value = (tickStart + index) * majorStep;
            var angle = AngleForValue(value, minimum, maximum, half);
            var from = OnArc(centerX, centerY, outer, angle);
            var to = OnArc(centerX, centerY, outer - majorLength, angle);
            context.DrawLine(pen, from, to);

            if (labelPen is null || labelRadius <= 4 || index % labelStep != 0)
            {
                continue;
            }

            var text = SurfaceDrawing.Format(
                SurfaceDrawing.FormatNumber(value, DecimalsForStep(majorStep)),
                fontSize * 0.85,
                TickLabelBrush!);
            if (IsTooClose(angle, text.Width, labelRadius, endLabels))
            {
                continue;
            }

            var anchor = OnArc(centerX, centerY, labelRadius, angle);
            SurfaceDrawing.DrawCentered(
                context,
                text,
                anchor.X,
                anchor.Y - (text.Height / 2),
                Bounds.Width);
        }
    }

    /// <summary>按主刻度数与弧长决定每隔几个刻度画一次数字（避免数字互相盖住）。</summary>
    private static int LabelStride(double radius, double half, double fontSize, int tickCount)
    {
        if (tickCount <= 1)
        {
            return 1;
        }

        var arcLength = 2 * half * radius;
        var budget = Math.Max(2, (int)(arcLength / Math.Max(18, fontSize * 2.6)));
        return Math.Max(1, (int)Math.Ceiling((double)tickCount / budget));
    }

    /// <summary>量程两端各标一个数字：端点用自己的精度，且与邻近主刻度二选一。</summary>
    private List<EndLabelSlot> BuildEndLabels(
        double radius,
        double ringThickness,
        double half,
        double minimum,
        double maximum,
        double fontSize)
    {
        var slots = new List<EndLabelSlot>(2);
        if (TickLabelBrush is not { } labelBrush)
        {
            return slots;
        }

        var span = maximum - minimum;
        var step = MajorTickStep > 0 && double.IsFinite(MajorTickStep)
            ? MajorTickStep
            : ChartScale.NiceStep(span, 8);
        var labelRadius = radius - ringThickness - (fontSize * 1.8);
        if (labelRadius <= 4)
        {
            return slots;
        }

        var maxWidth = Math.Max(24, (radius - ringThickness) * 0.8);
        Add(minimum, -half);
        Add(maximum, half);
        return slots;

        void Add(double value, double angle)
        {
            if (IsOnMajorTick(value, step))
            {
                return;
            }

            var text = SurfaceDrawing.Format(
                FormatEndpoint(value),
                fontSize * 0.85,
                labelBrush,
                maxWidth);
            slots.Add(new EndLabelSlot(angle, labelRadius, text));
        }
    }

    /// <summary>端点恰好落在主刻度上时不再重复画：那个位置已经有一份刻度数字。</summary>
    private static bool IsOnMajorTick(double value, double step)
    {
        if (!(step > 0) || !double.IsFinite(value))
        {
            return false;
        }

        var index = value / step;
        return Math.Abs(index - Math.Round(index)) < 1e-6;
    }

    internal static string FormatEndpoint(double value) =>
        double.IsFinite(value) ? value.ToString("0.######", CultureInfo.InvariantCulture) : "—";

    private static bool IsTooClose(
        double angle,
        double textWidth,
        double labelRadius,
        IReadOnlyList<EndLabelSlot> endLabels)
    {
        foreach (var slot in endLabels)
        {
            var delta = Math.Abs(angle - slot.Angle);
            if (delta > Math.PI)
            {
                delta = (2 * Math.PI) - delta;
            }

            if (delta * labelRadius < ((textWidth + slot.Text.Width) / 2) + 2)
            {
                return true;
            }
        }

        return false;
    }

    private void DrawEndLabels(
        DrawingContext context,
        double centerX,
        double centerY,
        IReadOnlyList<EndLabelSlot> endLabels)
    {
        foreach (var slot in endLabels)
        {
            var anchor = OnArc(centerX, centerY, slot.Radius, slot.Angle);
            SurfaceDrawing.DrawCentered(
                context,
                slot.Text,
                anchor.X,
                anchor.Y - (slot.Text.Height / 2),
                Bounds.Width);
        }
    }

    private void DrawNeedle(DrawingContext context, double centerX, double centerY, double radius, double angle)
    {
        if (NeedleBrush is not { } needleBrush)
        {
            return;
        }

        var tip = OnArc(centerX, centerY, radius * 0.82, angle);
        var tail = OnArc(centerX, centerY, radius * 0.16, angle + Math.PI);
        context.DrawLine(GetPen(needleBrush, Math.Max(2, radius * 0.025)), tail, tip);

        if (HubBrush is not { } hubBrush)
        {
            return;
        }

        var hubRadius = Math.Max(2.5, radius * 0.045);
        context.DrawEllipse(hubBrush, null, new Point(centerX, centerY), hubRadius, hubRadius);
    }

    /// <summary>标题/读数/单位都画在圆内；宽度按内圆弦长截断，不改变控件尺寸。</summary>
    private void DrawText(
        DrawingContext context,
        double centerX,
        double centerY,
        double radius,
        double thickness,
        double fontSize,
        double minimum,
        double maximum)
    {
        var inner = radius - (thickness / 2);
        var maxWidth = Math.Max(16, inner * 1.6);
        var limit = Bounds.Width;

        if (TitleBrush is { } titleBrush && SurfaceDrawing.HasText(Title))
        {
            var title = SurfaceDrawing.Format(Title, fontSize, titleBrush, maxWidth);
            SurfaceDrawing.DrawCentered(
                context,
                title,
                centerX,
                centerY - (radius * 0.55) - (title.Height / 2),
                limit);
        }

        if (!ShowValue)
        {
            return;
        }

        var displayed = _display.Value;
        var outOfRange = displayed < minimum - 1e-9 || displayed > maximum + 1e-9;
        var valueBrush = (outOfRange ? OutOfRangeBrush : null) ?? ValueBrush;
        var valueFontSize = Math.Clamp(radius * 0.24, fontSize * 1.4, fontSize * 2.6);
        var top = centerY + (radius * 0.06);

        if (valueBrush is { } brush)
        {
            var value = SurfaceDrawing.Format(
                SurfaceDrawing.FormatNumber(displayed, ValueDecimals),
                valueFontSize,
                brush,
                maxWidth);
            SurfaceDrawing.DrawCentered(context, value, centerX, top, limit);
            top += value.Height;
        }

        if (UnitBrush is { } unitBrush && SurfaceDrawing.HasText(Unit))
        {
            var unit = SurfaceDrawing.Format(Unit, fontSize, unitBrush, maxWidth);
            SurfaceDrawing.DrawCentered(context, unit, centerX, top + 1, limit);
        }
    }

    private static void DrawArc(
        DrawingContext context,
        IPen pen,
        double centerX,
        double centerY,
        double radius,
        double fromAngle,
        double toAngle)
    {
        if (Math.Abs(toAngle - fromAngle) < 1e-6)
        {
            return;
        }

        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(toAngle - fromAngle) * 180 / Math.PI / ArcSampleDegrees));
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            for (var index = 0; index <= steps; index++)
            {
                var angle = fromAngle + ((toAngle - fromAngle) * index / steps);
                var point = OnArc(centerX, centerY, radius, angle);
                if (index == 0)
                {
                    figure.BeginFigure(point, isFilled: false);
                }
                else
                {
                    figure.LineTo(point);
                }
            }

            figure.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private IPen GetPen(IBrush brush, double thickness)
    {
        var key = (brush, thickness);
        if (!_pens.TryGetValue(key, out var pen))
        {
            pen = new Pen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            _pens[key] = pen;
        }

        return pen;
    }

    private static Point OnArc(double centerX, double centerY, double radius, double angle) =>
        new(centerX + (radius * Math.Sin(angle)), centerY - (radius * Math.Cos(angle)));

    private static double AngleForValue(double value, double minimum, double maximum, double half)
    {
        var span = maximum - minimum;
        var fraction = span > 0 ? Math.Clamp((value - minimum) / span, 0, 1) : 0;
        return -half + (fraction * 2 * half);
    }

    private static int DecimalsForStep(double step)
    {
        var decimals = ChartAxisFormatter.DecimalsForStep(step);
        return Math.Clamp(decimals, 0, 3);
    }
}
