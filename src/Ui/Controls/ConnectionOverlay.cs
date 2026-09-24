using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **叠加信息层**【M1-16 / ADR-0039 D5】：在每个模块窗口内绘制"指向对端模块的连接线 + 流向 + 对端信息"。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么画在窗口内</b>：多窗口形态下不做跨窗口覆盖窗（ADR-0034/0037 结论保留）。
///     模块**拼接（接触）后**，发送端的线画到自身右边缘、接收端的线从自身左边缘画进来 —— 两条线在公共边处
///     **自然连成一条**，因此不需要任何跨窗口绘制能力，也不需要透明覆盖窗；</item>
///   <item><b>流向</b>：箭头表示数据流向 = 发送口朝外、接收口朝内（与标记上的箭头同一语义，ADR-0039 D3）；</item>
///   <item><b>未拼接</b>：线只画到本窗口边缘，并在端部标注对端 `实例.口名`（D-4 裁决）；</item>
///   <item>本层 `IsHitTestVisible=false`：不参与命中，点击穿透到内容/标记。</item>
/// </list>
/// </remarks>
public sealed class ConnectionOverlay : Control
{
    /// <summary>端口标记集合（布局信息 = 边缘 + 归一化偏移，与 <see cref="PortMarkerPanel"/> 同一规则）。</summary>
    public static readonly StyledProperty<IReadOnlyList<PortMarkerViewModel>?> PortsProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IReadOnlyList<PortMarkerViewModel>?>(nameof(Ports));

    /// <summary>是否绘制对端信息标签。</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<ConnectionOverlay, bool>(nameof(ShowLabels), defaultValue: true);

    /// <summary>连线画刷。</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(LineBrush));

    /// <summary>箭头画刷。</summary>
    public static readonly StyledProperty<IBrush?> ArrowBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(ArrowBrush));

    /// <summary>标签画刷。</summary>
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(LabelBrush));

    /// <summary>端口标记集合。</summary>
    public IReadOnlyList<PortMarkerViewModel>? Ports
    {
        get => GetValue(PortsProperty);
        set => SetValue(PortsProperty, value);
    }

    /// <summary>是否绘制对端信息标签。</summary>
    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>连线画刷。</summary>
    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>箭头画刷。</summary>
    public IBrush? ArrowBrush
    {
        get => GetValue(ArrowBrushProperty);
        set => SetValue(ArrowBrushProperty, value);
    }

    /// <summary>标签画刷。</summary>
    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    /// <summary>标记带宽（与 <see cref="PortMarkerPanel.BandThickness"/> 一致）。</summary>
    private const double Band = PortMarkerPanel.BandThickness;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Ports is not { Count: > 0 } ports)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < Band * 3 || height < Band * 3)
        {
            return;
        }

        var pen = new Pen(LineBrush ?? Brushes.SteelBlue, 1.2);

        // 标签用默认字型（本应用采用系统字体，09 §3 V4）
        var typeface = Typeface.Default;

        foreach (var port in ports)
        {
            if (!port.HasLink || port.PeerSide is not { } peerSide)
            {
                continue;
            }

            var start = Anchor(port, width, height);
            var end = BorderPoint(start, peerSide, width, height);

            context.DrawLine(pen, start, end);

            // 流向：发送口朝外（朝对端边缘）；接收口朝内（数据从对端进入本模块）
            var flow = port.IsSend ? peerSide : Opposite(peerSide);
            DrawArrow(context, end, flow);

            if (ShowLabels && port.PeerText is { Length: > 0 } text && LabelBrush is { } labelBrush)
            {
                DrawLabel(context, text, end, peerSide, typeface, labelBrush);
            }
        }
    }

    /// <summary>标记中心（与 <see cref="PortMarkerPanel"/> 的排布规则一致）。</summary>
    private static Point Anchor(PortMarkerViewModel port, double width, double height)
    {
        var offset = Math.Clamp(port.Offset, 0, 1);
        return port.Side switch
        {
            PortSide.Right => new Point(width - (Band / 2), height * offset),
            PortSide.Top => new Point(width * offset, Band / 2),
            PortSide.Bottom => new Point(width * offset, height - (Band / 2)),
            _ => new Point(Band / 2, height * offset),
        };
    }

    /// <summary>从标记指向该侧的窗口边界点。</summary>
    private static Point BorderPoint(Point from, PortSide side, double width, double height) => side switch
    {
        PortSide.Right => new Point(width, from.Y),
        PortSide.Top => new Point(from.X, 0),
        PortSide.Bottom => new Point(from.X, height),
        _ => new Point(0, from.Y),
    };

    private static PortSide Opposite(PortSide side) => side switch
    {
        PortSide.Left => PortSide.Right,
        PortSide.Right => PortSide.Left,
        PortSide.Top => PortSide.Bottom,
        _ => PortSide.Top,
    };

    /// <summary>在端部画一个 5 px 的流向箭头。</summary>
    private static void DrawArrow(DrawingContext context, Point tip, PortSide flow)
    {
        const double size = 5;
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            switch (flow)
            {
                case PortSide.Right:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X - size, tip.Y - size / 1.6));
                    sink.LineTo(new Point(tip.X - size, tip.Y + size / 1.6));
                    break;

                case PortSide.Left:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X + size, tip.Y - size / 1.6));
                    sink.LineTo(new Point(tip.X + size, tip.Y + size / 1.6));
                    break;

                case PortSide.Bottom:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X - size / 1.6, tip.Y - size));
                    sink.LineTo(new Point(tip.X + size / 1.6, tip.Y - size));
                    break;

                default:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X - size / 1.6, tip.Y + size));
                    sink.LineTo(new Point(tip.X + size / 1.6, tip.Y + size));
                    break;
            }

            sink.EndFigure(true);
        }
    }

    /// <summary>在连线端部内侧绘制对端信息（`实例.口名`）。</summary>
    private static void DrawLabel(
        DrawingContext context,
        string text,
        Point end,
        PortSide side,
        Typeface typeface,
        IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            9,
            brush);

        // 端部内收 4 px，避免压到窗口边框；左右两侧按文字宽度对齐
        var x = side switch
        {
            PortSide.Right => end.X - formatted.Width - 6,
            PortSide.Left => end.X + 6,
            _ => end.X + 4,
        };

        var y = side switch
        {
            PortSide.Top => end.Y + 4,
            PortSide.Bottom => end.Y - formatted.Height - 4,
            _ => end.Y - (formatted.Height / 2),
        };

        context.DrawText(formatted, new Point(x, y));
    }
}
