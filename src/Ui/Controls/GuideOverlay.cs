using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **窗内叠加层**：在模块窗口内部绘制
/// ① **虚线对齐参考线**（命中吸附时；线画在窗内即可——两窗口对齐后线在视觉上自然连成一条）；
/// ② **缩放时的边缘高亮**（2 px 主色，标出正在拖动的那条边）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>不绘制任何跨窗口内容：所有坐标都以**本窗口客户区左上角为原点**换算，
///     因此不需要透明覆盖窗（跨窗口画线是被否决的方案）；</item>
///   <item><b>坐标单位</b>：<see cref="GuideLine.Position"/> 与模型几何**同一单位 = 屏幕 DIP**
///     （模型一律 DIP，DIP → 物理像素的换算只在窗口边界一处，见 `ModuleWindowManager.ApplyGeometry`）。
///     本控件画在**本窗口客户区的 DIP** 空间里 ⇒ 只需减去客户区原点；而
///     <c>TopLevel.PointToScreen(0,0)</c> 给的是**物理像素** ⇒ **只把它 ÷ 缩放**换成 DIP。
///     <b>不能把"线 − 原点"这个差整体再除一次缩放</b>：那等于把线自己的坐标也缩了一遍
///     ⇒ 位置按 <c>(1/scale − 1)</c> 偏移（125% 屏上屏幕 y≈600 的线会画高 ≈120px），
///     且**越靠屏幕右/下越偏**（实测：中线对齐时那根线明显跑到上面去了）；</item>
///   <item>窗口边框不裁剪时线会画到窗口外（实测出现过横贯屏幕的虚线），
///     故 <c>ModuleWindow</c> 的 Frame 设 <c>ClipToBounds=True</c>。</item>
/// </list>
/// </remarks>
public sealed class GuideOverlay : Control
{
    /// <summary>对齐辅助线（**与模型几何同一单位：屏幕 DIP**；来自吸附引擎）。</summary>
    public static readonly StyledProperty<IReadOnlyList<GuideLine>?> GuidesProperty =
        AvaloniaProperty.Register<GuideOverlay, IReadOnlyList<GuideLine>?>(nameof(Guides));

    /// <summary>正在缩放的边（位标志：1 左 · 2 右 · 4 上 · 8 下；0 = 不在缩放）。</summary>
    public static readonly StyledProperty<int> ResizeEdgesProperty =
        AvaloniaProperty.Register<GuideOverlay, int>(nameof(ResizeEdges));

    /// <summary>线条颜色（虚线辅助线与边缘高亮共用；缺省用主色）。</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<GuideOverlay, IBrush?>(nameof(LineBrush));

    /// <summary>对齐辅助线（屏幕 DIP，与模型几何同单位）。</summary>
    public IReadOnlyList<GuideLine>? Guides
    {
        get => GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
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
        ResizeEdgesProperty,
        LineBrushProperty);

    /// <summary>虚线辅助线的线宽（DIP）。</summary>
    public const double GuideThickness = 1;

    /// <summary>
    /// **边框内缩容差**（DIP）：叠加层被窗口边框内缩（`Frame` 是 `BorderThickness=1` 的边框），
    /// 于是"贴边"的对齐线在叠加层坐标系里会落在可绘制范围**外**约一个边框宽 —— 仍然必须画出来。
    /// </summary>
    public const double EdgeTolerance = 3;

    /// <summary>
    /// 该位置的线**是否应当画在本控件里**：落在可绘制范围内，或只是"贴边超出"一点（<see cref="EdgeTolerance"/>）。
    /// </summary>
    /// <param name="position">线在控件坐标系里的位置（DIP）。</param>
    /// <param name="extent">该方向的可用范围（控件宽或高）。</param>
    /// <param name="tolerance">允许超出的量（DIP）。</param>
    /// <returns>是否应当绘制。</returns>
    /// <remarks>
    /// 【用户第十二轮第六轮反馈："底部和右边还是没有"】上一版只改了"收进半个线宽"，**没改这条判断** ——
    /// 而底部/右边的线在叠加层坐标里是 <c>范围 + 2</c>（客户区底边比叠加层底边低 2 DIP），
    /// 被 <c>position &lt;= extent</c> 直接排除 ⇒ **根本不画**。这正是"上/左有、下/右没有"的原因。
    /// 判据必须容忍"贴边超出"；真正该排除的是**离得远**的线（它不属于本窗口，由对方窗口自己画）。
    /// </remarks>
    public static bool IsDrawable(double position, double extent, double tolerance = EdgeTolerance) =>
        position >= -tolerance && position <= extent + tolerance;

    /// <summary>
    /// 把**贴着边界**的辅助线位置收进可绘制范围：返回落在 <c>[线宽/2, 范围 − 线宽/2]</c> 内的坐标。
    /// </summary>
    /// <param name="position">线在控件坐标系里的位置（已换算为 DIP）。</param>
    /// <param name="extent">该方向的可用范围（控件宽或高）。</param>
    /// <param name="thickness">线宽（DIP）。</param>
    /// <returns>实际用于绘制的坐标（位移 ≤ 半个线宽，肉眼无感）。</returns>
    /// <remarks>
    /// 贴边的线有一半线宽落在裁剪区外（叠加层 `ClipToBounds`），叠加虚线样式后**不可见**；收进半个线宽即可。
    /// 两个窗口各自贴着自己的边画线时收进量相同 ⇒ 仍在同一条直线上，视觉上依旧连成一条。
    /// </remarks>
    public static double InsetForDrawing(double position, double extent, double thickness = GuideThickness)
    {
        var half = thickness / 2;
        return extent <= thickness ? position : Math.Clamp(position, half, extent - half);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // **绝对色**（兜底）：调用方（模块窗口）总会把 `LineBrush` 设成主题色；
        // 这一支只在"忘了设色"时出现 —— 它刻意**不跟随主题**：兜底若随主题，
        // 反而会把"调用方没接线"这件事藏起来（屏幕上看着一切正常）。
        var brush = LineBrush ?? Brushes.DodgerBlue;
        var bounds = Bounds;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        // 客户区原点：`PointToScreen` 给**物理像素** ⇒ ÷ 缩放换成**屏幕 DIP**（与 Guides 同单位）。
        // 换算只有这一步：线自己的坐标已经是 DIP，**不再参与任何缩放**。
        var originX = 0.0;
        var originY = 0.0;

        if (TopLevel.GetTopLevel(this) is { } host)
        {
            var scale = host.RenderScaling <= 0 ? 1.0 : host.RenderScaling;
            var clientOrigin = host.PointToScreen(new Point(0, 0));
            originX = clientOrigin.X / scale;
            originY = clientOrigin.Y / scale;
        }

        // ① 虚线对齐参考线
        //    · 只画"属于本窗口"的线：贴边线允许超出可绘制范围一个边框内缩量（见 IsDrawable），
        //      离得远的线不画（那是对方窗口的事）；
        //    · 贴边时收进半个线宽，否则一半线宽被裁掉、虚线几乎不可见（见 InsetForDrawing）。
        if (Guides is { Count: > 0 } guides)
        {
            var pen = new Pen(brush, GuideThickness, new DashStyle([4, 4], 0));

            foreach (var guide in guides)
            {
                if (guide.IsVertical)
                {
                    var x = guide.Position - originX;
                    if (IsDrawable(x, bounds.Width))
                    {
                        var drawX = InsetForDrawing(x, bounds.Width);
                        context.DrawLine(pen, new Point(drawX, 0), new Point(drawX, bounds.Height));
                    }
                }
                else
                {
                    var y = guide.Position - originY;
                    if (IsDrawable(y, bounds.Height))
                    {
                        var drawY = InsetForDrawing(y, bounds.Height);
                        context.DrawLine(pen, new Point(0, drawY), new Point(bounds.Width, drawY));
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
