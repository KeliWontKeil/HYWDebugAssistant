using Avalonia;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **AttitudeView 的纯几何不变量**：默认投影等价、七预设轴向、拖动夹紧/绕回、
/// 4 列 × 3 行面板布局与命中、立方体目标面映射。呈现观感仍由截图 + 人眼判断。
/// </summary>
public class AttitudeViewGeometryTests
{
    /// <summary>(0,0) 与旧投影逐点一致；七预设的屏幕轴向符合显示口径；等轴测三轴都可见。</summary>
    [Fact]
    public void 相机等价与七预设轴向()
    {
        var origin = new AttitudeCamera(100, 80, 30, 0, 0);
        var point = origin.Project(0.4, -0.3, 0.2, out var depth);
        var factor = 4.0 / (4.0 - 0.2);

        depth.ShouldBe(0.2, 1e-9);
        point.X.ShouldBe(100 + (0.4 * 30 * factor), 1e-9);
        point.Y.ShouldBe(80 - (-0.3 * 30 * factor), 1e-9);

        var (x, y) = origin.ProjectDirection(1, 0, 0, 20);
        x.ShouldBe(20, 1e-9);
        y.ShouldBe(0, 1e-9);

        (x, y) = origin.ProjectDirection(0, 1, 0, 20);
        x.ShouldBe(0, 1e-9);
        y.ShouldBe(-20, 1e-9);

        (x, y) = origin.ProjectDirection(0, 0, 1, 20);
        x.ShouldBe(0, 1e-9);
        y.ShouldBe(0, 1e-9);

        // 轴的深度符号决定它画在机体前还是后：等轴测三轴都朝向相机，后视 Z 背离。
        var frontCamera = new AttitudeCamera(0, 0, 1, 0, 0);
        frontCamera.Depth(0, 0, 1).ShouldBe(1, 1e-9);
        frontCamera.Depth(1, 0, 0).ShouldBe(0, 1e-9);

        var backCamera = new AttitudeCamera(0, 0, 1, Math.PI, 0);
        backCamera.Depth(0, 0, 1).ShouldBe(-1, 1e-9);

        var isoCamera = new AttitudeCamera(
            0,
            0,
            1,
            45 * Math.PI / 180,
            AttitudeViewPresets.IsometricPitchDegrees * Math.PI / 180);
        isoCamera.Depth(1, 0, 0).ShouldBeGreaterThan(0);
        isoCamera.Depth(0, 1, 0).ShouldBeGreaterThan(0);
        isoCamera.Depth(0, 0, 1).ShouldBeGreaterThan(0);

        // 机体轴随机体旋转后深度会变：偏航 90° 时 +X 指向 −Z，朝前视相机就是“背面”。
        var yawedBody = new AttitudeBodyRotation(0, 0, Math.PI / 2);
        var (axisX, axisY, axisZ) = yawedBody.Rotate(1, 0, 0);
        frontCamera.Depth(axisX, axisY, axisZ).ShouldBe(-1, 1e-9);

        foreach (var preset in AttitudeViewPresets.All)
        {
            var (yaw, pitch) = AttitudeViewPresets.Angles(preset);
            var camera = new AttitudeCamera(
                0,
                0,
                1,
                yaw * Math.PI / 180,
                pitch * Math.PI / 180);
            var (xx, _) = camera.ProjectDirection(1, 0, 0, 20);
            var (_, yUp) = camera.ProjectDirection(0, 1, 0, 20);
            var (zx, zy) = camera.ProjectDirection(0, 0, 1, 20);

            switch (preset)
            {
                case AttitudeViewPreset.Front:
                    xx.ShouldBeGreaterThan(0);
                    yUp.ShouldBeLessThan(0);
                    Math.Abs(zx).ShouldBeLessThan(1e-9);
                    Math.Abs(zy).ShouldBeLessThan(1e-9);
                    break;

                case AttitudeViewPreset.Back:
                    xx.ShouldBeLessThan(0);
                    yUp.ShouldBeLessThan(0);
                    Math.Abs(zx).ShouldBeLessThan(1e-9);
                    Math.Abs(zy).ShouldBeLessThan(1e-9);
                    break;

                case AttitudeViewPreset.Left:
                    zx.ShouldBeGreaterThan(0);
                    break;

                case AttitudeViewPreset.Right:
                    zx.ShouldBeLessThan(0);
                    break;

                case AttitudeViewPreset.Top:
                    zx.ShouldBe(0, 1e-9);
                    zy.ShouldBeGreaterThan(0);
                    Math.Abs(yUp).ShouldBeLessThan(1e-9);
                    break;

                case AttitudeViewPreset.Bottom:
                    zx.ShouldBe(0, 1e-9);
                    zy.ShouldBeLessThan(0);
                    Math.Abs(yUp).ShouldBeLessThan(1e-9);
                    break;

                default:
                    Math.Abs(xx).ShouldBeGreaterThan(1);
                    Math.Abs(yUp).ShouldBeGreaterThan(1);
                    Math.Abs(zx).ShouldBeGreaterThan(1);
                    Math.Abs(zy).ShouldBeGreaterThan(1);
                    break;
            }
        }
    }

    /// <summary>拖动按起始快照换算；俯仰夹紧、偏航绕回。</summary>
    [Fact]
    public void 拖动换算夹紧与绕回()
    {
        var (yaw, pitch) = AttitudeViewMath.Orbit(170, 0, -100, 0);
        yaw.ShouldBe(-140, 1e-9);
        pitch.ShouldBe(0, 1e-9);

        (yaw, pitch) = AttitudeViewMath.Orbit(0, 85, 0, 100);
        pitch.ShouldBe(90, 1e-9);

        (yaw, pitch) = AttitudeViewMath.Orbit(0, -85, 0, -100);
        pitch.ShouldBe(-90, 1e-9);

        AttitudeViewMath.NormalizeYaw(180).ShouldBe(180, 1e-9);
        AttitudeViewMath.NormalizeYaw(-180).ShouldBe(180, 1e-9);
        AttitudeViewMath.NormalizeYaw(540).ShouldBe(180, 1e-9);
        AttitudeViewMath.NormalizeYaw(double.NaN).ShouldBe(0, 1e-9);
    }

    /// <summary>4 列 × 3 行面板的位置、命中与退化；等轴测在 (0,2)，紧邻上视。</summary>
    [Fact]
    public void 面板布局与命中()
    {
        var layout = AttitudeViewCubeLayout.Create(new Size(310, 300), 16);
        layout.CanShow.ShouldBeTrue();

        AttitudeViewCubeLayout.Cell(AttitudeViewPreset.Top).ShouldBe((0, 1));
        AttitudeViewCubeLayout.Cell(AttitudeViewPreset.Isometric).ShouldBe((0, 2));
        AttitudeViewCubeLayout.Cell(AttitudeViewPreset.Left).ShouldBe((1, 0));
        AttitudeViewCubeLayout.Cell(AttitudeViewPreset.Front).ShouldBe((1, 1));
        AttitudeViewCubeLayout.Cell(AttitudeViewPreset.Right).ShouldBe((1, 2));
        AttitudeViewCubeLayout.Cell(AttitudeViewPreset.Back).ShouldBe((1, 3));
        AttitudeViewCubeLayout.Cell(AttitudeViewPreset.Bottom).ShouldBe((2, 1));

        var top = layout.TileRect(AttitudeViewPreset.Top);
        var iso = layout.TileRect(AttitudeViewPreset.Isometric);
        iso.Y.ShouldBe(top.Y, 1e-9);
        iso.X.ShouldBe(top.X + 38, 1e-9);

        foreach (var preset in AttitudeViewPresets.All)
        {
            var tile = layout.TileRect(preset);
            tile.Left.ShouldBeGreaterThanOrEqualTo(layout.Panel.Left);
            tile.Top.ShouldBeGreaterThanOrEqualTo(layout.Panel.Top);
            tile.Right.ShouldBeLessThanOrEqualTo(layout.Panel.Right);
            tile.Bottom.ShouldBeLessThanOrEqualTo(layout.Panel.Bottom);
        }

        var topCenter = new Point(top.X + (top.Width / 2), top.Y + (top.Height / 2));
        var topHit = layout.HitTest(topCenter, selectorOpen: true);
        topHit.Kind.ShouldBe(AttitudeViewHitKind.Tile);
        topHit.Preset.ShouldBe(AttitudeViewPreset.Top);

        var buttonHit = layout.HitTest(
            new Point(layout.Button.X + 2, layout.Button.Y + 2),
            selectorOpen: false);
        buttonHit.Kind.ShouldBe(AttitudeViewHitKind.Button);

        layout.HitTest(topCenter, selectorOpen: false).Kind.ShouldBe(AttitudeViewHitKind.None);

        var footerHit = layout.HitTest(
            new Point(layout.Footer.X + 2, layout.Footer.Y + 2),
            selectorOpen: true);
        footerHit.Kind.ShouldBe(AttitudeViewHitKind.Panel);

        layout.HitTest(new Point(1, 1), selectorOpen: true).Kind.ShouldBe(AttitudeViewHitKind.None);

        AttitudeViewCubeLayout.Create(new Size(100, 100), 16).CanShow.ShouldBeFalse();

        AttitudeViewPresets.TryGetFaceNormal(AttitudeViewPreset.Front, out var normal).ShouldBeTrue();
        normal.ShouldBe((0.0, 0.0, 1.0));

        AttitudeViewPresets.TryGetFaceNormal(AttitudeViewPreset.Back, out normal).ShouldBeTrue();
        normal.ShouldBe((0.0, 0.0, -1.0));

        AttitudeViewPresets.TryGetFaceNormal(AttitudeViewPreset.Left, out normal).ShouldBeTrue();
        normal.ShouldBe((-1.0, 0.0, 0.0));

        AttitudeViewPresets.TryGetFaceNormal(AttitudeViewPreset.Right, out normal).ShouldBeTrue();
        normal.ShouldBe((1.0, 0.0, 0.0));

        AttitudeViewPresets.TryGetFaceNormal(AttitudeViewPreset.Top, out normal).ShouldBeTrue();
        normal.ShouldBe((0.0, 1.0, 0.0));

        AttitudeViewPresets.TryGetFaceNormal(AttitudeViewPreset.Bottom, out normal).ShouldBeTrue();
        normal.ShouldBe((0.0, -1.0, 0.0));

        AttitudeViewPresets.TryGetFaceNormal(AttitudeViewPreset.Isometric, out _).ShouldBeFalse();
    }
}
