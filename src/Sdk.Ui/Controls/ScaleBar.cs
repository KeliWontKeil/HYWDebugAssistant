using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls.Charting;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>填充起点：从量程下限开始，还是从零开始（双向数据从中点生长）。</summary>
public enum ScaleFillOrigin
{
    /// <summary>从量程下限开始。</summary>
    Minimum = 0,

    /// <summary>从零开始；零在量程外时夹到最近的端点。</summary>
    Zero = 1,
}

/// <summary>填充颜色的**角色**（映射主题 token，随深浅主题与用户改色跟随）。</summary>
public enum ScaleBarColor
{
    /// <summary>绿（成功色）。</summary>
    Success = 0,

    /// <summary>蓝（主题强调色）。</summary>
    Accent = 1,

    /// <summary>橙（警告色）。</summary>
    Warning = 2,

    /// <summary>红（错误色）。</summary>
    Danger = 3,

    /// <summary>第二强调色（紫）。</summary>
    Alternate = 4,
}

/// <summary>矩形刻度进度条：一个目标值 → 矩形轨道 + 填充 + 刻度（横/纵两向）。</summary>
/// <remarks>
/// <para>
/// 观感取"矩形细条"：轨道不走胶囊形，填充内缩一圈，默认绿色；颜色由
/// <see cref="Color"/> 这个角色决定，只映射主题 token，不写死色值。
/// </para>
/// <para>
/// **不含文字**：标题、读数、单位都由视图用标签拼（<see cref="ModuleLabel"/> /
/// <see cref="ModuleNumeric"/>），控件只画轨道、填充与刻度。文字一旦长在控件里，
/// "一条细条"就得留出一整行标题/读数的位置，既缩不下去也对不齐。
/// </para>
/// <para>
/// 与 <see cref="GaugeDial"/> 同一条口径：只画 <see cref="Value"/>，不判断有没有数据；
/// "保持还是回零"由模块发布的目标值决定。横/纵只是排布差异，刻度算法共用。
/// </para>
/// </remarks>
public sealed class ScaleBar : Control, IModuleViewElement
{
    /// <summary>排布方向。</summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ScaleBar, Orientation>(nameof(Orientation), Orientation.Horizontal);

    /// <summary>量程下限。</summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ScaleBar, double>(nameof(Minimum));

    /// <summary>量程上限。</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ScaleBar, double>(nameof(Maximum), 100);

    /// <summary>目标值（有限实数；合法性由模块保证）。</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ScaleBar, double>(nameof(Value));

    /// <summary>条厚度（DIP；&lt;= 0 = 用可用空间）。</summary>
    public static readonly StyledProperty<double> BarThicknessProperty =
        AvaloniaProperty.Register<ScaleBar, double>(nameof(BarThickness), 14);

    /// <summary>主刻度步长（&lt;= 0 = 自动）。</summary>
    public static readonly StyledProperty<double> TickStepProperty =
        AvaloniaProperty.Register<ScaleBar, double>(nameof(TickStep));

    /// <summary>每个主刻度间的次刻度数（0 = 不画）。</summary>
    public static readonly StyledProperty<int> MinorTickCountProperty =
        AvaloniaProperty.Register<ScaleBar, int>(nameof(MinorTickCount), 4);

    /// <summary>是否显示刻度数字。</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<ScaleBar, bool>(nameof(ShowLabels), true);

    /// <summary>填充起点。</summary>
    public static readonly StyledProperty<ScaleFillOrigin> FillOriginProperty =
        AvaloniaProperty.Register<ScaleBar, ScaleFillOrigin>(nameof(FillOrigin), ScaleFillOrigin.Minimum);

    /// <summary>填充颜色角色。</summary>
    public static readonly StyledProperty<ScaleBarColor> ColorProperty =
        AvaloniaProperty.Register<ScaleBar, ScaleBarColor>(nameof(Color), ScaleBarColor.Success);

    /// <summary>是否平滑。</summary>
    public static readonly StyledProperty<bool> SmoothingEnabledProperty =
        AvaloniaProperty.Register<ScaleBar, bool>(nameof(SmoothingEnabled), true);

    /// <summary>平滑时间常数（毫秒）。</summary>
    public static readonly StyledProperty<double> SmoothingTimeMsProperty =
        AvaloniaProperty.Register<ScaleBar, double>(nameof(SmoothingTimeMs), 120);

    /// <summary>基准字号（只用于刻度数字的大小）。</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<ScaleBar, double>(nameof(FontSize), 10);

    /// <summary>轨道底色。</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ScaleBar, IBrush?>(nameof(TrackBrush));

    /// <summary>轨道边框色。</summary>
    public static readonly StyledProperty<IBrush?> FrameBrushProperty =
        AvaloniaProperty.Register<ScaleBar, IBrush?>(nameof(FrameBrush));

    /// <summary>刻度线色。</summary>
    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<ScaleBar, IBrush?>(nameof(TickBrush));

    /// <summary>刻度数字色。</summary>
    public static readonly StyledProperty<IBrush?> TickLabelBrushProperty =
        AvaloniaProperty.Register<ScaleBar, IBrush?>(nameof(TickLabelBrush));

    /// <summary>填充画刷（内部：由 <see cref="Color"/> 绑定到主题 token）。</summary>
    private static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<ScaleBar, IBrush?>("FillBrush");

    private readonly AnimatedNumber _display = new();

    static ScaleBar()
    {
        AffectsRender<ScaleBar>(
            OrientationProperty,
            MinimumProperty,
            MaximumProperty,
            ValueProperty,
            BarThicknessProperty,
            TickStepProperty,
            MinorTickCountProperty,
            ShowLabelsProperty,
            FillOriginProperty,
            ColorProperty,
            SmoothingEnabledProperty,
            SmoothingTimeMsProperty,
            FontSizeProperty,
            TrackBrushProperty,
            FrameBrushProperty,
            TickBrushProperty,
            TickLabelBrushProperty,
            FillBrushProperty);
    }

    /// <summary>排布方向。</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
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

    /// <summary>条厚度（&lt;= 0 = 用可用空间）。</summary>
    public double BarThickness
    {
        get => GetValue(BarThicknessProperty);
        set => SetValue(BarThicknessProperty, value);
    }

    /// <summary>主刻度步长（&lt;= 0 = 自动）。</summary>
    public double TickStep
    {
        get => GetValue(TickStepProperty);
        set => SetValue(TickStepProperty, value);
    }

    /// <summary>每个主刻度间的次刻度数。</summary>
    public int MinorTickCount
    {
        get => GetValue(MinorTickCountProperty);
        set => SetValue(MinorTickCountProperty, value);
    }

    /// <summary>是否显示刻度数字。</summary>
    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>填充起点。</summary>
    public ScaleFillOrigin FillOrigin
    {
        get => GetValue(FillOriginProperty);
        set => SetValue(FillOriginProperty, value);
    }

    /// <summary>填充颜色角色。</summary>
    public ScaleBarColor Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
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

    /// <summary>基准字号（只用于刻度数字的大小）。</summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>轨道底色。</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>轨道边框色。</summary>
    public IBrush? FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
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

    private IBrush? FillBrush => GetValue(FillBrushProperty);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColorProperty)
        {
            ApplyFillRole();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyFillRole();
    }

    /// <summary>把颜色角色落成主题 token（动态资源：换深浅主题/用户改色自动跟随）。</summary>
    private void ApplyFillRole()
    {
        var token = TokenFor(Color);
        if (token.Length == 0)
        {
            ClearValue(FillBrushProperty);
            return;
        }

        this[!FillBrushProperty] = new DynamicResourceExtension(token);
    }

    private static string TokenFor(ScaleBarColor color) => color switch
    {
        ScaleBarColor.Accent => "AccentPrimary",
        ScaleBarColor.Warning => "SemanticWarn",
        ScaleBarColor.Danger => "SemanticError",
        ScaleBarColor.Alternate => "AccentSecondary",
        _ => "SemanticOk",
    };

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
        if (Orientation == Orientation.Horizontal)
        {
            RenderHorizontal(context, width, height, fontSize);
        }
        else
        {
            RenderVertical(context, width, height, fontSize);
        }
    }

    private void RenderHorizontal(DrawingContext context, double width, double height, double fontSize)
    {
        const double inset = 2;
        var tickBand = ShowLabels ? Math.Max(fontSize * 1.9, 14) : 6;
        var areaHeight = Math.Max(6, height - (2 * inset) - tickBand);
        var thickness = BarThickness > 0 && double.IsFinite(BarThickness)
            ? Math.Min(BarThickness, areaHeight)
            : areaHeight;
        var bar = new Rect(
            inset,
            inset + ((areaHeight - thickness) / 2),
            Math.Max(4, width - (2 * inset)),
            Math.Max(4, thickness));

        DrawBarAndFill(context, bar, horizontal: true);
        DrawTicksHorizontal(context, bar, width, fontSize);
    }

    private void RenderVertical(DrawingContext context, double width, double height, double fontSize)
    {
        const double inset = 2;
        var labelBand = ShowLabels ? Math.Max(fontSize * 4.2, 34) : 8;
        var availableWidth = Math.Max(6, width - (2 * inset) - labelBand);
        var thickness = BarThickness > 0 && double.IsFinite(BarThickness)
            ? Math.Min(BarThickness, availableWidth)
            : availableWidth;
        var bar = new Rect(
            inset + ((availableWidth - thickness) / 2),
            inset,
            Math.Max(4, thickness),
            Math.Max(8, height - (2 * inset)));

        DrawBarAndFill(context, bar, horizontal: false);
        DrawTicksVertical(context, bar, fontSize);
    }

    private void DrawBarAndFill(DrawingContext context, Rect bar, bool horizontal)
    {
        // 矩形轨道：1 DIP 边框 + 底色（Windows 进度条的凹槽观感）。
        context.DrawRectangle(
            TrackBrush,
            FrameBrush is { } frameBrush ? new Pen(frameBrush, 1) : null,
            bar);

        if (FillBrush is not { } fillBrush)
        {
            return;
        }

        var minimum = NormalizedMinimum();
        var maximum = NormalizedMaximum();
        var displayed = Math.Clamp(_display.Value, minimum, maximum);
        var origin = FillOrigin == ScaleFillOrigin.Zero ? Math.Clamp(0, minimum, maximum) : minimum;
        var from = Math.Min(origin, displayed);
        var to = Math.Max(origin, displayed);
        if (to - from <= 1e-9)
        {
            return;
        }

        // 填充内缩一圈：轨道边框仍可见，填充不越出轨道。
        const double inset = 2;
        var inner = new Rect(
            bar.X + inset,
            bar.Y + inset,
            Math.Max(1, bar.Width - (2 * inset)),
            Math.Max(1, bar.Height - (2 * inset)));

        if (horizontal)
        {
            var left = ChartScale.Map(from, minimum, maximum, inner.Left, inner.Right);
            var right = ChartScale.Map(to, minimum, maximum, inner.Left, inner.Right);
            context.DrawRectangle(
                fillBrush,
                null,
                new Rect(left, inner.Top, Math.Max(1, right - left), inner.Height));
        }
        else
        {
            var bottom = ChartScale.Map(from, minimum, maximum, inner.Bottom, inner.Top);
            var top = ChartScale.Map(to, minimum, maximum, inner.Bottom, inner.Top);
            context.DrawRectangle(
                fillBrush,
                null,
                new Rect(inner.Left, top, inner.Width, Math.Max(1, bottom - top)));
        }
    }

    private void DrawTicksHorizontal(DrawingContext context, Rect bar, double width, double fontSize)
    {
        if (TickBrush is not { } tickBrush)
        {
            return;
        }

        var minimum = NormalizedMinimum();
        var maximum = NormalizedMaximum();
        var span = maximum - minimum;
        var majorStep = TickStep > 0 && double.IsFinite(TickStep)
            ? TickStep
            : ChartScale.NiceStep(span, Math.Clamp((int)(bar.Width / 70), 2, 12));
        if (!(majorStep > 0))
        {
            return;
        }

        var pen = new Pen(tickBrush, 1);
        var majorLength = Math.Max(3, fontSize * 0.4);
        var minorLength = Math.Max(2, fontSize * 0.2);
        DrawMinorTicks(context, pen, minorLength, minimum, maximum, majorStep, horizontal: true, bar);

        var tickStart = (long)Math.Ceiling(minimum / majorStep);
        var tickEnd = (long)Math.Floor(maximum / majorStep);
        if (tickEnd - tickStart > 60)
        {
            tickEnd = tickStart + 60;
        }

        for (var tick = tickStart; tick <= tickEnd; tick++)
        {
            var value = tick * majorStep;
            var x = ChartScale.Map(value, minimum, maximum, bar.Left, bar.Right);
            context.DrawLine(pen, new Point(x, bar.Bottom + 1), new Point(x, bar.Bottom + majorLength));

            if (!ShowLabels || TickLabelBrush is not { } labelBrush)
            {
                continue;
            }

            var text = SurfaceDrawing.Format(
                SurfaceDrawing.FormatNumber(value, DecimalsForStep(majorStep)),
                fontSize * 0.9,
                labelBrush);
            var left = Math.Clamp(x - (text.Width / 2), 0, Math.Max(0, width - text.Width));
            context.DrawText(text, new Point(left, bar.Bottom + majorLength + 3));
        }
    }

    private void DrawTicksVertical(DrawingContext context, Rect bar, double fontSize)
    {
        if (TickBrush is not { } tickBrush)
        {
            return;
        }

        var minimum = NormalizedMinimum();
        var maximum = NormalizedMaximum();
        var span = maximum - minimum;
        var majorStep = TickStep > 0 && double.IsFinite(TickStep)
            ? TickStep
            : ChartScale.NiceStep(span, Math.Clamp((int)(bar.Height / 70), 2, 12));
        if (!(majorStep > 0))
        {
            return;
        }

        var pen = new Pen(tickBrush, 1);
        var majorLength = Math.Max(3, fontSize * 0.4);
        var minorLength = Math.Max(2, fontSize * 0.2);
        DrawMinorTicks(context, pen, minorLength, minimum, maximum, majorStep, horizontal: false, bar);

        var tickStart = (long)Math.Ceiling(minimum / majorStep);
        var tickEnd = (long)Math.Floor(maximum / majorStep);
        if (tickEnd - tickStart > 60)
        {
            tickEnd = tickStart + 60;
        }

        for (var tick = tickStart; tick <= tickEnd; tick++)
        {
            var value = tick * majorStep;
            var y = ChartScale.Map(value, minimum, maximum, bar.Bottom, bar.Top);
            context.DrawLine(pen, new Point(bar.Right + 1, y), new Point(bar.Right + majorLength, y));

            if (!ShowLabels || TickLabelBrush is not { } labelBrush)
            {
                continue;
            }

            var text = SurfaceDrawing.Format(
                SurfaceDrawing.FormatNumber(value, DecimalsForStep(majorStep)),
                fontSize * 0.9,
                labelBrush);
            var top = Math.Clamp(y - (text.Height / 2), 0, Math.Max(0, Bounds.Height - text.Height));
            var left = Math.Clamp(
                bar.Right + majorLength + 3,
                bar.Right + 2,
                Math.Max(bar.Right + 2, Bounds.Width - text.Width));
            context.DrawText(text, new Point(left, top));
        }
    }

    private void DrawMinorTicks(
        DrawingContext context,
        IPen pen,
        double length,
        double minimum,
        double maximum,
        double majorStep,
        bool horizontal,
        Rect bar)
    {
        var minorCount = Math.Clamp(MinorTickCount, 0, 9);
        if (minorCount <= 0)
        {
            return;
        }

        var minorStep = majorStep / (minorCount + 1);
        var minorStart = (long)Math.Ceiling(minimum / minorStep);
        var minorEnd = (long)Math.Floor(maximum / minorStep);
        if (minorEnd - minorStart > 400)
        {
            return;
        }

        for (var tick = minorStart; tick <= minorEnd; tick++)
        {
            var value = tick * minorStep;
            var majorIndex = value / majorStep;
            if (Math.Abs(majorIndex - Math.Round(majorIndex)) < 1e-6)
            {
                continue;
            }

            if (horizontal)
            {
                var x = ChartScale.Map(value, minimum, maximum, bar.Left, bar.Right);
                context.DrawLine(pen, new Point(x, bar.Bottom + 1), new Point(x, bar.Bottom + length));
            }
            else
            {
                var y = ChartScale.Map(value, minimum, maximum, bar.Bottom, bar.Top);
                context.DrawLine(pen, new Point(bar.Right + 1, y), new Point(bar.Right + length, y));
            }
        }
    }

    private double NormalizedMinimum() => double.IsFinite(Minimum) ? Minimum : 0;

    private double NormalizedMaximum()
    {
        var minimum = NormalizedMinimum();
        var maximum = Maximum;
        return double.IsFinite(maximum) && maximum > minimum ? maximum : minimum + 1;
    }

    private double NormalizedFontSize() =>
        double.IsFinite(FontSize) && FontSize > 0 ? FontSize : 10;

    private static int DecimalsForStep(double step)
    {
        var decimals = ChartAxisFormatter.DecimalsForStep(step);
        return Math.Clamp(decimals, 0, 3);
    }
}
