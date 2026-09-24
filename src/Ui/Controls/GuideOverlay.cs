using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **窗内叠加层**（ADR-0037 / 用户裁决）：在模块窗口内部绘制
/// ① **虚线对齐参考线**（命中吸附时；线画在窗内即可——两窗口对齐后线在视觉上自然连成一条）；
/// ② **缩放时的边缘高亮**（2 px 主色，标出正在拖动的那条边）。
/// </summary>
/// <remarks>
/// 不绘制任何跨窗口内容：所有坐标都以**本窗口左上角为原点**换算，
/// 因此不需要透明覆盖窗（跨窗口画线是 ADR-0034 明确推迟的方案）。
/// </remarks>
public sealed class GuideOverlay : Control
{
    /// <summary>对齐辅助线（**屏幕坐标**，来自吸附引擎）。</summary>
    public static readonly StyledProperty<IReadOnlyList<GuideLine>?> GuidesProperty =
        AvaloniaProperty.Register<GuideOverlay, IReadOnlyList<GuideLine>?>(nameof(Guides));

    /// <summary>本模块窗口左上角的屏幕坐标 X（把辅助线换算到窗内坐标）。</summary>
    public static readonly StyledProperty<double> OriginXProperty =
        AvaloniaProperty.Register<GuideOverlay, double>(nameof(OriginX));

    /// <summary>本模块窗口左上角的屏幕坐标 Y。</summary>
    public static readonly StyledProperty<double> OriginYProperty =
        AvaloniaProperty.Register<GuideOverlay, double>(nameof(OriginY));

    /// <summary>正在缩放的边（位标志：1 左 · 2 右 · 4 上 · 8 下；0 = 不在缩放）。</summary>
    public static readonly StyledProperty<int> ResizeEdgesProperty =
        AvaloniaProperty.Register<GuideOverlay, int>(nameof(ResizeEdges));

    /// <summary>线条颜色（虚线辅助线与边缘高亮共用；缺省用主色）。</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<GuideOverlay, IBrush?>(nameof(LineBrush));

    /// <summary>对齐辅助线（屏幕坐标）。</summary>
    public IReadOnlyList<GuideLine>? Guides
    {
        get => GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    /// <summary>窗口左上角屏幕坐标 X。</summary>
    public double OriginX
    {
        get => GetValue(OriginXProperty);
        set => SetValue(OriginXProperty, value);
    }

    /// <summary>窗口左上角屏幕坐标 Y。</summary>
    public double OriginY
    {
        get => GetValue(OriginYProperty);
        set => SetValue(OriginYProperty, value);
    }

    /// <summary>正在缩放的边（位标志）。</summary>
    public int ResizeEdges
    {
        get => GetValue(ResizeEdgesProperty);
        set => SetValue(ResizeEdgesProperty, value);
    }

    /// <summary>线色。</summary>
    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    static GuideOverlay() => AffectsRender<GuideOverlay>(
        GuidesProperty,
        OriginXProperty,
        OriginYProperty,
        ResizeEdgesProperty,
        LineBrushProperty);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var brush = LineBrush ?? Brushes.DodgerBlue;
        var bounds = Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        // ① 虚线对齐参考线（窗内坐标 = 屏幕坐标 - 窗口原点）
        if (Guides is { Count: > 0 } guides)
        {
            var pen = new Pen(brush, 1, new DashStyle([4, 4], 0));

            foreach (var guide in guides)
            {
                if (guide.IsVertical)
                {
                    var x = guide.Position - OriginX;
                    if (x >= 0 && x <= bounds.Width)
                    {
                        context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
                    }
                }
                else
                {
                    var y = guide.Position - OriginY;
                    if (y >= 0 && y <= bounds.Height)
                    {
                        context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
                    }
                }
            }
        }

        // ② 缩放边缘高亮（2 px 实线，标出正在拖动的边）
        if (ResizeEdges != 0)
        {
            var pen = new Pen(brush, 2);
            const int left = 1, right = 2, top = 4, bottom = 8;

            if ((ResizeEdges & left) != 0)
            {
                context.DrawLine(pen, new Point(1, 0), new Point(1, bounds.Height));
            }

            if ((ResizeEdges & right) != 0)
            {
                context.DrawLine(pen, new Point(bounds.Width - 1, 0), new Point(bounds.Width - 1, bounds.Height));
            }

            if ((ResizeEdges & top) != 0)
            {
                context.DrawLine(pen, new Point(0, 1), new Point(bounds.Width, 1));
            }

            if ((ResizeEdges & bottom) != 0)
            {
                context.DrawLine(pen, new Point(0, bounds.Height - 1), new Point(bounds.Width, bounds.Height - 1));
            }
        }
    }
}
