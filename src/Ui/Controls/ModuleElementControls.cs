using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// 状态指示器：**颜色 + 形状双重编码**（09 §7，不只靠颜色）。
/// </summary>
/// <remarks>
/// 正常/Active = 实心点；Disabled/停流 = 空心点；无数据流/悬空/占位 = 三角；异常停机 = 叉。
/// </remarks>
public sealed class StateIndicator : LayerControlBase
{
    /// <summary>状态语义。</summary>
    public static readonly StyledProperty<StateVisual> StateProperty =
        AvaloniaProperty.Register<StateIndicator, StateVisual>(nameof(State));

    /// <summary>状态语义。</summary>
    public StateVisual State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    static StateIndicator() => AffectsRender<StateIndicator>(
        StateProperty,
        OkBrushProperty,
        IdleBrushProperty,
        WarnBrushProperty,
        ErrorBrushProperty);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var radius = Math.Min(bounds.Width, bounds.Height) / 2 - 1;
        var brush = BrushFor(State) ?? IdleBrush;

        switch (State)
        {
            case StateVisual.Active:
                context.DrawEllipse(brush, null, center, radius, radius);
                break;

            case StateVisual.Error:
                var pen = new Pen(brush, 1.5);
                context.DrawLine(pen, new Point(center.X - radius, center.Y - radius), new Point(center.X + radius, center.Y + radius));
                context.DrawLine(pen, new Point(center.X - radius, center.Y + radius), new Point(center.X + radius, center.Y - radius));
                break;

            case StateVisual.Warn:
            case StateVisual.Dangling:
            case StateVisual.Placeholder:
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(center.X, center.Y - radius), isFilled: true);
                    ctx.LineTo(new Point(center.X + radius, center.Y + radius));
                    ctx.LineTo(new Point(center.X - radius, center.Y + radius));
                    ctx.EndFigure(isClosed: true);
                }

                context.DrawGeometry(brush, null, triangle);
                break;

            default:
                context.DrawEllipse(null, new Pen(brush, 1.4), center, radius - 0.5, radius - 0.5);
                break;
        }
    }
}

/// <summary>
/// 曲线绘制（A3）：自量程折线——**数据优先**，不做固定刻度（09 §1）。
/// </summary>
/// <remarks>
/// M1 最小实现：只画折线（无坐标轴/网格/游标）。视觉细节属 Gate C。
/// </remarks>
public sealed class CurveControl : Control
{
    /// <summary>点序列（时间升序）。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> PointsProperty =
        AvaloniaProperty.Register<CurveControl, IReadOnlyList<double>?>(nameof(Points));

    /// <summary>窗口最小值（由视图模型算好）。</summary>
    public static readonly StyledProperty<double> MinProperty =
        AvaloniaProperty.Register<CurveControl, double>(nameof(Min));

    /// <summary>窗口最大值。</summary>
    public static readonly StyledProperty<double> MaxProperty =
        AvaloniaProperty.Register<CurveControl, double>(nameof(Max));

    /// <summary>线色。</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CurveControl, IBrush?>(nameof(Stroke));

    /// <summary>点序列。</summary>
    public IReadOnlyList<double>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>窗口最小值。</summary>
    public double Min
    {
        get => GetValue(MinProperty);
        set => SetValue(MinProperty, value);
    }

    /// <summary>窗口最大值。</summary>
    public double Max
    {
        get => GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    /// <summary>线色。</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    static CurveControl() => AffectsRender<CurveControl>(PointsProperty, MinProperty, MaxProperty, StrokeProperty);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Points is not { Count: > 1 } points)
        {
            return;
        }

        var bounds = Bounds;
        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var range = Max - Min;
        if (range <= 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var step = width / Math.Max(1, points.Count - 1);
            var started = false;

            for (var i = 0; i < points.Count; i++)
            {
                var value = points[i];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    started = false;
                    continue;
                }

                // 8 px 内边距：极值不贴边、不被裁掉
                var normalized = (value - Min) / range;
                var y = 4 + ((1 - Math.Clamp(normalized, 0, 1)) * (height - 8));
                var x = i * step;

                if (!started)
                {
                    ctx.BeginFigure(new Point(x, y), isFilled: false);
                    started = true;
                }
                else
                {
                    ctx.LineTo(new Point(x, y));
                }
            }

            if (started)
            {
                ctx.EndFigure(isClosed: false);
            }
        }

        // **绝对色**（兜底）：调用方总会给 `Stroke`（主题色）；这一支只在"忘了接线"时出现，刻意不跟随主题。
        context.DrawGeometry(null, new Pen(Stroke ?? Brushes.DodgerBlue, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}
