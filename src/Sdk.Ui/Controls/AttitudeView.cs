using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>三轴姿态的渲染物。</summary>
public enum AttitudeRenderStyle
{
    /// <summary>只画机体坐标轴。</summary>
    Axes = 0,

    /// <summary>方块（半透明面 + 棱）。</summary>
    Cube = 1,

    /// <summary>无人机（机身 + 四臂 + 桨盘 + 机头标记）。</summary>
    Drone = 2,
}

/// <summary>三轴姿态视图：roll / pitch / yaw → 三维姿态（世界网格 + 机体指向）。</summary>
/// <remarks>
/// <para>
/// 只画三个角度：不读显示源、不解析单位、不判断有没有数据——"失联后保持还是回到单位姿态"
/// 由模块发布的目标角度决定（见模块的失联归零设置）。
/// </para>
/// <para>
/// 世界（网格、相机）固定，机体绕自身原点旋转；屏幕投影是固定的简单透视，
/// 不依赖第三方 3D 引擎，也不需要额外的渲染循环（由框架每帧 <see cref="Refresh"/> 推进）。
/// </para>
/// </remarks>
public sealed class AttitudeView : Control, IModuleViewElement
{
    /// <summary>横滚角（度）。</summary>
    public static readonly StyledProperty<double> RollProperty =
        AvaloniaProperty.Register<AttitudeView, double>(nameof(Roll));

    /// <summary>俯仰角（度）。</summary>
    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<AttitudeView, double>(nameof(Pitch));

    /// <summary>偏航角（度）。</summary>
    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<AttitudeView, double>(nameof(Yaw));

    /// <summary>渲染物。</summary>
    public static readonly StyledProperty<AttitudeRenderStyle> RenderStyleProperty =
        AvaloniaProperty.Register<AttitudeView, AttitudeRenderStyle>(nameof(RenderStyle), AttitudeRenderStyle.Cube);

    /// <summary>缩放（1 = 默认大小）。</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<AttitudeView, double>(nameof(Zoom), 1);

    /// <summary>是否画地面网格。</summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<AttitudeView, bool>(nameof(ShowGrid), true);

    /// <summary>是否画轴标签。</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<AttitudeView, bool>(nameof(ShowLabels), true);

    /// <summary>是否平滑。</summary>
    public static readonly StyledProperty<bool> SmoothingEnabledProperty =
        AvaloniaProperty.Register<AttitudeView, bool>(nameof(SmoothingEnabled), true);

    /// <summary>平滑时间常数（毫秒）。</summary>
    public static readonly StyledProperty<double> SmoothingTimeMsProperty =
        AvaloniaProperty.Register<AttitudeView, double>(nameof(SmoothingTimeMs), 120);

    /// <summary>基准字号（轴标签由它派生）。</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<AttitudeView, double>(nameof(FontSize), 10);

    /// <summary>地面网格色。</summary>
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(GridBrush));

    /// <summary>X 轴色。</summary>
    public static readonly StyledProperty<IBrush?> AxisXBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(AxisXBrush));

    /// <summary>Y 轴色。</summary>
    public static readonly StyledProperty<IBrush?> AxisYBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(AxisYBrush));

    /// <summary>Z 轴色。</summary>
    public static readonly StyledProperty<IBrush?> AxisZBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(AxisZBrush));

    /// <summary>机体面色（半透明）。</summary>
    public static readonly StyledProperty<IBrush?> BodyBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(BodyBrush));

    /// <summary>机体棱线色。</summary>
    public static readonly StyledProperty<IBrush?> BodyEdgeBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(BodyEdgeBrush));

    /// <summary>轴标签色。</summary>
    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(TextBrush));

    /// <summary>相机到原点的距离（与模型单位同尺度；只影响透视强度）。</summary>
    private const double CameraDistance = 4;

    /// <summary>网格半宽（模型单位）。</summary>
    private const double GridExtent = 1.2;

    /// <summary>网格线间距（模型单位）。</summary>
    private const double GridStep = 0.3;

    private readonly AnimatedNumber _roll = new();
    private readonly AnimatedNumber _pitch = new();
    private readonly AnimatedNumber _yaw = new();

    static AttitudeView()
    {
        AffectsRender<AttitudeView>(
            RollProperty,
            PitchProperty,
            YawProperty,
            RenderStyleProperty,
            ZoomProperty,
            ShowGridProperty,
            ShowLabelsProperty,
            SmoothingEnabledProperty,
            SmoothingTimeMsProperty,
            FontSizeProperty,
            GridBrushProperty,
            AxisXBrushProperty,
            AxisYBrushProperty,
            AxisZBrushProperty,
            BodyBrushProperty,
            BodyEdgeBrushProperty,
            TextBrushProperty);
    }

    /// <summary>横滚角（度）。</summary>
    public double Roll
    {
        get => GetValue(RollProperty);
        set => SetValue(RollProperty, value);
    }

    /// <summary>俯仰角（度）。</summary>
    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    /// <summary>偏航角（度）。</summary>
    public double Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    /// <summary>渲染物。</summary>
    public AttitudeRenderStyle RenderStyle
    {
        get => GetValue(RenderStyleProperty);
        set => SetValue(RenderStyleProperty, value);
    }

    /// <summary>缩放。</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>是否画地面网格。</summary>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>是否画轴标签。</summary>
    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
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

    /// <summary>地面网格色。</summary>
    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>X 轴色。</summary>
    public IBrush? AxisXBrush
    {
        get => GetValue(AxisXBrushProperty);
        set => SetValue(AxisXBrushProperty, value);
    }

    /// <summary>Y 轴色。</summary>
    public IBrush? AxisYBrush
    {
        get => GetValue(AxisYBrushProperty);
        set => SetValue(AxisYBrushProperty, value);
    }

    /// <summary>Z 轴色。</summary>
    public IBrush? AxisZBrush
    {
        get => GetValue(AxisZBrushProperty);
        set => SetValue(AxisZBrushProperty, value);
    }

    /// <summary>机体面色。</summary>
    public IBrush? BodyBrush
    {
        get => GetValue(BodyBrushProperty);
        set => SetValue(BodyBrushProperty, value);
    }

    /// <summary>机体棱线色。</summary>
    public IBrush? BodyEdgeBrush
    {
        get => GetValue(BodyEdgeBrushProperty);
        set => SetValue(BodyEdgeBrushProperty, value);
    }

    /// <summary>轴标签色。</summary>
    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var smoothing = SmoothingEnabled;
        var timeConstant = SmoothingTimeMs;
        var changed = _roll.Advance(Roll, smoothing, timeConstant, 0.02);
        changed |= _pitch.Advance(Pitch, smoothing, timeConstant, 0.02);
        changed |= _yaw.Advance(Yaw, smoothing, timeConstant, 0.02);

        if (changed)
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
        if (width < 40 || height < 40)
        {
            return;
        }

        var fontSize = double.IsFinite(FontSize) && FontSize > 0 ? FontSize : 10;
        var zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, 0.3, 3) : 1;
        var camera = new Camera(
            width / 2,
            height / 2,
            Math.Min(width, height) * 0.30 * zoom,
            _roll.Value * Math.PI / 180,
            _pitch.Value * Math.PI / 180,
            _yaw.Value * Math.PI / 180);

        if (ShowGrid)
        {
            DrawGrid(context, camera);
        }

        DrawAxes(context, camera, fontSize);

        switch (RenderStyle)
        {
            case AttitudeRenderStyle.Cube:
                DrawCube(context, camera);
                break;

            case AttitudeRenderStyle.Drone:
                DrawDrone(context, camera);
                break;
        }
    }

    private void DrawGrid(DrawingContext context, in Camera camera)
    {
        if (GridBrush is not { } gridBrush)
        {
            return;
        }

        var pen = new Pen(gridBrush, 1);
        var lines = (int)Math.Round(GridExtent / GridStep);
        for (var index = -lines; index <= lines; index++)
        {
            var offset = index * GridStep;
            var from = camera.Project(offset, -1, -GridExtent);
            var to = camera.Project(offset, -1, GridExtent);
            context.DrawLine(pen, from, to);

            from = camera.Project(-GridExtent, -1, offset);
            to = camera.Project(GridExtent, -1, offset);
            context.DrawLine(pen, from, to);
        }
    }

    private void DrawAxes(DrawingContext context, in Camera camera, double fontSize)
    {
        var origin = camera.ProjectRotated(0, 0, 0);
        DrawAxis(context, camera, origin, 1, 0, 0, AxisXBrush, "X", fontSize);
        DrawAxis(context, camera, origin, 0, 1, 0, AxisYBrush, "Y", fontSize);
        DrawAxis(context, camera, origin, 0, 0, 1, AxisZBrush, "Z", fontSize);
    }

    private void DrawAxis(
        DrawingContext context,
        in Camera camera,
        Point origin,
        double x,
        double y,
        double z,
        IBrush? brush,
        string label,
        double fontSize)
    {
        if (brush is null)
        {
            return;
        }

        var tip = camera.ProjectRotated(x, y, z);
        context.DrawLine(new Pen(brush, 1.6), origin, tip);

        if (!ShowLabels || TextBrush is not { } textBrush)
        {
            return;
        }

        var text = SurfaceDrawing.Format(label, fontSize, textBrush);
        var anchor = camera.ProjectRotated(x * 1.18, y * 1.18, z * 1.18);
        SurfaceDrawing.DrawCentered(
            context,
            text,
            anchor.X,
            anchor.Y - (text.Height / 2),
            Bounds.Width);
    }

    private void DrawCube(DrawingContext context, in Camera camera)
    {
        if (BodyEdgeBrush is not { } edgeBrush)
        {
            return;
        }

        const double half = 0.5;
        Span<Point> points = stackalloc Point[8];
        Span<double> depth = stackalloc double[8];

        // 顶点顺序：0–3 为后面（-h），4–7 为前面（+h）；每层依次是 (-,-) (+,-) (+,+) (-,+)。
        ReadOnlySpan<double> xs = [-half, half, half, -half, -half, half, half, -half];
        ReadOnlySpan<double> ys = [-half, -half, half, half, -half, -half, half, half];
        ReadOnlySpan<double> zs = [-half, -half, -half, -half, half, half, half, half];

        for (var index = 0; index < 8; index++)
        {
            points[index] = camera.ProjectRotated(xs[index], ys[index], zs[index]);
            depth[index] = camera.DepthRotated(xs[index], ys[index], zs[index]);
        }

        ReadOnlySpan<int> faces =
        [
            0, 1, 2, 3,
            4, 5, 6, 7,
            0, 1, 5, 4,
            2, 3, 7, 6,
            0, 3, 7, 4,
            1, 2, 6, 5,
        ];

        Span<double> faceDepth = stackalloc double[6];
        Span<int> order = stackalloc int[6];
        for (var face = 0; face < 6; face++)
        {
            faceDepth[face] = (depth[faces[face * 4]] + depth[faces[(face * 4) + 1]]
                + depth[faces[(face * 4) + 2]] + depth[faces[(face * 4) + 3]]) / 4;
            order[face] = face;
        }

        for (var outer = 1; outer < 6; outer++)
        {
            var current = order[outer];
            var probe = outer - 1;
            while (probe >= 0 && faceDepth[order[probe]] > faceDepth[current])
            {
                order[probe + 1] = order[probe];
                probe--;
            }

            order[probe + 1] = current;
        }

        var edgePen = new Pen(edgeBrush, 1.4);
        for (var sorted = 0; sorted < 6; sorted++)
        {
            var face = order[sorted];
            var geometry = new StreamGeometry();
            using (var figure = geometry.Open())
            {
                figure.BeginFigure(points[faces[face * 4]], isFilled: true);
                figure.LineTo(points[faces[(face * 4) + 1]]);
                figure.LineTo(points[faces[(face * 4) + 2]]);
                figure.LineTo(points[faces[(face * 4) + 3]]);
                figure.EndFigure(true);
            }

            context.DrawGeometry(BodyBrush, edgePen, geometry);
        }
    }

    private void DrawDrone(DrawingContext context, in Camera camera)
    {
        if (BodyEdgeBrush is not { } edgeBrush)
        {
            return;
        }

        var pen = new Pen(edgeBrush, 1.4);
        var thinPen = new Pen(edgeBrush, 1);

        // 机身：一个小长方体（线框）。
        Span<Point> body = stackalloc Point[8];
        for (var index = 0; index < 8; index++)
        {
            var x = (index & 1) == 0 ? -0.16 : 0.16;
            var y = (index & 2) == 0 ? -0.07 : 0.07;
            var z = (index & 4) == 0 ? -0.22 : 0.22;
            body[index] = camera.ProjectRotated(x, y, z);
        }

        ReadOnlySpan<int> edges =
        [
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7,
        ];

        for (var index = 0; index < edges.Length; index += 2)
        {
            context.DrawLine(pen, body[edges[index]], body[edges[index + 1]]);
        }

        // 四臂 + 桨盘。
        Span<double> armX = [0.55, 0.55, -0.55, -0.55];
        Span<double> armZ = [0.55, -0.55, 0.55, -0.55];
        for (var arm = 0; arm < 4; arm++)
        {
            var attachX = armX[arm] > 0 ? 0.14 : -0.14;
            var attachZ = armZ[arm] > 0 ? 0.18 : -0.18;
            context.DrawLine(
                pen,
                camera.ProjectRotated(attachX, 0, attachZ),
                camera.ProjectRotated(armX[arm], 0, armZ[arm]));

            DrawRotor(context, camera, thinPen, armX[arm], armZ[arm]);
        }

        // 机头标记：沿 +Z 的一段加粗线。
        context.DrawLine(
            new Pen(edgeBrush, 2.4),
            camera.ProjectRotated(0, 0, 0.24),
            camera.ProjectRotated(0, 0, 0.5));
    }

    private static void DrawRotor(
        DrawingContext context,
        in Camera camera,
        IPen pen,
        double centerX,
        double centerZ)
    {
        const int segments = 18;
        const double rotorRadius = 0.17;
        var previous = camera.ProjectRotated(centerX + rotorRadius, 0, centerZ);
        for (var index = 1; index <= segments; index++)
        {
            var angle = index * 2 * Math.PI / segments;
            var point = camera.ProjectRotated(
                centerX + (rotorRadius * Math.Cos(angle)),
                0,
                centerZ + (rotorRadius * Math.Sin(angle)));
            context.DrawLine(pen, previous, point);
            previous = point;
        }
    }

    /// <summary>世界固定、机体旋转的透视相机（纯值类型：每帧不分配）。</summary>
    private readonly struct Camera
    {
        private readonly double _centerX;
        private readonly double _centerY;
        private readonly double _scale;
        private readonly double _rollCos;
        private readonly double _rollSin;
        private readonly double _pitchCos;
        private readonly double _pitchSin;
        private readonly double _yawCos;
        private readonly double _yawSin;

        public Camera(
            double centerX,
            double centerY,
            double scale,
            double roll,
            double pitch,
            double yaw)
        {
            _centerX = centerX;
            _centerY = centerY;
            _scale = scale;
            _rollCos = Math.Cos(roll);
            _rollSin = Math.Sin(roll);
            _pitchCos = Math.Cos(pitch);
            _pitchSin = Math.Sin(pitch);
            _yawCos = Math.Cos(yaw);
            _yawSin = Math.Sin(yaw);
        }

        /// <summary>世界坐标 → 屏幕（不旋转）。</summary>
        public Point Project(double x, double y, double z)
        {
            var factor = CameraDistance / (CameraDistance - z);
            if (!(factor > 0.05) || !double.IsFinite(factor))
            {
                factor = 0.05;
            }

            return new Point(_centerX + (x * _scale * factor), _centerY - (y * _scale * factor));
        }

        /// <summary>机体系坐标 → 屏幕（先按 roll→pitch→yaw 旋转，再投影）。</summary>
        public Point ProjectRotated(double x, double y, double z)
        {
            var (rx, ry, rz) = Rotate(x, y, z);
            return Project(rx, ry, rz);
        }

        /// <summary>机体系坐标旋转后的深度（越大越靠近观察者）。</summary>
        public double DepthRotated(double x, double y, double z)
        {
            var (_, _, rz) = Rotate(x, y, z);
            return rz;
        }

        private (double X, double Y, double Z) Rotate(double x, double y, double z)
        {
            // Rz(roll) → Rx(pitch) → Ry(yaw)：先绕机体前后轴滚转，再俯仰，最后偏航。
            var x1 = (x * _rollCos) - (y * _rollSin);
            var y1 = (x * _rollSin) + (y * _rollCos);
            var z1 = z;

            var y2 = (y1 * _pitchCos) - (z1 * _pitchSin);
            var z2 = (y1 * _pitchSin) + (z1 * _pitchCos);
            var x2 = x1;

            var x3 = (x2 * _yawCos) + (z2 * _yawSin);
            var z3 = (-x2 * _yawSin) + (z2 * _yawCos);
            return (x3, y2, z3);
        }
    }
}
