using Avalonia;
using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>命中区域类型。</summary>
internal enum AttitudeViewHitKind
{
    /// <summary>没有命中。</summary>
    None = 0,

    /// <summary>视角按钮。</summary>
    Button = 1,

    /// <summary>面板背景（含名称行）。</summary>
    Panel = 2,

    /// <summary>某个视角格子。</summary>
    Tile = 3,
}

/// <summary>一次命中结果。</summary>
/// <param name="Kind">区域类型。</param>
/// <param name="Preset">命中的视角（仅 <see cref="AttitudeViewHitKind.Tile"/> 有效）。</param>
internal readonly record struct AttitudeViewHit(
    AttitudeViewHitKind Kind,
    AttitudeViewPreset? Preset = null);

/// <summary>
/// **视角按钮与七视角面板的唯一布局**：绘制与命中共用同一组矩形。
/// </summary>
/// <remarks>
/// 面板采用接近参考图的紧凑排布：等轴测紧邻上视，不放到最右边；
/// 名称行高度由字体实测值传入，不在两处各算一套。
/// </remarks>
internal readonly struct AttitudeViewCubeLayout
{
    private const double ButtonInset = 6;
    private const double ButtonSize = 24;
    private const double PanelGap = 4;
    private const double PanelPadding = 6;
    private const double TileSize = 34;
    private const double TileGap = 4;
    private const int PanelColumns = 4;
    private const int PanelRows = 3;
    private const double MinimumWidth = 180;

    private AttitudeViewCubeLayout(
        Rect button,
        Rect panel,
        Rect footer,
        bool canShow)
    {
        Button = button;
        Panel = panel;
        Footer = footer;
        CanShow = canShow;
    }

    /// <summary>按钮矩形（控件本地坐标）。</summary>
    public Rect Button { get; }

    /// <summary>面板矩形（含名称行）。</summary>
    public Rect Panel { get; }

    /// <summary>名称行矩形。</summary>
    public Rect Footer { get; }

    /// <summary>当前尺寸是否放得下按钮与面板。</summary>
    public bool CanShow { get; }

    /// <summary>按控件尺寸与名称行高度构造布局。</summary>
    /// <param name="bounds">控件尺寸。</param>
    /// <param name="footerHeight">名称行高度（由字体实测）。</param>
    public static AttitudeViewCubeLayout Create(Size bounds, double footerHeight)
    {
        var footer = Math.Max(12, double.IsFinite(footerHeight) ? footerHeight : 12);
        var panelWidth = (PanelPadding * 2) + (PanelColumns * TileSize)
            + ((PanelColumns - 1) * TileGap);
        var panelHeight = (PanelPadding * 2) + (PanelRows * TileSize)
            + ((PanelRows - 1) * TileGap) + footer;
        var panelTop = ButtonInset + ButtonSize + PanelGap;

        var canShow = bounds.Width >= Math.Max(MinimumWidth, panelWidth + (ButtonInset * 2))
            && bounds.Height >= panelTop + panelHeight + ButtonInset;

        var button = new Rect(ButtonInset, ButtonInset, ButtonSize, ButtonSize);
        var panel = new Rect(ButtonInset, panelTop, panelWidth, panelHeight);
        var footerRect = new Rect(
            panel.X + PanelPadding,
            panel.Bottom - PanelPadding - footer,
            panel.Width - (PanelPadding * 2),
            footer);

        return new AttitudeViewCubeLayout(button, panel, footerRect, canShow);
    }

    /// <summary>取某个预设的格子矩形。</summary>
    /// <param name="preset">预设。</param>
    public Rect TileRect(AttitudeViewPreset preset)
    {
        if (!CanShow)
        {
            return default;
        }

        var (row, column) = Cell(preset);
        return new Rect(
            Panel.X + PanelPadding + (column * (TileSize + TileGap)),
            Panel.Y + PanelPadding + (row * (TileSize + TileGap)),
            TileSize,
            TileSize);
    }

    /// <summary>命中测试；绘制与输入都只依赖本方法。</summary>
    /// <param name="point">控件本地坐标。</param>
    /// <param name="selectorOpen">面板是否打开。</param>
    public AttitudeViewHit HitTest(Point point, bool selectorOpen)
    {
        if (!CanShow)
        {
            return new AttitudeViewHit(AttitudeViewHitKind.None);
        }

        if (Button.Contains(point))
        {
            return new AttitudeViewHit(AttitudeViewHitKind.Button);
        }

        if (!selectorOpen)
        {
            return new AttitudeViewHit(AttitudeViewHitKind.None);
        }

        foreach (var preset in AttitudeViewPresets.All)
        {
            if (TileRect(preset).Contains(point))
            {
                return new AttitudeViewHit(AttitudeViewHitKind.Tile, preset);
            }
        }

        return Panel.Contains(point)
            ? new AttitudeViewHit(AttitudeViewHitKind.Panel)
            : new AttitudeViewHit(AttitudeViewHitKind.None);
    }

    /// <summary>预设落在哪一行哪一列（0 起）。</summary>
    /// <param name="preset">预设。</param>
    public static (int Row, int Column) Cell(AttitudeViewPreset preset) => preset switch
    {
        AttitudeViewPreset.Top => (0, 1),
        AttitudeViewPreset.Isometric => (0, 2),
        AttitudeViewPreset.Left => (1, 0),
        AttitudeViewPreset.Front => (1, 1),
        AttitudeViewPreset.Right => (1, 2),
        AttitudeViewPreset.Back => (1, 3),
        AttitudeViewPreset.Bottom => (2, 1),
        _ => (1, 1),
    };
}

/// <summary>
/// **固定等轴框架的透明立方体图标**：所有预设共用同一轮廓，只填目标面。
/// </summary>
/// <remarks>
/// 不使用每个预设各自的相机角度：图标不是缩小的场景，而是“同一个立方体 + 哪一面亮”。
/// 背面/左面/底面透过线框可见，因此不需要把立方体转到目标面。
/// </remarks>
internal static class AttitudeViewCubeRenderer
{
    private const double Cos30 = 0.8660254037844386;

    private static readonly (double X, double Y, double Z)[] Vertices =
    [
        (-0.5, -0.5, -0.5),
        (0.5, -0.5, -0.5),
        (-0.5, 0.5, -0.5),
        (0.5, 0.5, -0.5),
        (-0.5, -0.5, 0.5),
        (0.5, -0.5, 0.5),
        (-0.5, 0.5, 0.5),
        (0.5, 0.5, 0.5),
    ];

    private static readonly (int A, int B)[] Edges =
    [
        (0, 1), (1, 3), (3, 2), (2, 0),
        (4, 5), (5, 7), (7, 6), (6, 4),
        (0, 4), (1, 5), (2, 6), (3, 7),
    ];

    private static readonly CubeFace[] Faces =
    [
        new(0, 0, 1, 4, 5, 7, 6),
        new(0, 0, -1, 0, 1, 3, 2),
        new(1, 0, 0, 1, 5, 7, 3),
        new(-1, 0, 0, 0, 4, 6, 2),
        new(0, 1, 0, 2, 3, 7, 6),
        new(0, -1, 0, 0, 1, 5, 4),
    ];

    /// <summary>画一个透明线框立方体；有目标面时只填那一面。</summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="tile">格子矩形。</param>
    /// <param name="preset">目标预设；空 = 只画框架（按钮图标）。</param>
    /// <param name="edgeBrush">棱线色。</param>
    /// <param name="accentBrush">目标面颜色。</param>
    public static void DrawIcon(
        DrawingContext context,
        Rect tile,
        AttitudeViewPreset? preset,
        IBrush edgeBrush,
        IBrush accentBrush)
    {
        var centerX = tile.X + (tile.Width / 2);
        var centerY = tile.Y + (tile.Height / 2);
        var scale = Math.Min(tile.Width, tile.Height) * 0.38;

        Span<Point> points = stackalloc Point[8];
        for (var index = 0; index < Vertices.Length; index++)
        {
            var vertex = Vertices[index];
            points[index] = new Point(
                centerX + ((vertex.X - vertex.Z) * scale * Cos30),
                centerY + ((((vertex.X + vertex.Z) * 0.5) - vertex.Y) * scale));
        }

        CubeFace? targetFace = null;
        if (preset is { } value && AttitudeViewPresets.TryGetFaceNormal(value, out var normal))
        {
            targetFace = FindFace(normal);
        }

        if (targetFace is { } face)
        {
            var geometry = CreateQuad(points, face);
            using (context.PushOpacity(0.30))
            {
                context.DrawGeometry(accentBrush, null, geometry);
            }
        }

        using (context.PushOpacity(0.85))
        {
            var pen = new Pen(edgeBrush, 1);
            foreach (var edge in Edges)
            {
                if (!IsHiddenEdge(edge.A, edge.B))
                {
                    context.DrawLine(pen, points[edge.A], points[edge.B]);
                }
            }
        }

        using (context.PushOpacity(0.40))
        {
            var pen = new Pen(edgeBrush, 1);
            foreach (var edge in Edges)
            {
                if (IsHiddenEdge(edge.A, edge.B))
                {
                    context.DrawLine(pen, points[edge.A], points[edge.B]);
                }
            }
        }

        if (targetFace is { } target)
        {
            context.DrawGeometry(null, new Pen(accentBrush, 1), CreateQuad(points, target));
        }
    }

    private static CubeFace? FindFace((double X, double Y, double Z) normal)
    {
        foreach (var face in Faces)
        {
            if (face.NX == normal.X && face.NY == normal.Y && face.NZ == normal.Z)
            {
                return face;
            }
        }

        return null;
    }

    private static StreamGeometry CreateQuad(Span<Point> points, CubeFace face)
    {
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(points[face.A], isFilled: true);
            figure.LineTo(points[face.B]);
            figure.LineTo(points[face.C]);
            figure.LineTo(points[face.D]);
            figure.EndFigure(true);
        }

        return geometry;
    }

    private static bool IsHiddenEdge(int a, int b) =>
        (a == 0 && (b == 1 || b == 2 || b == 4))
        || (b == 0 && (a == 1 || a == 2 || a == 4));

    private readonly record struct CubeFace(
        double NX,
        double NY,
        double NZ,
        int A,
        int B,
        int C,
        int D);
}
