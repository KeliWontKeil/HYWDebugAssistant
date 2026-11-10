using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HYWDebugAssistant.Sdk.Ui.Controls.Charting;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>图表 x 轴的显示单位。</summary>
public enum ChartAxisKind
{
    /// <summary>数据编号/索引（整数标签）。</summary>
    Index = 0,

    /// <summary>时间（秒，小数标签）。</summary>
    Time = 1,
}

/// <summary>
/// **通用多序列绘图控件**：坐标轴、网格、曲线、双游标读数。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不做范围选择</b>：选择是独立控件 <see cref="RangeSelector"/> 的职责；本控件不持有选择状态、不画选择块。</item>
///   <item><b>不做跟踪</b>：跟踪是视图的视口策略；视图改 <see cref="XStart"/> / 序列即可，本控件只按给定范围画。</item>
///   <item><b>不做保存/组/通道</b>：本控件不知道数据来源与业务语义，只画 <see cref="Series"/>。</item>
///   <item>游标与绘图区坐标映射内聚：游标留在本控件，避免把视口/序列复制给第二个控件。</item>
/// </list>
/// </remarks>
public sealed class ChartSurface : Control
{
    /// <summary>序列集合（视图创建一次并复用）。</summary>
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<ChartSurface, IReadOnlyList<ChartSeries>?>(nameof(Series));

    /// <summary>数据内容失效号；递增才重绘曲线。</summary>
    public static readonly StyledProperty<long> DataVersionProperty =
        AvaloniaProperty.Register<ChartSurface, long>(nameof(DataVersion));

    /// <summary>首点 x（Index 或 Time 单位）。</summary>
    public static readonly StyledProperty<double> XStartProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(XStart));

    /// <summary>点间距（Index 或 Time 单位）。</summary>
    public static readonly StyledProperty<double> XStepProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(XStep), 1);

    /// <summary>窗口终点（Index 或 Time 单位；NaN/≤XStart = 按点数推导）。</summary>
    public static readonly StyledProperty<double> XEndProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(XEnd), double.NaN);

    /// <summary>竖向参考线（周期刻度等）。</summary>
    public static readonly StyledProperty<IReadOnlyList<ChartReferenceLine>?> ReferenceLinesProperty =
        AvaloniaProperty.Register<ChartSurface, IReadOnlyList<ChartReferenceLine>?>(nameof(ReferenceLines));

    /// <summary>x 轴单位。</summary>
    public static readonly StyledProperty<ChartAxisKind> XAxisKindProperty =
        AvaloniaProperty.Register<ChartSurface, ChartAxisKind>(nameof(XAxisKind));

    /// <summary>x 轴标题（模块本地化文本）。</summary>
    public static readonly StyledProperty<string> XAxisTitleProperty =
        AvaloniaProperty.Register<ChartSurface, string>(nameof(XAxisTitle), string.Empty);

    /// <summary>y 轴标题（模块本地化文本）。</summary>
    public static readonly StyledProperty<string> YAxisTitleProperty =
        AvaloniaProperty.Register<ChartSurface, string>(nameof(YAxisTitle), string.Empty);

    /// <summary>y 轴下限。</summary>
    public static readonly StyledProperty<double> YMinProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(YMin));

    /// <summary>y 轴上限。</summary>
    public static readonly StyledProperty<double> YMaxProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(YMax), 1);

    /// <summary>x 刻度步长；&lt;= 0 = 自动。</summary>
    public static readonly StyledProperty<double> XTickStepProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(XTickStep));

    /// <summary>y 刻度步长；&lt;= 0 = 自动。</summary>
    public static readonly StyledProperty<double> YTickStepProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(YTickStep));

    /// <summary>是否显示游标。</summary>
    public static readonly StyledProperty<bool> ShowCursorsProperty =
        AvaloniaProperty.Register<ChartSurface, bool>(nameof(ShowCursors));

    /// <summary>游标 A 的屏幕比例位置（0–1）。</summary>
    public static readonly StyledProperty<double> CursorAFractionProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(CursorAFraction), 0.33);

    /// <summary>游标 B 的屏幕比例位置（0–1）。</summary>
    public static readonly StyledProperty<double> CursorBFractionProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(CursorBFraction), 0.66);

    /// <summary>是否显示 Y 轴游标。</summary>
    public static readonly StyledProperty<bool> ShowYCursorsProperty =
        AvaloniaProperty.Register<ChartSurface, bool>(nameof(ShowYCursors));

    /// <summary>Y 游标 A 的屏幕比例位置（0–1，0 = 顶部）。</summary>
    public static readonly StyledProperty<double> YCursorAFractionProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(YCursorAFraction), 0.33);

    /// <summary>Y 游标 B 的屏幕比例位置（0–1，0 = 顶部）。</summary>
    public static readonly StyledProperty<double> YCursorBFractionProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(YCursorBFraction), 0.66);

    /// <summary>Y 游标 A 的标签字母（显示器用 m）。</summary>
    public static readonly StyledProperty<string> YCursorALabelProperty =
        AvaloniaProperty.Register<ChartSurface, string>(nameof(YCursorALabel), "A");

    /// <summary>Y 游标 B 的标签字母（显示器用 n）。</summary>
    public static readonly StyledProperty<string> YCursorBLabelProperty =
        AvaloniaProperty.Register<ChartSurface, string>(nameof(YCursorBLabel), "B");

    /// <summary>
    /// **绘图区相对控件边界的内缩**（左、上、右、下，DIP）。
    /// </summary>
    /// <remarks>
    /// 这是绘图区边距的唯一来源：刻度文字、游标标签、轴标题都算在这块内缩里，控件不再
    /// 按 <see cref="ShowCursors"/> 或固定常量额外增加顶部/底部预留。调用方按实际需要给出；
    /// 设置 <see cref="XAxisTitle"/> 时还必须把标题高度计入底部。
    /// </remarks>
    public static readonly StyledProperty<Thickness> PlotInsetProperty =
        AvaloniaProperty.Register<ChartSurface, Thickness>(nameof(PlotInset), new Thickness(40, 20, 4, 20));

    /// <summary>绘图区背景。</summary>
    public static readonly StyledProperty<IBrush?> PlotBackgroundProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(PlotBackground));

    /// <summary>轴/边框色。</summary>
    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(AxisBrush));

    /// <summary>网格色。</summary>
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(GridBrush));

    /// <summary>刻度/轴标题文字色。</summary>
    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(TextBrush));

    /// <summary>游标线色。</summary>
    public static readonly StyledProperty<IBrush?> CursorBrushProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(CursorBrush));

    /// <summary>游标标签底色。</summary>
    public static readonly StyledProperty<IBrush?> CursorLabelBackgroundProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(CursorLabelBackground));

    /// <summary>游标标签边框色。</summary>
    public static readonly StyledProperty<IBrush?> CursorLabelBorderProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(CursorLabelBorder));

    /// <summary>游标标签文字色。</summary>
    public static readonly StyledProperty<IBrush?> CursorLabelTextProperty =
        AvaloniaProperty.Register<ChartSurface, IBrush?>(nameof(CursorLabelText));

    /// <summary>图表文字字号。</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<ChartSurface, double>(nameof(FontSize), 10);

    private readonly Dictionary<Color, IBrush> _colorCache = [];
    private readonly Dictionary<(IBrush Brush, double Thickness), IPen> _pens = [];

    private Rect _plotArea;
    private Rect _cursorALabel;
    private Rect _cursorBLabel;
    private Rect _cursorYLabelA;
    private Rect _cursorYLabelB;
    private int _draggingCursor;
    private int _draggingYCursor;

    static ChartSurface()
    {
        AffectsRender<ChartSurface>(
            SeriesProperty,
            DataVersionProperty,
            XStartProperty,
            XStepProperty,
            XEndProperty,
            ReferenceLinesProperty,
            XAxisKindProperty,
            XAxisTitleProperty,
            YAxisTitleProperty,
            YMinProperty,
            YMaxProperty,
            XTickStepProperty,
            YTickStepProperty,
            ShowCursorsProperty,
            CursorAFractionProperty,
            CursorBFractionProperty,
            ShowYCursorsProperty,
            YCursorAFractionProperty,
            YCursorBFractionProperty,
            YCursorALabelProperty,
            YCursorBLabelProperty,
            PlotInsetProperty,
            PlotBackgroundProperty,
            AxisBrushProperty,
            GridBrushProperty,
            TextBrushProperty,
            CursorBrushProperty,
            CursorLabelBackgroundProperty,
            CursorLabelBorderProperty,
            CursorLabelTextProperty,
            FontSizeProperty);
    }

    /// <summary>创建图表控件。</summary>
    public ChartSurface()
    {
        // 图表必须填满宿主；否则某些容器会按内容期望尺寸把它放在中间，出现无意义留白。
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>序列集合。</summary>
    public IReadOnlyList<ChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>数据内容失效号。</summary>
    public long DataVersion
    {
        get => GetValue(DataVersionProperty);
        set => SetValue(DataVersionProperty, value);
    }

    /// <summary>首点 x。</summary>
    public double XStart
    {
        get => GetValue(XStartProperty);
        set => SetValue(XStartProperty, value);
    }

    /// <summary>点间距。</summary>
    public double XStep
    {
        get => GetValue(XStepProperty);
        set => SetValue(XStepProperty, value);
    }

    /// <summary>窗口终点（NaN/≤XStart = 按点数推导）。</summary>
    public double XEnd
    {
        get => GetValue(XEndProperty);
        set => SetValue(XEndProperty, value);
    }

    /// <summary>竖向参考线（周期刻度等）。</summary>
    public IReadOnlyList<ChartReferenceLine>? ReferenceLines
    {
        get => GetValue(ReferenceLinesProperty);
        set => SetValue(ReferenceLinesProperty, value);
    }

    /// <summary>x 轴单位。</summary>
    public ChartAxisKind XAxisKind
    {
        get => GetValue(XAxisKindProperty);
        set => SetValue(XAxisKindProperty, value);
    }

    /// <summary>x 轴标题。</summary>
    public string XAxisTitle
    {
        get => GetValue(XAxisTitleProperty);
        set => SetValue(XAxisTitleProperty, value);
    }

    /// <summary>y 轴标题。</summary>
    public string YAxisTitle
    {
        get => GetValue(YAxisTitleProperty);
        set => SetValue(YAxisTitleProperty, value);
    }

    /// <summary>y 轴下限。</summary>
    public double YMin
    {
        get => GetValue(YMinProperty);
        set => SetValue(YMinProperty, value);
    }

    /// <summary>y 轴上限。</summary>
    public double YMax
    {
        get => GetValue(YMaxProperty);
        set => SetValue(YMaxProperty, value);
    }

    /// <summary>x 刻度步长（&lt;= 0 = 自动）。</summary>
    public double XTickStep
    {
        get => GetValue(XTickStepProperty);
        set => SetValue(XTickStepProperty, value);
    }

    /// <summary>y 刻度步长（&lt;= 0 = 自动）。</summary>
    public double YTickStep
    {
        get => GetValue(YTickStepProperty);
        set => SetValue(YTickStepProperty, value);
    }

    /// <summary>是否显示游标。</summary>
    public bool ShowCursors
    {
        get => GetValue(ShowCursorsProperty);
        set => SetValue(ShowCursorsProperty, value);
    }

    /// <summary>游标 A 屏幕比例位置（0–1）。</summary>
    public double CursorAFraction
    {
        get => GetValue(CursorAFractionProperty);
        set => SetValue(CursorAFractionProperty, ClampFraction(value));
    }

    /// <summary>游标 B 屏幕比例位置（0–1）。</summary>
    public double CursorBFraction
    {
        get => GetValue(CursorBFractionProperty);
        set => SetValue(CursorBFractionProperty, ClampFraction(value));
    }

    /// <summary>是否显示 Y 轴游标。</summary>
    public bool ShowYCursors
    {
        get => GetValue(ShowYCursorsProperty);
        set => SetValue(ShowYCursorsProperty, value);
    }

    /// <summary>Y 游标 A 的屏幕比例位置（0 = 顶部）。</summary>
    public double YCursorAFraction
    {
        get => GetValue(YCursorAFractionProperty);
        set => SetValue(YCursorAFractionProperty, ClampFraction(value));
    }

    /// <summary>Y 游标 B 的屏幕比例位置（0 = 顶部）。</summary>
    public double YCursorBFraction
    {
        get => GetValue(YCursorBFractionProperty);
        set => SetValue(YCursorBFractionProperty, ClampFraction(value));
    }

    /// <summary>Y 游标 A 的标签字母。</summary>
    public string YCursorALabel
    {
        get => GetValue(YCursorALabelProperty);
        set => SetValue(YCursorALabelProperty, value ?? string.Empty);
    }

    /// <summary>Y 游标 B 的标签字母。</summary>
    public string YCursorBLabel
    {
        get => GetValue(YCursorBLabelProperty);
        set => SetValue(YCursorBLabelProperty, value ?? string.Empty);
    }

    /// <summary>绘图区相对控件边界的内缩（左、上、右、下，DIP）。</summary>
    public Thickness PlotInset
    {
        get => GetValue(PlotInsetProperty);
        set => SetValue(PlotInsetProperty, value);
    }

    /// <summary>绘图区背景。</summary>
    public IBrush? PlotBackground
    {
        get => GetValue(PlotBackgroundProperty);
        set => SetValue(PlotBackgroundProperty, value);
    }

    /// <summary>轴/边框色。</summary>
    public IBrush? AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    /// <summary>网格色。</summary>
    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>刻度/轴标题文字色。</summary>
    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>游标线色。</summary>
    public IBrush? CursorBrush
    {
        get => GetValue(CursorBrushProperty);
        set => SetValue(CursorBrushProperty, value);
    }

    /// <summary>游标标签底色。</summary>
    public IBrush? CursorLabelBackground
    {
        get => GetValue(CursorLabelBackgroundProperty);
        set => SetValue(CursorLabelBackgroundProperty, value);
    }

    /// <summary>游标标签边框色。</summary>
    public IBrush? CursorLabelBorder
    {
        get => GetValue(CursorLabelBorderProperty);
        set => SetValue(CursorLabelBorderProperty, value);
    }

    /// <summary>游标标签文字色。</summary>
    public IBrush? CursorLabelText
    {
        get => GetValue(CursorLabelTextProperty);
        set => SetValue(CursorLabelTextProperty, value);
    }

    /// <summary>图表文字字号。</summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// **按实际绘图区宽度和当前标签文本测量**，最多能放下多少个 X 轴主刻度。
    /// </summary>
    /// <returns>可读上限；布局尚未完成时返回 0（表示未知）。</returns>
    /// <remarks>
    /// 不写死刻度个数：这里只提供"标签宽度 + 最小间距"这条可读密度规则，
    /// 个数由控件当前宽度和字体实际测量得出。图表与数据轴字号不同，因此各自算自己的上限。
    /// </remarks>
    public int GetMaxReadableXTicks()
    {
        var plot = _plotArea.Width > 1 ? _plotArea : CalculatePlotArea(new Rect(Bounds.Size));
        if (plot.Width <= 1)
        {
            return 0;
        }

        var startLabel = ChartScale.Format(XStart, XAxisKind);
        var endValue = double.IsFinite(XEnd) && XEnd > XStart
            ? XEnd
            : XStart + Math.Max(1, VisiblePointCount() - 1) * (XStep > 0 ? XStep : 1);
        var endLabel = ChartScale.Format(endValue, XAxisKind);
        // 绝对色：主题资源缺失时的标签测量兜底。
        var labelWidth = Math.Max(
            Format(startLabel, TextBrush ?? Brushes.Gray).Width,
            Format(endLabel, TextBrush ?? Brushes.Gray).Width);
        var minSpacing = Math.Max(16, labelWidth + Math.Max(6, FontSize * 0.8));
        return Math.Max(1, (int)(plot.Width / minSpacing));
    }

    /// <summary>
    /// **按实际绘图区高度和字体行高测量**，最多能放下多少个 Y 轴主刻度。
    /// </summary>
    /// <returns>可读上限；布局尚未完成时返回 0（表示未知）。</returns>
    public int GetMaxReadableYTicks()
    {
        var plot = _plotArea.Height > 1 ? _plotArea : CalculatePlotArea(new Rect(Bounds.Size));
        if (plot.Height <= 1)
        {
            return 0;
        }

        // 绝对色：主题资源缺失时的行高测量兜底。
        var lineHeight = Format("0", TextBrush ?? Brushes.Gray).Height;
        var minSpacing = Math.Max(10, lineHeight + Math.Max(4, FontSize * 0.4));
        return Math.Max(1, (int)(plot.Height / minSpacing));
    }

    /// <summary>游标位置变化（拖动产生；程序设置属性不触发）。</summary>
    public event EventHandler? CursorChanged;

    /// <summary>Y 轴游标位置变化（拖动产生；程序设置属性不触发）。</summary>
    public event EventHandler? YCursorChanged;

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        _plotArea = CalculatePlotArea(new Rect(finalSize));
        return arranged;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var plot = CalculatePlotArea(bounds);
        _plotArea = plot;

        if (PlotBackground is { } background)
        {
            context.DrawRectangle(background, null, bounds);
        }

        DrawGridAndAxes(context, plot);
        DrawReferenceLines(context, plot);
        DrawSeries(context, plot);
        DrawCursors(context, plot);
        DrawYCursors(context, plot);
    }

    /// <summary>把当前图表渲染为 PNG；失败返回 null（不抛）。</summary>
    public byte[]? CapturePng()
    {
        var bounds = Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return null;
        }

        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height)));

        try
        {
            using var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            using (var drawing = target.CreateDrawingContext())
            {
                Render(drawing);
            }

            using var stream = new MemoryStream();
            target.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!ShowCursors && !ShowYCursors)
        {
            return;
        }

        var point = e.GetPosition(this);
        var plot = _plotArea;
        if (plot.Width <= 1 || plot.Height <= 1)
        {
            return;
        }

        if (ShowCursors)
        {
            var tolerance = 6;
            var xa = plot.Left + (CursorAFraction * plot.Width);
            var xb = plot.Left + (CursorBFraction * plot.Width);

            if (_cursorALabel.Contains(point) || ChartScale.Hit(point.X, xa, tolerance) == 1 && point.Y <= plot.Bottom)
            {
                _draggingCursor = 1;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (_cursorBLabel.Contains(point) || ChartScale.Hit(point.X, xb, tolerance) == 1 && point.Y <= plot.Bottom)
            {
                _draggingCursor = 2;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        if (!ShowYCursors)
        {
            return;
        }

        var ya = plot.Top + (YCursorAFraction * plot.Height);
        var yb = plot.Top + (YCursorBFraction * plot.Height);
        if (_cursorYLabelA.Contains(point) || Math.Abs(point.Y - ya) <= 5 && point.X <= plot.Left + 8)
        {
            _draggingYCursor = 1;
        }
        else if (_cursorYLabelB.Contains(point) || Math.Abs(point.Y - yb) <= 5 && point.X <= plot.Left + 8)
        {
            _draggingYCursor = 2;
        }
        else
        {
            return;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggingCursor == 0 && _draggingYCursor == 0)
        {
            return;
        }

        var plot = _plotArea;
        if (plot.Width <= 1 || plot.Height <= 1)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (_draggingYCursor != 0)
        {
            var yFraction = ChartScale.Clamp((point.Y - plot.Top) / plot.Height, 0, 1);
            if (_draggingYCursor == 1)
            {
                YCursorAFraction = yFraction;
            }
            else
            {
                YCursorBFraction = yFraction;
            }

            YCursorChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var fraction = ChartScale.Clamp((point.X - plot.Left) / plot.Width, 0, 1);
        if (_draggingCursor == 1)
        {
            CursorAFraction = fraction;
        }
        else
        {
            CursorBFraction = fraction;
        }

        CursorChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggingCursor == 0 && _draggingYCursor == 0)
        {
            return;
        }

        _draggingCursor = 0;
        _draggingYCursor = 0;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _draggingCursor = 0;
        _draggingYCursor = 0;
    }

    private static double ClampFraction(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private Rect CalculatePlotArea(Rect bounds)
    {
        // 四边只取调用方给的内缩；夹紧到实际边界，避免极小窗口出现负宽高。
        var left = Math.Clamp(PlotInset.Left, 0, bounds.Width);
        var top = Math.Clamp(PlotInset.Top, 0, bounds.Height);
        var right = Math.Clamp(PlotInset.Right, 0, Math.Max(0, bounds.Width - left));
        var bottom = Math.Clamp(PlotInset.Bottom, 0, Math.Max(0, bounds.Height - top));
        var width = Math.Max(0, bounds.Width - left - right);
        var height = Math.Max(0, bounds.Height - top - bottom);
        return new Rect(left, top, width, height);
    }

    private double ResolveXEnd(double stepX, int count)
    {
        if (double.IsFinite(XEnd) && XEnd > XStart)
        {
            return XEnd;
        }

        return XStart + (stepX * Math.Max(1, count - 1));
    }

    private void DrawReferenceLines(DrawingContext context, Rect plot)
    {
        if (ReferenceLines is not { Count: > 0 } lines || plot.Width <= 1)
        {
            return;
        }

        var stepX = XStep > 0 && !double.IsNaN(XStep) && !double.IsInfinity(XStep) ? XStep : 1;
        var xMin = XStart;
        var xMax = ResolveXEnd(stepX, VisiblePointCount());
        if (xMax <= xMin)
        {
            xMax = xMin + 1;
        }

        // 绝对色：主题资源缺失时的参考线兜底。
        var gridBrush = GridBrush ?? Brushes.LightGray;

        foreach (var line in lines)
        {
            if (line.X <= xMin || line.X >= xMax || !double.IsFinite(line.X))
            {
                continue;
            }

            var x = ChartScale.Map(line.X, xMin, xMax, plot.Left, plot.Right);
            var pen = line.Dashed
                ? new Pen(gridBrush, Math.Max(0.5, line.Thickness), new DashStyle([4, 4], 0))
                : new Pen(gridBrush, Math.Max(0.5, line.Thickness));
            context.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private void DrawGridAndAxes(DrawingContext context, Rect plot)
    {
        if (plot.Width <= 1 || plot.Height <= 1)
        {
            return;
        }

        var series = Series;
        var maxCount = 1;
        if (series is not null)
        {
            for (var index = 0; index < series.Count; index++)
            {
                var item = series[index];
                if (item.Visible && item.Count > maxCount)
                {
                    maxCount = item.Count;
                }
            }
        }

        var stepX = XStep > 0 && !double.IsNaN(XStep) && !double.IsInfinity(XStep) ? XStep : 1;
        var xMin = XStart;
        var xMax = ResolveXEnd(stepX, maxCount);
        if (xMax <= xMin)
        {
            xMax = xMin + 1;
        }

        var yMin = YMin;
        var yMax = YMax;
        if (yMax <= yMin)
        {
            var center = (yMin + yMax) / 2;
            yMin = center - 1;
            yMax = center + 1;
        }

        // 绝对色：主题资源缺失时的轴/网格/文字兜底。
        var axisBrush = AxisBrush ?? Brushes.Gray;
        var gridBrush = GridBrush ?? Brushes.LightGray;
        var textBrush = TextBrush ?? Brushes.Gray;
        var axisPen = new Pen(axisBrush, 1);
        var gridPen = new Pen(gridBrush, 1);

        var xStep = XTickStep > 0 && !double.IsNaN(XTickStep) && !double.IsInfinity(XTickStep)
            ? XTickStep
            : ChartScale.NiceStep(xMax - xMin, Math.Max(2, (int)(plot.Width / 80)));
        var yStep = YTickStep > 0 && !double.IsNaN(YTickStep) && !double.IsInfinity(YTickStep)
            ? YTickStep
            : ChartScale.NiceStep(yMax - yMin, Math.Max(2, (int)(plot.Height / 40)));

        var xStart = (long)Math.Ceiling(xMin / xStep);
        var xEnd = (long)Math.Floor(xMax / xStep);
        if (xEnd - xStart > 200)
        {
            xEnd = xStart + 200;
        }

        var xTickCount = Math.Max(1, (int)Math.Min(200, xEnd - xStart + 1));
        var xLabelChars = ChartAxisFormatter.MaxChars(
            (plot.Width / xTickCount) - 4,
            FontSize);
        var yLabelChars = ChartAxisFormatter.MaxChars(plot.Left - 10, FontSize);

        for (var tick = xStart; tick <= xEnd; tick++)
        {
            var value = tick * xStep;
            var x = ChartScale.Map(value, xMin, xMax, plot.Left, plot.Right);
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            DrawText(
                context,
                ChartAxisFormatter.Format(value, XAxisKind, xStep, xLabelChars),
                textBrush,
                new Point(x, plot.Bottom + 6),
                center: true);
        }

        var yStart = (long)Math.Ceiling(yMin / yStep);
        var yEnd = (long)Math.Floor(yMax / yStep);
        if (yEnd - yStart > 200)
        {
            yEnd = yStart + 200;
        }

        for (var tick = yStart; tick <= yEnd; tick++)
        {
            var value = tick * yStep;
            var y = ChartScale.Map(value, yMin, yMax, plot.Bottom, plot.Top);
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            context.DrawLine(axisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
            DrawText(
                context,
                ChartAxisFormatter.FormatNumber(value, yStep, yLabelChars),
                textBrush,
                new Point(plot.Left - 6, y),
                center: false,
                right: true);
        }

        // 坐标轴外框：左、下就是坐标轴；补上、右两条边线，只框绘图区，不含刻度文字/游标标签/图例。
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Right, plot.Top), new Point(plot.Right, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Right, plot.Top));

        if (XAxisTitle.Length > 0)
        {
            DrawText(context, XAxisTitle, textBrush, new Point(plot.Left + (plot.Width / 2), Bounds.Height - 12), center: true);
        }

        if (YAxisTitle.Length > 0)
        {
            DrawYAxisTitle(context, plot, YAxisTitle, textBrush);
        }
    }

    private void DrawYAxisTitle(DrawingContext context, Rect plot, string text, IBrush textBrush)
    {
        using (context.PushTransform(Matrix.CreateRotation(Math.PI / 2)))
        {
            DrawText(
                context,
                text,
                textBrush,
                new Point(plot.Top + (plot.Height / 2), -6),
                center: true);
        }
    }

    private void DrawSeries(DrawingContext context, Rect plot)
    {
        if (Series is not { Count: > 0 } series)
        {
            return;
        }

        var maxCount = 1;
        for (var index = 0; index < series.Count; index++)
        {
            var item = series[index];
            if (item.Visible && item.Count > maxCount)
            {
                maxCount = item.Count;
            }
        }

        var stepX = XStep > 0 && !double.IsNaN(XStep) && !double.IsInfinity(XStep) ? XStep : 1;
        var xMin = XStart;
        var xMax = ResolveXEnd(stepX, maxCount);
        if (xMax <= xMin)
        {
            xMax = xMin + 1;
        }

        var yMin = YMin;
        var yMax = YMax;
        if (yMax <= yMin)
        {
            var center = (yMin + yMax) / 2;
            yMin = center - 1;
            yMax = center + 1;
        }

        using (context.PushClip(plot))
        {
            for (var index = 0; index < series.Count; index++)
            {
                var item = series[index];
                if (!item.Visible || item.Count < 2 || item.Values.Length == 0)
                {
                    continue;
                }

                var count = Math.Min(item.Count, item.Values.Length);
                var values = item.Values.Span;
                var geometry = new StreamGeometry();
                using (var figure = geometry.Open())
                {
                    var started = false;

                    for (var pointIndex = 0; pointIndex < count; pointIndex++)
                    {
                        var value = values[pointIndex];
                        if (double.IsNaN(value) || double.IsInfinity(value))
                        {
                            started = false;
                            continue;
                        }

                        var x = plot.Left + ((pointIndex * stepX) / (xMax - xMin) * plot.Width);
                        var y = ChartScale.Map(value, yMin, yMax, plot.Bottom, plot.Top);

                        if (!started)
                        {
                            figure.BeginFigure(new Point(x, y), isFilled: false);
                            started = true;
                        }
                        else
                        {
                            figure.LineTo(new Point(x, y));
                        }
                    }

                    if (started)
                    {
                        figure.EndFigure(isClosed: false);
                    }
                }

                var brush = ResolveSeriesBrush(item);
                context.DrawGeometry(null, GetPen(brush, item.Thickness), geometry);
            }
        }
    }

    private void DrawCursors(DrawingContext context, Rect plot)
    {
        if (!ShowCursors || plot.Width <= 1 || plot.Height <= 1)
        {
            _cursorALabel = default;
            _cursorBLabel = default;
            return;
        }

        // 绝对色：主题资源缺失时的游标兜底。
        var cursorBrush = CursorBrush ?? Brushes.DodgerBlue;
        var pen = GetPen(cursorBrush, 1);
        var xMin = XStart;
        var stepX = XStep > 0 && !double.IsNaN(XStep) && !double.IsInfinity(XStep) ? XStep : 1;
        var xMax = ResolveXEnd(stepX, VisiblePointCount());
        if (xMax <= xMin)
        {
            xMax = xMin + 1;
        }

        var xa = plot.Left + (CursorAFraction * plot.Width);
        var xb = plot.Left + (CursorBFraction * plot.Width);

        context.DrawLine(pen, new Point(xa, plot.Top), new Point(xa, plot.Bottom));
        context.DrawLine(pen, new Point(xb, plot.Top), new Point(xb, plot.Bottom));

        // 绝对色：游标标签在主题资源缺失时的兜底。
        var labelBackground = CursorLabelBackground ?? Brushes.White;
        var labelBorder = CursorLabelBorder ?? Brushes.Gray;
        var labelText = CursorLabelText ?? Brushes.Black;
        var valueA = xMin + (ChartScale.Clamp(CursorAFraction, 0, 1) * (xMax - xMin));
        var valueB = xMin + (ChartScale.Clamp(CursorBFraction, 0, 1) * (xMax - xMin));
        var cursorStep = Math.Max(1e-9, (xMax - xMin) / 1000);

        _cursorALabel = DrawCursorLabel(context, plot, xa, FormatXCursorValue("A", valueA, cursorStep), labelBackground, labelBorder, labelText);
        _cursorBLabel = DrawCursorLabel(context, plot, xb, FormatXCursorValue("B", valueB, cursorStep), labelBackground, labelBorder, labelText);

        // Δ = 第二游标 − 第一游标：反向拖动时为负，符合示波器读数习惯。
        var delta = valueB - valueA;
        DrawCursorLabel(context, plot, (xa + xb) / 2, FormatXCursorValue("Δ", delta, cursorStep), labelBackground, labelBorder, labelText);
    }

    private void DrawYCursors(DrawingContext context, Rect plot)
    {
        if (!ShowYCursors || plot.Width <= 1 || plot.Height <= 1)
        {
            _cursorYLabelA = default;
            _cursorYLabelB = default;
            return;
        }

        // 绝对色：主题资源缺失时的游标兜底。
        var cursorBrush = CursorBrush ?? Brushes.DodgerBlue;
        var pen = GetPen(cursorBrush, 1);
        var yMin = YMin;
        var yMax = YMax;
        if (yMax <= yMin)
        {
            var center = (yMin + yMax) / 2;
            yMin = center - 1;
            yMax = center + 1;
        }

        var ya = plot.Top + (YCursorAFraction * plot.Height);
        var yb = plot.Top + (YCursorBFraction * plot.Height);
        context.DrawLine(pen, new Point(plot.Left, ya), new Point(plot.Right, ya));
        context.DrawLine(pen, new Point(plot.Left, yb), new Point(plot.Right, yb));

        // 绝对色：游标标签在主题资源缺失时的兜底。
        var labelBackground = CursorLabelBackground ?? Brushes.White;
        var labelBorder = CursorLabelBorder ?? Brushes.Gray;
        var labelText = CursorLabelText ?? Brushes.Black;
        var valueA = yMax - (ChartScale.Clamp(YCursorAFraction, 0, 1) * (yMax - yMin));
        var valueB = yMax - (ChartScale.Clamp(YCursorBFraction, 0, 1) * (yMax - yMin));
        var cursorStep = Math.Max(1e-9, (yMax - yMin) / 1000);

        _cursorYLabelA = DrawYCursorLabel(
            context,
            plot,
            ya,
            FormatYCursorValue(YCursorALabel, valueA, cursorStep),
            labelBackground,
            labelBorder,
            labelText);
        _cursorYLabelB = DrawYCursorLabel(
            context,
            plot,
            yb,
            FormatYCursorValue(YCursorBLabel, valueB, cursorStep),
            labelBackground,
            labelBorder,
            labelText);

        // Δ = n − m：不取绝对值。
        var delta = valueB - valueA;
        DrawYCursorLabel(
            context,
            plot,
            (ya + yb) / 2,
            FormatYCursorValue("Δ", delta, cursorStep),
            labelBackground,
            labelBorder,
            labelText);
    }

    private Rect DrawYCursorLabel(
        DrawingContext context,
        Rect plot,
        double y,
        string text,
        IBrush background,
        IBrush border,
        IBrush textBrush)
    {
        var formatted = Format(text, textBrush);
        var width = formatted.Width + 8;
        var height = formatted.Height + 4;
        // 游标标签放在绘图区外左侧；允许覆盖 y 轴刻度文字（用户明确接受）。
        var left = Math.Max(0, plot.Left - width - 2);
        var top = ChartScale.Clamp(y - (height / 2), 0, Math.Max(0, Bounds.Height - height));
        var rect = new Rect(left, top, width, height);
        context.DrawRectangle(background, new Pen(border, 1), rect, 2, 2);
        context.DrawText(formatted, new Point(rect.Left + 4, rect.Top + 2));
        return rect;
    }

    private Rect DrawCursorLabel(
        DrawingContext context,
        Rect plot,
        double x,
        string text,
        IBrush background,
        IBrush border,
        IBrush textBrush)
    {
        var formatted = Format(text, textBrush);
        var width = formatted.Width + 8;
        var height = formatted.Height + 4;
        // A/B/Δ 同一高度：全部放在绘图区上方，且与顶边保留间距。
        var top = Math.Max(0, plot.Top - height - 4);
        var left = x - (width / 2);
        left = ChartScale.Clamp(left, 0, Math.Max(0, Bounds.Width - width));

        var rect = new Rect(left, top, width, height);
        context.DrawRectangle(background, new Pen(border, 1), rect, 2, 2);
        context.DrawText(formatted, new Point(rect.Left + 4, rect.Top + 2));
        return rect;
    }

    private string FormatXCursorValue(string symbol, double value, double step) =>
        symbol + " " + ChartAxisFormatter.Format(value, XAxisKind, step, 14);

    private static string FormatYCursorValue(string symbol, double value, double step) =>
        symbol + " " + ChartAxisFormatter.FormatNumber(value, step, 14);

    private int VisiblePointCount()
    {
        if (Series is not { Count: > 0 } series)
        {
            return 1;
        }

        var max = 1;
        for (var index = 0; index < series.Count; index++)
        {
            var item = series[index];
            if (item.Visible && item.Count > max)
            {
                max = item.Count;
            }
        }

        return max;
    }

    private IBrush ResolveSeriesBrush(ChartSeries series)
    {
        if (series.Color is { } color)
        {
            if (!_colorCache.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                _colorCache[color] = brush;
            }

            return brush;
        }

        // 曲线颜色由调用方（模块/视图）显式给出；这里只作主题兜底。
        // 绝对色：主题资源缺失时的曲线兜底色。
        return this.TryFindResource("AccentSecondary", out var value) && value is IBrush resolved
            ? resolved
            : Brushes.DodgerBlue;
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

    private void DrawText(
        DrawingContext context,
        string text,
        IBrush brush,
        Point origin,
        bool center = false,
        bool right = false)
    {
        var formatted = Format(text, brush);
        var x = origin.X;
        var y = origin.Y;

        if (center)
        {
            x = ChartScale.Clamp(x - (formatted.Width / 2), 0, Math.Max(0, Bounds.Width - formatted.Width));
        }
        else if (right)
        {
            x = Math.Max(0, x - formatted.Width);
        }

        context.DrawText(formatted, new Point(x, y));
    }

    private FormattedText Format(string text, IBrush brush) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            FontSize,
            brush);
}
