using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Text;

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
/// 世界（网格、机体轴、地面参考轴）固定，机体绕自身原点旋转；相机可在世界坐标系里拖动/预设，
/// 与机体姿态相互独立。视角、按钮与七视角面板都属于控件自身，不读模块设置、不发事件。
/// 投影不依赖第三方 3D 引擎，也不需要额外的渲染循环（由框架每帧 <see cref="Refresh"/> 推进）。
/// </para>
/// </remarks>
public sealed class AttitudeView : Control, IModuleViewElement, IModulePreviewBoundsTarget, ICustomHitTest
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

    /// <summary>视角面板/按钮的面底色。</summary>
    public static readonly StyledProperty<IBrush?> ViewSurfaceBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(ViewSurfaceBrush));

    /// <summary>目标面高亮、悬停/选中底与按钮激活边框。</summary>
    public static readonly StyledProperty<IBrush?> ViewAccentBrushProperty =
        AvaloniaProperty.Register<AttitudeView, IBrush?>(nameof(ViewAccentBrush));

    /// <summary>网格半宽（模型单位）。</summary>
    private const double GridExtent = 1.2;

    /// <summary>网格线间距（模型单位）。</summary>
    private const double GridStep = 0.3;

    /// <summary>机体轴从机体内部开始的可见起点（立方体半边长）。</summary>
    private const double BodyAxisStart = 0.5;

    /// <summary>左下参考轴的屏幕半径（DIP）。</summary>
    private const double GroundAxisRadius = 20;

    /// <summary>左下参考轴距控件边缘的留白（DIP）。</summary>
    private const double GroundAxisInset = 10;

    /// <summary>轴对视时改用圆点/圆环的二维长度阈值（DIP）。</summary>
    private const double GroundAxisDotThreshold = 3;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeAll);

    private readonly AnimatedNumber _roll = new();
    private readonly AnimatedNumber _pitch = new();
    private readonly AnimatedNumber _yaw = new();

    private double _viewYawDegrees;
    private double _viewPitchDegrees;
    private AttitudeViewPreset? _viewPreset;
    private bool _selectorOpen;
    private bool _hoverButton;
    private AttitudeViewPreset? _hoverPreset;
    private bool _pressedButton;
    private AttitudeViewPreset? _pressedPreset;
    private bool _orbiting;
    private Point _dragStart;
    private double _dragStartYaw;
    private double _dragStartPitch;

    /// <summary>构造姿态视图：初始为等轴测，并接收键盘焦点以支持空格开合面板。</summary>
    public AttitudeView()
    {
        ClipToBounds = true;
        Focusable = true;
        IsTabStop = false;
        FocusAdorner = null;

        var (yaw, pitch) = AttitudeViewPresets.Angles(AttitudeViewPreset.Isometric);
        SetView(yaw, pitch, AttitudeViewPreset.Isometric);
        LostFocus += OnLostFocus;
    }

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
            TextBrushProperty,
            ViewSurfaceBrushProperty,
            ViewAccentBrushProperty);
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

    /// <summary>视角面板/按钮的面底色。</summary>
    public IBrush? ViewSurfaceBrush
    {
        get => GetValue(ViewSurfaceBrushProperty);
        set => SetValue(ViewSurfaceBrushProperty, value);
    }

    /// <summary>目标面高亮、悬停/选中底与按钮激活边框。</summary>
    public IBrush? ViewAccentBrush
    {
        get => GetValue(ViewAccentBrushProperty);
        set => SetValue(ViewAccentBrushProperty, value);
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

        var fontSize = ResolveFontSize();
        var zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, 0.3, 3) : 1;
        var radians = Math.PI / 180;
        var camera = new AttitudeCamera(
            width / 2,
            height / 2,
            Math.Min(width, height) * 0.30 * zoom,
            _viewYawDegrees * radians,
            _viewPitchDegrees * radians);
        var body = new AttitudeBodyRotation(
            _roll.Value * radians,
            _pitch.Value * radians,
            _yaw.Value * radians);

        if (ShowGrid)
        {
            DrawGrid(context, camera);
        }

        // 机体轴与机体共用画家序：轴向相机的前半段画在机体之后，背离相机的画在之前；
        // 机体内部那一段不画（被机体挡住），否则整根轴会像贴在面上。
        if (RenderStyle == AttitudeRenderStyle.Axes)
        {
            DrawBodyAxisLines(context, camera, body, frontPass: null);
        }
        else
        {
            DrawBodyAxisLines(context, camera, body, frontPass: false);
        }

        switch (RenderStyle)
        {
            case AttitudeRenderStyle.Cube:
                DrawCube(context, camera, body);
                break;

            case AttitudeRenderStyle.Drone:
                DrawDrone(context, camera, body);
                break;
        }

        if (RenderStyle != AttitudeRenderStyle.Axes)
        {
            DrawBodyAxisLines(context, camera, body, frontPass: true);
        }

        DrawBodyAxisLabels(context, camera, body, fontSize);
        DrawGroundAxes(context, camera, fontSize);

        var layout = CreateCubeLayout(width, height, fontSize);
        DrawViewButton(context, layout, fontSize);
        if (_selectorOpen)
        {
            DrawViewSelector(context, layout, fontSize);
        }
    }

    /// <summary>当前字号（非有限/非正时回默认）。</summary>
    private double ResolveFontSize() => double.IsFinite(FontSize) && FontSize > 0 ? FontSize : 10;

    /// <summary>按控件尺寸与当前字体构造按钮/面板布局。</summary>
    private AttitudeViewCubeLayout CreateCubeLayout(double width, double height, double fontSize)
    {
        var sample = SurfaceDrawing.Format("X", fontSize, TextBrush ?? Brushes.Transparent);
        var footerHeight = Math.Ceiling(sample.Height) + 2;
        return AttitudeViewCubeLayout.Create(new Size(width, height), footerHeight);
    }

    private void DrawGrid(DrawingContext context, in AttitudeCamera camera)
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

    /// <summary>
    /// 画机体轴的外段：按轴是否朝向相机拆到机体前/后两个图层；机体内部段不画。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="camera">相机。</param>
    /// <param name="body">机体旋转。</param>
    /// <param name="frontPass">true = 朝向相机的外段，false = 背离相机的外段，null = 全部（坐标轴模式）。</param>
    private void DrawBodyAxisLines(
        DrawingContext context,
        in AttitudeCamera camera,
        in AttitudeBodyRotation body,
        bool? frontPass)
    {
        DrawBodyAxisLine(context, camera, body, 1, 0, 0, AxisXBrush, frontPass);
        DrawBodyAxisLine(context, camera, body, 0, 1, 0, AxisYBrush, frontPass);
        DrawBodyAxisLine(context, camera, body, 0, 0, 1, AxisZBrush, frontPass);
    }

    private void DrawBodyAxisLine(
        DrawingContext context,
        in AttitudeCamera camera,
        in AttitudeBodyRotation body,
        double x,
        double y,
        double z,
        IBrush? brush,
        bool? frontPass)
    {
        if (brush is null)
        {
            return;
        }

        if (frontPass is { } requested)
        {
            // 机体轴已经随机体旋转；深度必须用旋转后的轴，不能用世界轴。
            var (axisX, axisY, axisZ) = body.Rotate(x, y, z);
            var isFront = camera.Depth(axisX, axisY, axisZ) >= 0;
            if (isFront != requested)
            {
                return;
            }
        }

        var start = RenderStyle == AttitudeRenderStyle.Axes ? 0 : BodyAxisStart;
        var from = camera.ProjectBody(body, x * start, y * start, z * start);
        var to = camera.ProjectBody(body, x, y, z);
        context.DrawLine(new Pen(brush, 1.6), from, to);
    }

    /// <summary>机体轴标签始终画在机体之后（标签是注释，不参与深度遮挡）。</summary>
    private void DrawBodyAxisLabels(
        DrawingContext context,
        in AttitudeCamera camera,
        in AttitudeBodyRotation body,
        double fontSize)
    {
        DrawBodyAxisLabel(context, camera, body, 1, 0, 0, AxisXBrush, "X", fontSize);
        DrawBodyAxisLabel(context, camera, body, 0, 1, 0, AxisYBrush, "Y", fontSize);
        DrawBodyAxisLabel(context, camera, body, 0, 0, 1, AxisZBrush, "Z", fontSize);
    }

    private void DrawBodyAxisLabel(
        DrawingContext context,
        in AttitudeCamera camera,
        in AttitudeBodyRotation body,
        double x,
        double y,
        double z,
        IBrush? brush,
        string label,
        double fontSize)
    {
        if (brush is null || !ShowLabels || TextBrush is not { } textBrush)
        {
            return;
        }

        var text = SurfaceDrawing.Format(label, fontSize, textBrush);
        var anchor = camera.ProjectBody(body, x * 1.18, y * 1.18, z * 1.18);
        SurfaceDrawing.DrawCentered(
            context,
            text,
            anchor.X,
            anchor.Y - (text.Height / 2),
            Bounds.Width);
    }

    private void DrawCube(
        DrawingContext context,
        in AttitudeCamera camera,
        in AttitudeBodyRotation body)
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
            points[index] = camera.ProjectBody(
                body,
                xs[index],
                ys[index],
                zs[index],
                out depth[index]);
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

    private void DrawDrone(
        DrawingContext context,
        in AttitudeCamera camera,
        in AttitudeBodyRotation rotation)
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
            body[index] = camera.ProjectBody(rotation, x, y, z);
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
                camera.ProjectBody(rotation, attachX, 0, attachZ),
                camera.ProjectBody(rotation, armX[arm], 0, armZ[arm]));

            DrawRotor(context, camera, rotation, thinPen, armX[arm], armZ[arm]);
        }

        // 机头标记：沿 +Z 的一段加粗线。
        context.DrawLine(
            new Pen(edgeBrush, 2.4),
            camera.ProjectBody(rotation, 0, 0, 0.24),
            camera.ProjectBody(rotation, 0, 0, 0.5));
    }

    private static void DrawRotor(
        DrawingContext context,
        in AttitudeCamera camera,
        in AttitudeBodyRotation rotation,
        IPen pen,
        double centerX,
        double centerZ)
    {
        const int segments = 18;
        const double rotorRadius = 0.17;
        var previous = camera.ProjectBody(rotation, centerX + rotorRadius, 0, centerZ);
        for (var index = 1; index <= segments; index++)
        {
            var angle = index * 2 * Math.PI / segments;
            var point = camera.ProjectBody(
                rotation,
                centerX + (rotorRadius * Math.Cos(angle)),
                0,
                centerZ + (rotorRadius * Math.Sin(angle)));
            context.DrawLine(pen, previous, point);
            previous = point;
        }
    }

    /// <summary>左下角地面坐标系参考轴（固定屏幕尺寸，只随视角旋转）。</summary>
    private void DrawGroundAxes(DrawingContext context, in AttitudeCamera camera, double fontSize)
    {
        var center = new Point(
            GroundAxisInset + GroundAxisRadius,
            Bounds.Height - GroundAxisInset - GroundAxisRadius);

        Span<double> depths = stackalloc double[3];
        depths[0] = camera.Depth(1, 0, 0);
        depths[1] = camera.Depth(0, 1, 0);
        depths[2] = camera.Depth(0, 0, 1);

        Span<int> order = stackalloc int[3];
        order[0] = 0;
        order[1] = 1;
        order[2] = 2;
        for (var outer = 1; outer < order.Length; outer++)
        {
            var current = order[outer];
            var probe = outer - 1;
            while (probe >= 0 && depths[order[probe]] > depths[current])
            {
                order[probe + 1] = order[probe];
                probe--;
            }

            order[probe + 1] = current;
        }

        for (var index = 0; index < order.Length; index++)
        {
            switch (order[index])
            {
                case 0:
                    DrawGroundAxis(context, camera, center, 1, 0, 0, AxisXBrush, "X", fontSize);
                    break;

                case 1:
                    DrawGroundAxis(context, camera, center, 0, 1, 0, AxisYBrush, "Y", fontSize);
                    break;

                default:
                    DrawGroundAxis(context, camera, center, 0, 0, 1, AxisZBrush, "Z", fontSize);
                    break;
            }
        }

        if (TextBrush is { } originBrush)
        {
            context.DrawEllipse(originBrush, null, center, 1.2, 1.2);
        }
    }

    /// <summary>画一根地面参考轴；轴对视时退化为圆点/圆环。</summary>
    private void DrawGroundAxis(
        DrawingContext context,
        in AttitudeCamera camera,
        Point center,
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

        var (offsetX, offsetY) = camera.ProjectDirection(x, y, z, GroundAxisRadius);
        var length = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
        var point = new Point(center.X + offsetX, center.Y + offsetY);

        if (length < GroundAxisDotThreshold)
        {
            var depth = camera.Depth(x, y, z);
            if (depth >= 0)
            {
                context.DrawEllipse(brush, null, center, 2.4, 2.4);
            }
            else
            {
                context.DrawEllipse(null, new Pen(brush, 1.2), center, 2.4, 2.4);
            }
        }
        else
        {
            context.DrawLine(new Pen(brush, 1.6), center, point);
        }

        if (!ShowLabels || TextBrush is not { } textBrush)
        {
            return;
        }

        var text = SurfaceDrawing.Format(label, fontSize, textBrush);
        var anchor = length < GroundAxisDotThreshold
            ? new Point(center.X + 9, center.Y - 9)
            : new Point(center.X + (offsetX * 1.22), center.Y + (offsetY * 1.22));
        var left = Math.Clamp(anchor.X - (text.Width / 2), 0, Math.Max(0, Bounds.Width - text.Width));
        var top = Math.Clamp(anchor.Y - (text.Height / 2), 0, Math.Max(0, Bounds.Height - text.Height));
        context.DrawText(text, new Point(left, top));
    }

    /// <summary>视角按钮：常态 / 悬停 / 打开（按下共用打开态）。</summary>
    private void DrawViewButton(
        DrawingContext context,
        in AttitudeViewCubeLayout layout,
        double fontSize)
    {
        if (!layout.CanShow || TextBrush is not { } textBrush)
        {
            return;
        }

        var accent = ViewAccentBrush ?? textBrush;
        var active = _selectorOpen || _hoverButton || _pressedButton;
        if (active)
        {
            using (context.PushOpacity(_selectorOpen ? 0.18 : (_pressedButton ? 0.16 : 0.10)))
            {
                context.DrawRectangle(accent, null, layout.Button, 3, 3);
            }
        }

        using (context.PushOpacity(active ? 0.9 : 0.35))
        {
            context.DrawRectangle(null, new Pen(accent, 1), layout.Button, 3, 3);
        }

        var icon = new Rect(
            layout.Button.X + 4,
            layout.Button.Y + 4,
            layout.Button.Width - 8,
            layout.Button.Height - 8);
        AttitudeViewCubeRenderer.DrawIcon(context, icon, null, textBrush, accent);

        if (!_hoverButton || _selectorOpen)
        {
            return;
        }

        if (ViewSurfaceBrush is not { } surface)
        {
            return;
        }

        var maxWidth = Math.Max(40, Bounds.Width - layout.Button.Right - 12);
        var hint = SurfaceDrawing.Format(Tr("attitude.view.button"), fontSize, textBrush, maxWidth);
        var width = hint.Width + 8;
        var height = hint.Height + 4;
        var left = layout.Button.Right + 4;
        if (left + width > Bounds.Width - 4)
        {
            left = Math.Max(4, Bounds.Width - 4 - width);
        }

        var top = layout.Button.Y + ((layout.Button.Height - height) / 2);
        var bubble = new Rect(left, top, width, height);
        context.DrawRectangle(surface, null, bubble, 2, 2);
        using (context.PushOpacity(0.35))
        {
            context.DrawRectangle(null, new Pen(textBrush, 1), bubble, 2, 2);
        }

        context.DrawText(hint, new Point(left + 4, top + 2));
    }

    /// <summary>七视角面板：格子 + 名称行；格子不画白底，只画悬停/选中底。</summary>
    private void DrawViewSelector(
        DrawingContext context,
        in AttitudeViewCubeLayout layout,
        double fontSize)
    {
        if (!layout.CanShow || TextBrush is not { } textBrush || ViewSurfaceBrush is not { } surface)
        {
            return;
        }

        var accent = ViewAccentBrush ?? textBrush;
        context.DrawRectangle(surface, null, layout.Panel, 3, 3);
        using (context.PushOpacity(0.28))
        {
            context.DrawRectangle(null, new Pen(textBrush, 1), layout.Panel, 3, 3);
        }

        foreach (var preset in AttitudeViewPresets.All)
        {
            var tile = layout.TileRect(preset);
            var selected = _viewPreset == preset;
            var hovered = _hoverPreset == preset;
            var pressed = _pressedPreset == preset;
            if (selected || hovered || pressed)
            {
                using (context.PushOpacity(selected ? 0.14 : (pressed ? 0.20 : 0.08)))
                {
                    context.DrawRectangle(accent, null, tile, 3, 3);
                }
            }

            if (selected)
            {
                using (context.PushOpacity(0.55))
                {
                    context.DrawRectangle(null, new Pen(accent, 1), tile, 3, 3);
                }
            }

            AttitudeViewCubeRenderer.DrawIcon(context, tile, preset, textBrush, accent);
        }

        var current = _hoverPreset ?? _viewPreset;
        var name = current is { } value
            ? Tr(AttitudeViewPresets.TextKey(value))
            : Tr("attitude.view.custom");
        var footer = SurfaceDrawing.Format(
            name,
            fontSize,
            textBrush,
            Math.Max(1, layout.Footer.Width - 4));
        var top = layout.Footer.Y + ((layout.Footer.Height - footer.Height) / 2);
        context.DrawText(footer, new Point(layout.Footer.X + 2, top));
    }

    /// <summary>开合视角面板；尺寸放不下时不动作。</summary>
    private bool ToggleSelector()
    {
        if (_selectorOpen)
        {
            CloseSelector();
            return true;
        }

        var layout = CreateCubeLayout(Bounds.Width, Bounds.Height, ResolveFontSize());
        if (!layout.CanShow)
        {
            return false;
        }

        _selectorOpen = true;
        _hoverPreset = null;
        InvalidateVisual();
        return true;
    }

    /// <summary>关闭视角面板（不改变当前视角）。</summary>
    private void CloseSelector()
    {
        if (!_selectorOpen)
        {
            return;
        }

        _selectorOpen = false;
        _hoverPreset = null;
        _pressedPreset = null;
        Cursor = null;
        InvalidateVisual();
    }

    /// <summary>唯一的视角写入点：拖动、预设、初始化都走这里。</summary>
    private void SetView(double yawDegrees, double pitchDegrees, AttitudeViewPreset? preset)
    {
        var yaw = AttitudeViewMath.NormalizeYaw(yawDegrees);
        var pitch = AttitudeViewMath.ClampPitch(pitchDegrees);
        if (yaw == _viewYawDegrees && pitch == _viewPitchDegrees && preset == _viewPreset)
        {
            return;
        }

        _viewYawDegrees = yaw;
        _viewPitchDegrees = pitch;
        _viewPreset = preset;
        InvalidateVisual();
    }

    /// <summary>按预设视角写入相机。</summary>
    /// <param name="preset">预设。</param>
    internal void ApplyPreset(AttitudeViewPreset preset)
    {
        var (yaw, pitch) = AttitudeViewPresets.Angles(preset);
        SetView(yaw, pitch, preset);
    }

    /// <summary>当前视角角度（度；测试与截图用）。</summary>
    internal (double Yaw, double Pitch) ViewAngles => (_viewYawDegrees, _viewPitchDegrees);

    /// <summary>当前预设（拖动后为空；测试与截图用）。</summary>
    internal AttitudeViewPreset? CurrentViewPreset => _viewPreset;

    /// <summary>面板是否打开（测试与截图用）。</summary>
    internal bool IsViewSelectorOpen => _selectorOpen;

    /// <summary>测试/截图入口：打开面板（不改变焦点）。</summary>
    internal void OpenViewSelector()
    {
        if (_selectorOpen)
        {
            return;
        }

        var layout = CreateCubeLayout(Bounds.Width, Bounds.Height, ResolveFontSize());
        if (!layout.CanShow)
        {
            return;
        }

        _selectorOpen = true;
        InvalidateVisual();
    }

    private void UpdateHover(AttitudeViewHit hit)
    {
        var hoverButton = hit.Kind == AttitudeViewHitKind.Button;
        var hoverPreset = hit.Kind == AttitudeViewHitKind.Tile ? hit.Preset : null;
        if (hoverButton == _hoverButton && hoverPreset == _hoverPreset)
        {
            return;
        }

        _hoverButton = hoverButton;
        _hoverPreset = hoverPreset;
        Cursor = hoverButton || hoverPreset is not null ? HandCursor : null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ = Focus();
        var point = e.GetPosition(this);
        var layout = CreateCubeLayout(Bounds.Width, Bounds.Height, ResolveFontSize());
        var hit = layout.HitTest(point, _selectorOpen);

        if (_selectorOpen)
        {
            switch (hit.Kind)
            {
                case AttitudeViewHitKind.Tile:
                    _pressedPreset = hit.Preset;
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                    return;

                case AttitudeViewHitKind.Button:
                    _pressedButton = true;
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                    return;

                case AttitudeViewHitKind.Panel:
                    e.Handled = true;
                    return;

                default:
                    // 点面板外：先收起，再继续走拖动；否则第一次拖动会“只关面板、不转视角”。
                    CloseSelector();
                    break;
            }
        }

        if (hit.Kind == AttitudeViewHitKind.Button)
        {
            _pressedButton = true;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _orbiting = true;
        _dragStart = point;
        _dragStartYaw = _viewYawDegrees;
        _dragStartPitch = _viewPitchDegrees;
        Cursor = DragCursor;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_orbiting)
        {
            var point = e.GetPosition(this);
            var (yaw, pitch) = AttitudeViewMath.Orbit(
                _dragStartYaw,
                _dragStartPitch,
                point.X - _dragStart.X,
                point.Y - _dragStart.Y);
            SetView(yaw, pitch, null);
            e.Handled = true;
            return;
        }

        var layout = CreateCubeLayout(Bounds.Width, Bounds.Height, ResolveFontSize());
        UpdateHover(layout.HitTest(e.GetPosition(this), _selectorOpen));
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var point = e.GetPosition(this);
        var layout = CreateCubeLayout(Bounds.Width, Bounds.Height, ResolveFontSize());

        if (_orbiting)
        {
            _orbiting = false;
            e.Pointer.Capture(null);
            UpdateHover(layout.HitTest(point, _selectorOpen));
            e.Handled = true;
            return;
        }

        if (_pressedButton)
        {
            var inside = layout.Button.Contains(point);
            _pressedButton = false;
            e.Pointer.Capture(null);
            if (inside)
            {
                _ = ToggleSelector();
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_pressedPreset is { } preset)
        {
            var inside = layout.TileRect(preset).Contains(point);
            _pressedPreset = null;
            e.Pointer.Capture(null);
            if (inside)
            {
                ApplyPreset(preset);
                CloseSelector();
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _orbiting = false;
        _pressedButton = false;
        _pressedPreset = null;
        Cursor = null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (!_orbiting)
        {
            UpdateHover(new AttitudeViewHit(AttitudeViewHitKind.None));
        }
    }

    /// <summary>失焦时收起面板并取消未完成的手势。</summary>
    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        CloseSelector();
        _orbiting = false;
        _pressedButton = false;
        _pressedPreset = null;
        Cursor = null;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        CloseSelector();
        _orbiting = false;
        _pressedButton = false;
        _pressedPreset = null;
        Cursor = null;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && ToggleSelector())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _selectorOpen)
        {
            CloseSelector();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// 自绘控件默认只命中已绘制像素，网格/机体之间的缝隙会漏给父级；这里让整块边界参与命中。
    /// </summary>
    bool ICustomHitTest.HitTest(Point point) =>
        new Rect(Bounds.Size).Contains(point);

    private static string Tr(string key) => TextCatalog.Get(typeof(AttitudeView).Assembly, key);
}
