namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// 画布矩形（模块视图的位置与尺寸）——自由画布的几何基础类型。
/// </summary>
/// <param name="X">左上角 X。</param>
/// <param name="Y">左上角 Y。</param>
/// <param name="Width">宽。</param>
/// <param name="Height">高。</param>
public readonly record struct CanvasRect(double X, double Y, double Width, double Height)
{
    /// <summary>左边缘。</summary>
    public double Left => X;

    /// <summary>上边缘。</summary>
    public double Top => Y;

    /// <summary>右边缘。</summary>
    public double Right => X + Width;

    /// <summary>下边缘。</summary>
    public double Bottom => Y + Height;

    /// <summary>中心 X。</summary>
    public double CenterX => X + (Width / 2);

    /// <summary>中心 Y。</summary>
    public double CenterY => Y + (Height / 2);

    /// <summary>是否具有正的尺寸（零尺寸的模块视图不参与几何判定）。</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>按新位置平移（尺寸不变）。</summary>
    public CanvasRect At(double x, double y) => new(x, y, Width, Height);

    /// <summary>按偏移平移。</summary>
    public CanvasRect OffsetBy(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    /// <inheritdoc />
    public override string ToString() => $"({X:0.##}, {Y:0.##}, {Width:0.##}×{Height:0.##})";
}
