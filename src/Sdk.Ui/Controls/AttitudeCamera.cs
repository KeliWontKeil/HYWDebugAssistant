using Avalonia;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>三轴姿态的七种预设视角。</summary>
internal enum AttitudeViewPreset
{
    /// <summary>前视。</summary>
    Front = 0,

    /// <summary>后视。</summary>
    Back = 1,

    /// <summary>左视。</summary>
    Left = 2,

    /// <summary>右视。</summary>
    Right = 3,

    /// <summary>上视。</summary>
    Top = 4,

    /// <summary>下视。</summary>
    Bottom = 5,

    /// <summary>等轴测。</summary>
    Isometric = 6,
}

/// <summary>预设视角的角度、文案键与目标面。</summary>
internal static class AttitudeViewPresets
{
    /// <summary>等轴测俯仰（度）。</summary>
    public static readonly double IsometricPitchDegrees = Math.Atan(1 / Math.Sqrt(2)) * 180 / Math.PI;

    /// <summary>全部预设（面板绘制顺序不依赖枚举顺序，命中遍历用）。</summary>
    public static readonly IReadOnlyList<AttitudeViewPreset> All =
    [
        AttitudeViewPreset.Front,
        AttitudeViewPreset.Back,
        AttitudeViewPreset.Left,
        AttitudeViewPreset.Right,
        AttitudeViewPreset.Top,
        AttitudeViewPreset.Bottom,
        AttitudeViewPreset.Isometric,
    ];

    /// <summary>取预设的偏航/俯仰（度）。</summary>
    /// <param name="preset">预设。</param>
    public static (double Yaw, double Pitch) Angles(AttitudeViewPreset preset) => preset switch
    {
        AttitudeViewPreset.Front => (0, 0),
        AttitudeViewPreset.Back => (180, 0),
        AttitudeViewPreset.Left => (-90, 0),
        AttitudeViewPreset.Right => (90, 0),
        AttitudeViewPreset.Top => (0, 90),
        AttitudeViewPreset.Bottom => (0, -90),
        _ => (45, IsometricPitchDegrees),
    };

    /// <summary>预设的文案键（Sdk.Ui 文本表）。</summary>
    /// <param name="preset">预设。</param>
    public static string TextKey(AttitudeViewPreset preset) => preset switch
    {
        AttitudeViewPreset.Front => "attitude.view.front",
        AttitudeViewPreset.Back => "attitude.view.back",
        AttitudeViewPreset.Left => "attitude.view.left",
        AttitudeViewPreset.Right => "attitude.view.right",
        AttitudeViewPreset.Top => "attitude.view.top",
        AttitudeViewPreset.Bottom => "attitude.view.bottom",
        _ => "attitude.view.isometric",
    };

    /// <summary>预设对应的世界坐标系面法线；等轴测没有单一目标面。</summary>
    /// <param name="preset">预设。</param>
    /// <param name="normal">面法线。</param>
    public static bool TryGetFaceNormal(
        AttitudeViewPreset preset,
        out (double X, double Y, double Z) normal)
    {
        normal = preset switch
        {
            AttitudeViewPreset.Front => (0, 0, 1),
            AttitudeViewPreset.Back => (0, 0, -1),
            AttitudeViewPreset.Left => (-1, 0, 0),
            AttitudeViewPreset.Right => (1, 0, 0),
            AttitudeViewPreset.Top => (0, 1, 0),
            AttitudeViewPreset.Bottom => (0, -1, 0),
            _ => default,
        };

        return preset != AttitudeViewPreset.Isometric;
    }
}

/// <summary>机体三轴旋转（滚转 → 俯仰 → 偏航），与显示口径一致。</summary>
internal readonly struct AttitudeBodyRotation
{
    private readonly double _rollCos;
    private readonly double _rollSin;
    private readonly double _pitchCos;
    private readonly double _pitchSin;
    private readonly double _yawCos;
    private readonly double _yawSin;

    /// <summary>构造旋转。</summary>
    /// <param name="roll">横滚（弧度）。</param>
    /// <param name="pitch">俯仰（弧度）。</param>
    /// <param name="yaw">偏航（弧度）。</param>
    public AttitudeBodyRotation(double roll, double pitch, double yaw)
    {
        _rollCos = Math.Cos(roll);
        _rollSin = Math.Sin(roll);
        _pitchCos = Math.Cos(pitch);
        _pitchSin = Math.Sin(pitch);
        _yawCos = Math.Cos(yaw);
        _yawSin = Math.Sin(yaw);
    }

    /// <summary>旋转一个机体系点。</summary>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    public (double X, double Y, double Z) Rotate(double x, double y, double z)
    {
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

/// <summary>世界固定、相机可轨道旋转的透视相机（纯值类型）。</summary>
/// <remarks>
/// 机体旋转在相机之外完成：先按机体角度旋转点，再交给本结构做视角旋转与透视。
/// 默认偏航/俯仰为零时，投影与旧的固定相机逐点一致（+Z 朝观察者）。
/// </remarks>
internal readonly struct AttitudeCamera
{
    private const double CameraDistance = 4;

    private readonly double _centerX;
    private readonly double _centerY;
    private readonly double _scale;
    private readonly double _rightX;
    private readonly double _rightY;
    private readonly double _rightZ;
    private readonly double _upX;
    private readonly double _upY;
    private readonly double _upZ;
    private readonly double _viewX;
    private readonly double _viewY;
    private readonly double _viewZ;

    /// <summary>构造相机。</summary>
    /// <param name="centerX">屏幕中心 X。</param>
    /// <param name="centerY">屏幕中心 Y。</param>
    /// <param name="scale">每模型单位的屏幕尺度。</param>
    /// <param name="yaw">视角偏航（弧度）。</param>
    /// <param name="pitch">视角俯仰（弧度）。</param>
    public AttitudeCamera(double centerX, double centerY, double scale, double yaw, double pitch)
    {
        _centerX = centerX;
        _centerY = centerY;
        _scale = scale;

        var sinYaw = Math.Sin(yaw);
        var cosYaw = Math.Cos(yaw);
        var sinPitch = Math.Sin(pitch);
        var cosPitch = Math.Cos(pitch);

        _viewX = cosPitch * sinYaw;
        _viewY = sinPitch;
        _viewZ = cosPitch * cosYaw;

        _rightX = cosYaw;
        _rightY = 0;
        _rightZ = -sinYaw;

        _upX = -sinPitch * sinYaw;
        _upY = cosPitch;
        _upZ = -sinPitch * cosYaw;
    }

    /// <summary>世界点 → 屏幕。</summary>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    public Point Project(double x, double y, double z) => Project(x, y, z, out _);

    /// <summary>世界点 → 屏幕，并给出相机深度（越大越靠近观察者）。</summary>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    /// <param name="depth">深度。</param>
    public Point Project(double x, double y, double z, out double depth)
    {
        depth = (x * _viewX) + (y * _viewY) + (z * _viewZ);
        var factor = CameraDistance / (CameraDistance - depth);
        if (!(factor > 0.05) || !double.IsFinite(factor))
        {
            factor = 0.05;
        }

        var screenX = (x * _rightX) + (y * _rightY) + (z * _rightZ);
        var screenY = (x * _upX) + (y * _upY) + (z * _upZ);
        return new Point(
            _centerX + (_scale * screenX * factor),
            _centerY - (_scale * screenY * factor));
    }

    /// <summary>机体系点 → 屏幕（先机体旋转，再视角旋转与透视）。</summary>
    /// <param name="body">机体旋转。</param>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    public Point ProjectBody(in AttitudeBodyRotation body, double x, double y, double z) =>
        ProjectBody(body, x, y, z, out _);

    /// <summary>机体系点 → 屏幕，并给出相机深度。</summary>
    /// <param name="body">机体旋转。</param>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    /// <param name="depth">深度。</param>
    public Point ProjectBody(
        in AttitudeBodyRotation body,
        double x,
        double y,
        double z,
        out double depth)
    {
        var (rx, ry, rz) = body.Rotate(x, y, z);
        return Project(rx, ry, rz, out depth);
    }

    /// <summary>世界方向 → 屏幕二维方向（正交、固定半径，用于左下参考轴）。</summary>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    /// <param name="radius">屏幕半径。</param>
    public (double X, double Y) ProjectDirection(double x, double y, double z, double radius) =>
        (radius * ((x * _rightX) + (y * _rightY) + (z * _rightZ)),
            -radius * ((x * _upX) + (y * _upY) + (z * _upZ)));

    /// <summary>世界方向在相机中的深度（越大越靠近观察者）。</summary>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    public double Depth(double x, double y, double z) => (x * _viewX) + (y * _viewY) + (z * _viewZ);
}

/// <summary>视角角度的归一化与拖动换算（控件与测试共用）。</summary>
internal static class AttitudeViewMath
{
    /// <summary>每 DIP 的拖动角度（度）。</summary>
    public const double DragDegreesPerDip = 0.5;

    /// <summary>把偏航归一化到 (−180, 180]。</summary>
    /// <param name="degrees">任意角度。</param>
    public static double NormalizeYaw(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return 0;
        }

        var value = ((degrees % 360) + 360) % 360;
        return value > 180 ? value - 360 : value;
    }

    /// <summary>把俯仰夹在 [−90, 90]；非有限值回 0。</summary>
    /// <param name="degrees">任意角度。</param>
    public static double ClampPitch(double degrees) =>
        double.IsFinite(degrees) ? Math.Clamp(degrees, -90, 90) : 0;

    /// <summary>按起始角度与总位移得到新的视角角度。</summary>
    /// <param name="yawDegrees">拖动开始时的偏航。</param>
    /// <param name="pitchDegrees">拖动开始时的俯仰。</param>
    /// <param name="dx">水平位移（DIP）。</param>
    /// <param name="dy">垂直位移（DIP）。</param>
    public static (double Yaw, double Pitch) Orbit(
        double yawDegrees,
        double pitchDegrees,
        double dx,
        double dy) =>
        (NormalizeYaw(yawDegrees - (dx * DragDegreesPerDip)),
            ClampPitch(pitchDegrees + (dy * DragDegreesPerDip)));
}
