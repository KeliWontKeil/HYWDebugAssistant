using Avalonia;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>
/// 图片裁切视图的纯几何：始终按目标比例铺满视口，默认居中，可缩放/平移。
/// </summary>
/// <remarks>
/// 与控件解耦，便于单测：控件只把指针/滚轮翻译成 <see cref="Zoom"/> 与 <see cref="Pan"/>。
/// 显示的图像坐标到视口坐标是同一个 scale；源矩形由可见视口反算。
/// </remarks>
public sealed class ImageCropMath
{
    /// <summary>最小缩放（1 = 刚好铺满视口）。</summary>
    public const double MinZoom = 1;

    /// <summary>最大缩放。</summary>
    public const double MaxZoom = 8;

    /// <summary>创建裁切几何。</summary>
    /// <param name="imageSize">源图像素尺寸。</param>
    /// <param name="viewSize">裁切框尺寸（DIP / 像素同口径）。</param>
    /// <param name="aspectRatio">目标宽高比（宽 / 高）。</param>
    public ImageCropMath(Size imageSize, Size viewSize, double aspectRatio)
    {
        ImageSize = Normalize(imageSize);
        ViewSize = Normalize(viewSize);
        AspectRatio = aspectRatio > 0 && double.IsFinite(aspectRatio) ? aspectRatio : 1;
        Reset();
    }

    /// <summary>源图像素尺寸。</summary>
    public Size ImageSize { get; private set; }

    /// <summary>视口尺寸。</summary>
    public Size ViewSize { get; private set; }

    /// <summary>目标宽高比。</summary>
    public double AspectRatio { get; }

    /// <summary>当前缩放（1–8）。</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>当前平移（视口像素；以居中为 0）。</summary>
    public Vector Pan { get; private set; }

    /// <summary>图像在视口中的显示尺寸。</summary>
    public Size DisplayedSize
    {
        get
        {
            var scale = Scale;
            return new Size(ImageSize.Width * scale, ImageSize.Height * scale);
        }
    }

    /// <summary>图像在视口中的落位（左上角）。</summary>
    public Rect DestinationRect
    {
        get
        {
            var displayed = DisplayedSize;
            return new Rect(
                (ViewSize.Width - displayed.Width) / 2 + Pan.X,
                (ViewSize.Height - displayed.Height) / 2 + Pan.Y,
                displayed.Width,
                displayed.Height);
        }
    }

    /// <summary>当前视口对应的源矩形（输出裁切用）。</summary>
    public Rect SourceRect
    {
        get
        {
            var scale = Scale;
            var origin = DestinationRect.TopLeft;
            var x = Math.Clamp(-origin.X / scale, 0, Math.Max(0, ImageSize.Width - ViewSize.Width / scale));
            var y = Math.Clamp(-origin.Y / scale, 0, Math.Max(0, ImageSize.Height - ViewSize.Height / scale));
            return new Rect(x, y, Math.Min(ImageSize.Width, ViewSize.Width / scale), Math.Min(ImageSize.Height, ViewSize.Height / scale));
        }
    }

    /// <summary>缩放倍率（图像像素 → 视口像素）。</summary>
    public double Scale => Math.Max(ViewSize.Width / ImageSize.Width, ViewSize.Height / ImageSize.Height) * Zoom;

    /// <summary>重置为“铺满并居中”。</summary>
    public void Reset()
    {
        Zoom = 1;
        Pan = default;
    }

    /// <summary>设置视口尺寸（控件尺寸变化时保持当前缩放与相对位置）。</summary>
    /// <param name="size">新视口尺寸。</param>
    public void SetViewSize(Size size)
    {
        var normalized = Normalize(size);
        if (Math.Abs(normalized.Width - ViewSize.Width) < 0.01
            && Math.Abs(normalized.Height - ViewSize.Height) < 0.01)
        {
            return;
        }

        ViewSize = normalized;
        Pan = ClampPan(Pan);
    }

    /// <summary>设置缩放并夹取平移。</summary>
    /// <param name="zoom">新的缩放。</param>
    public void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(double.IsFinite(zoom) ? zoom : 1, MinZoom, MaxZoom);
        Pan = ClampPan(Pan);
    }

    /// <summary>按视口像素平移（拖动图像时用）。</summary>
    /// <param name="delta">视口像素增量。</param>
    public void PanBy(Vector delta)
    {
        Pan = ClampPan(Pan + delta);
    }

    /// <summary>把平移夹在“图像仍覆盖视口”的范围内。</summary>
    /// <param name="pan">候选平移。</param>
    public Vector ClampPan(Vector pan)
    {
        var displayed = DisplayedSize;
        var maxX = Math.Max(0, (displayed.Width - ViewSize.Width) / 2);
        var maxY = Math.Max(0, (displayed.Height - ViewSize.Height) / 2);
        return new Vector(
            Math.Clamp(double.IsFinite(pan.X) ? pan.X : 0, -maxX, maxX),
            Math.Clamp(double.IsFinite(pan.Y) ? pan.Y : 0, -maxY, maxY));
    }

    /// <summary>输出像素尺寸（保持比例；长边上限）。</summary>
    /// <param name="maxEdge">长边上限。</param>
    public PixelSize OutputSize(int maxEdge = 4096)
    {
        var source = SourceRect;
        var width = Math.Max(1, (int)Math.Round(source.Width));
        var height = Math.Max(1, (int)Math.Round(source.Height));
        var scale = Math.Min(1, maxEdge / (double)Math.Max(width, height));
        return new PixelSize(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static Size Normalize(Size size) => new(
        Math.Max(1, double.IsFinite(size.Width) ? size.Width : 1),
        Math.Max(1, double.IsFinite(size.Height) ? size.Height : 1));
}
