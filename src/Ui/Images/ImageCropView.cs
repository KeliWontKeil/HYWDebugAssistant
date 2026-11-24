using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>
/// 固定比例裁切框：铺满、居中、滚轮缩放、拖动平移；渲染时只显示框内部分。
/// </summary>
public sealed class ImageCropView : Control
{
    /// <summary>源图像。</summary>
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ImageCropView, Bitmap?>(nameof(Source));

    /// <summary>目标宽高比（宽 / 高）。</summary>
    public static readonly StyledProperty<double> AspectRatioProperty =
        AvaloniaProperty.Register<ImageCropView, double>(nameof(AspectRatio), 1);

    private ImageCropMath? _math;
    private Point _lastPointer;
    private bool _dragging;

    /// <summary>创建裁切框。</summary>
    public ImageCropView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>源图像。</summary>
    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>目标宽高比。</summary>
    public double AspectRatio
    {
        get => GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    /// <summary>当前裁切几何（无图 ⇒ null）。</summary>
    public ImageCropMath? Math => _math;

    /// <summary>重置为居中铺满。</summary>
    public void Reset()
    {
        _math?.Reset();
        InvalidateVisual();
    }

    /// <summary>设置缩放。</summary>
    /// <param name="zoom">缩放倍率。</param>
    public void SetZoom(double zoom)
    {
        _math?.SetZoom(zoom);
        InvalidateVisual();
    }

    /// <summary>返回当前裁切的源图矩形（无图 ⇒ null）。</summary>
    public ImageCropSelection? CaptureSelection()
    {
        if (Source is not { } source)
        {
            return null;
        }

        var math = _math;
        if (math is null)
        {
            var viewSize = Bounds.Size;
            if (viewSize.Width <= 0 || viewSize.Height <= 0)
            {
                return null;
            }

            math = new ImageCropMath(
                new Size(source.PixelSize.Width, source.PixelSize.Height),
                viewSize,
                AspectRatio);
            _math = math;
        }

        var rect = math.SourceRect;
        return rect.Width > 0 && rect.Height > 0
            ? new ImageCropSelection(source.PixelSize, rect)
            : null;
    }

    /// <summary>把当前裁切结果编码为 PNG（无图 ⇒ null）。</summary>
    public byte[]? CapturePng()
    {
        if (Source is not { } source || _math is not { } math)
        {
            return null;
        }

        var crop = math.SourceRect;
        var output = math.OutputSize();
        using var target = new RenderTargetBitmap(output, new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(source, crop, new Rect(0, 0, output.Width, output.Height));
        }

        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Source is not { } source)
        {
            return;
        }

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        if (_math is null)
        {
            _math = new ImageCropMath(
                new Size(source.PixelSize.Width, source.PixelSize.Height),
                size,
                AspectRatio);
        }
        else
        {
            _math.SetViewSize(size);
        }

        using (context.PushClip(new Rect(size)))
        {
            context.DrawImage(source, _math.DestinationRect);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty || change.Property == AspectRatioProperty)
        {
            _math = null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_math is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging || _math is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        _math.PanBy(point - _lastPointer);
        _lastPointer = point;
        InvalidateVisual();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_math is not { } math)
        {
            return;
        }

        var factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        math.SetZoom(math.Zoom * factor);
        InvalidateVisual();
        e.Handled = true;
    }
}
