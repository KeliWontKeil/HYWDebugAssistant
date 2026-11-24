using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Ui.Imaging;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **显示图片字节的控件**：支持静态图与 GIF 动画，自带空态/加载/错误提示。
/// </summary>
/// <remarks>
/// 控件只绘制调用方给的 <see cref="ImageBytes"/>，不读文件、不读模块值；
/// 图片属于控件内容，持久化由视图通过 <see cref="IModuleViewImageStore"/> 负责。
/// </remarks>
public sealed class ModuleImageSurface : Control, IModulePreviewBoundsTarget
{
    /// <summary>没有图片时的提示（单行）。</summary>
    public static readonly StyledProperty<string> EmptyHintProperty =
        AvaloniaProperty.Register<ModuleImageSurface, string>(nameof(EmptyHint), string.Empty);

    /// <summary>首次加载时的提示（单行）。</summary>
    public static readonly StyledProperty<string> LoadingHintProperty =
        AvaloniaProperty.Register<ModuleImageSurface, string>(nameof(LoadingHint), string.Empty);

    /// <summary>加载失败时的提示（单行）。</summary>
    public static readonly StyledProperty<string> ErrorHintProperty =
        AvaloniaProperty.Register<ModuleImageSurface, string>(nameof(ErrorHint), string.Empty);

    /// <summary>图片缩放方式。</summary>
    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<ModuleImageSurface, Stretch>(nameof(Stretch), Stretch.Uniform);

    /// <summary>归一化源图裁切矩形（0–1；null = 整图）。</summary>
    public static readonly StyledProperty<Rect?> CropProperty =
        AvaloniaProperty.Register<ModuleImageSurface, Rect?>(nameof(Crop));

    /// <summary>提示文字颜色。</summary>
    public static readonly StyledProperty<IBrush?> HintBrushProperty =
        AvaloniaProperty.Register<ModuleImageSurface, IBrush?>(nameof(HintBrush));

    /// <summary>提示文字字号。</summary>
    public static readonly StyledProperty<double> HintFontSizeProperty =
        AvaloniaProperty.Register<ModuleImageSurface, double>(nameof(HintFontSize), 12);

    private const long MaxFileBytes = 32L * 1024 * 1024;
    private const int MaxEdge = 20_000;
    private const long MaxPixels = 32_000_000;

    private ImageState _state = ImageState.Empty;
    private Bitmap? _staticImage;
    private byte[]? _imageBytes;
    private WriteableBitmap[]? _animationFrames;
    private TimeSpan[] _animationDelays = [];
    private int _animationLoopCount;
    private int _animationFrameIndex;
    private int _animationLoops;
    private int _animationToken;
    private bool _animationStopped;
    private bool _animationScheduled;
    private long _animationStartedAt;
    private double _nextFrameAtMs;
    private bool _attached;

    private int _loadVersion;
    private CancellationTokenSource? _loadCancellation;

    static ModuleImageSurface()
    {
        AffectsRender<ModuleImageSurface>(
            EmptyHintProperty,
            LoadingHintProperty,
            ErrorHintProperty,
            StretchProperty,
            CropProperty,
            HintBrushProperty,
            HintFontSizeProperty);
    }

    /// <summary>构造图片面。</summary>
    public ModuleImageSurface()
    {
        ClipToBounds = true;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    /// <summary>没有图片时的提示。</summary>
    public string EmptyHint
    {
        get => GetValue(EmptyHintProperty);
        set => SetValue(EmptyHintProperty, value);
    }

    /// <summary>首次加载时的提示。</summary>
    public string LoadingHint
    {
        get => GetValue(LoadingHintProperty);
        set => SetValue(LoadingHintProperty, value);
    }

    /// <summary>加载失败时的提示。</summary>
    public string ErrorHint
    {
        get => GetValue(ErrorHintProperty);
        set => SetValue(ErrorHintProperty, value);
    }

    /// <summary>图片缩放方式。</summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>归一化源图裁切矩形（0–1；null = 整图）。</summary>
    public Rect? Crop
    {
        get => GetValue(CropProperty);
        set => SetValue(CropProperty, value);
    }

    /// <summary>要显示的图片字节（null/空 = 空态）。</summary>
    /// <remarks>设置后异步解码；控件不读文件。</remarks>
    public byte[]? ImageBytes
    {
        get => _imageBytes;
        set
        {
            if (ReferenceEquals(_imageBytes, value))
            {
                return;
            }

            _imageBytes = value;
            if (value is null)
            {
                CancelLoad();
                DisposeImage();
                _state = ImageState.Empty;
                InvalidateVisual();
                return;
            }

            StartLoadBytes(value);
        }
    }

    /// <summary>提示文字颜色。</summary>
    public IBrush? HintBrush
    {
        get => GetValue(HintBrushProperty);
        set => SetValue(HintBrushProperty, value);
    }

    /// <summary>提示文字字号。</summary>
    public double HintFontSize
    {
        get => GetValue(HintFontSizeProperty);
        set => SetValue(HintFontSizeProperty, value);
    }

    /// <summary>当前是否已经有一张可绘制的图片（机制探针用）。</summary>
    internal bool HasImage => _staticImage is not null || _animationFrames is { Length: > 0 };

    /// <summary>最近一次加载失败的原因（机制探针用）。</summary>
    internal string? LastError { get; private set; }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var image = CurrentImage();
        if (image is not null)
        {
            var sourceSize = image.Size;
            if (sourceSize.Width > 0 && sourceSize.Height > 0)
            {
                var crop = NormalizeCrop(Crop);
                var sourceRect = new Rect(
                    crop.X * sourceSize.Width,
                    crop.Y * sourceSize.Height,
                    crop.Width * sourceSize.Width,
                    crop.Height * sourceSize.Height);
                context.DrawImage(
                    image,
                    sourceRect,
                    CalculateDestination(sourceRect.Size, new Size(width, height)));
            }

            return;
        }

        var hint = _state switch
        {
            ImageState.Loading => LoadingHint,
            ImageState.Error => ErrorHint,
            _ => EmptyHint,
        };

        DrawHint(context, hint, width, height);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;

        if (_imageBytes is not null && _state == ImageState.Empty)
        {
            StartLoadBytes(_imageBytes);
        }
        else if (_animationFrames is not null)
        {
            StartAnimation();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        CancelLoad();
        DisposeImage();
        _state = ImageState.Empty;
    }

    private IImage? CurrentImage()
    {
        if (_staticImage is not null)
        {
            return _staticImage;
        }

        return _animationFrames is { Length: > 0 } frames ? frames[_animationFrameIndex] : null;
    }

    private void StartLoadBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            DisposeImage();
            _state = ImageState.Empty;
            InvalidateVisual();
            return;
        }

        CancelLoad();
        var version = _loadVersion;
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _state = ImageState.Loading;

        if (_staticImage is null && _animationFrames is null)
        {
            InvalidateVisual();
        }

        _ = LoadBytesAsync(bytes, version, cancellation);
    }

    private async Task LoadBytesAsync(byte[] bytes, int version, CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            if (bytes.Length > MaxFileBytes)
            {
                throw new InvalidDataException("Image bytes are too large.");
            }

            await DecodeAndApplyAsync(bytes, version, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 新资产/卸载替换了这次加载。
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() => ApplyError(version, exception));
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }
        }
    }

    private async Task DecodeAndApplyAsync(byte[] bytes, int version, CancellationToken cancellationToken)
    {
        var animation = await Task.Run(
                () => TryDecodeGif(bytes, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ApplyPayload(version, bytes, animation);
            }
        });
    }

    private static GifAnimation? TryDecodeGif(byte[] bytes, CancellationToken cancellationToken) =>
        TryDecodeGif(bytes.AsSpan(), cancellationToken);

    private static GifAnimation? TryDecodeGif(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length < 6
            || (!bytes[..6].SequenceEqual("GIF87a"u8) && !bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GifAnimationDecoder.Decode(bytes);
        }
        catch (Exception)
        {
            // 动画解析失败时退回静态首帧；由 Avalonia 的图片解码做第二次尝试。
            return null;
        }
    }

    private void ApplyPayload(int version, byte[] bytes, GifAnimation? animation)
    {
        if (version != _loadVersion)
        {
            return;
        }

        if (animation is { Frames.Count: > 1 })
        {
            ApplyAnimation(animation);
            return;
        }

        if (!ImageHeader.TryReadSize(bytes, out var sourceWidth, out var sourceHeight)
            || sourceWidth > MaxEdge
            || sourceHeight > MaxEdge
            || (long)sourceWidth * sourceHeight > MaxPixels)
        {
            throw new InvalidDataException("Image is outside the supported size.");
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = Bitmap.DecodeToWidth(stream, sourceWidth);

        StopAnimation();
        DisposeImageCore();
        _staticImage = bitmap;
        _state = ImageState.Ready;
        LastError = null;
        InvalidateVisual();
    }

    private void ApplyAnimation(GifAnimation animation)
    {
        var frames = new WriteableBitmap[animation.Frames.Count];
        try
        {
            for (var index = 0; index < animation.Frames.Count; index++)
            {
                frames[index] = CreateBitmap(animation.Width, animation.Height, animation.Frames[index].Pixels);
            }
        }
        catch
        {
            foreach (var frame in frames)
            {
                frame?.Dispose();
            }

            throw;
        }

        StopAnimation();
        DisposeImageCore();
        _animationFrames = frames;
        _animationDelays = [.. animation.Frames.Select(frame => frame.Delay)];
        _animationLoopCount = animation.LoopCount;
        _animationFrameIndex = 0;
        _state = ImageState.Ready;
        LastError = null;
        InvalidateVisual();
        StartAnimation();
    }

    private static WriteableBitmap CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        try
        {
            var rowBytes = width * 4;
            using var framebuffer = bitmap.Lock();
            if (framebuffer.RowBytes < rowBytes)
            {
                throw new InvalidOperationException("Bitmap row stride is too small.");
            }

            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(
                    pixels,
                    row * rowBytes,
                    IntPtr.Add(framebuffer.Address, row * framebuffer.RowBytes),
                    rowBytes);
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    private void ApplyError(int version, Exception exception)
    {
        if (version != _loadVersion)
        {
            return;
        }

        LastError = exception.ToString();
        StopAnimation();
        DisposeImageCore();
        _state = ImageState.Error;
        InvalidateVisual();
    }

    private void CancelLoad()
    {
        _loadVersion++;
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 加载任务刚好结束并释放了 CTS。
        }
    }

    private void DisposeImage()
    {
        StopAnimation();
        DisposeImageCore();
    }

    private void DisposeImageCore()
    {
        _staticImage?.Dispose();
        _staticImage = null;

        if (_animationFrames is not null)
        {
            foreach (var frame in _animationFrames)
            {
                frame.Dispose();
            }

            _animationFrames = null;
        }

        _animationDelays = [];
        _animationLoopCount = 0;
        _animationFrameIndex = 0;
        _animationLoops = 0;
        _state = ImageState.Empty;
    }

    private void StartAnimation()
    {
        StopAnimation();
        if (_animationFrames is not { Length: > 1 } || !_attached)
        {
            return;
        }

        _animationStartedAt = Stopwatch.GetTimestamp();
        _nextFrameAtMs = _animationDelays[0].TotalMilliseconds;
        _animationFrameIndex = 0;
        _animationLoops = 0;
        _animationStopped = false;
        _animationScheduled = false;
        ScheduleAnimation();
    }

    private void StopAnimation()
    {
        _animationToken++;
        _animationStopped = true;
        _animationScheduled = false;
    }

    private void ScheduleAnimation()
    {
        if (!_attached
            || _animationStopped
            || _animationFrames is not { Length: > 1 }
            || _animationScheduled
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        _animationScheduled = true;
        var token = _animationToken;
        topLevel.RequestAnimationFrame(_ => OnAnimationFrame(token));
    }

    private void OnAnimationFrame(int token)
    {
        _animationScheduled = false;
        if (token != _animationToken
            || !_attached
            || _animationFrames is not { Length: > 1 } frames)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedMs = (now - _animationStartedAt) * 1000.0 / Stopwatch.Frequency;
        var changed = false;
        var guard = 0;

        while (elapsedMs >= _nextFrameAtMs && guard++ < 4)
        {
            var next = _animationFrameIndex + 1;
            if (next >= frames.Length)
            {
                if (_animationLoopCount > 0 && ++_animationLoops >= _animationLoopCount)
                {
                    _animationStopped = true;
                    break;
                }

                next = 0;
            }

            _animationFrameIndex = next;
            _nextFrameAtMs += _animationDelays[next].TotalMilliseconds;
            changed = true;
        }

        if (!_animationStopped && _nextFrameAtMs <= elapsedMs)
        {
            // 窗口被系统挂起后回来：不要一次追赶几百帧。
            _nextFrameAtMs = elapsedMs + _animationDelays[_animationFrameIndex].TotalMilliseconds;
        }

        if (changed)
        {
            InvalidateVisual();
        }

        if (!_animationStopped)
        {
            ScheduleAnimation();
        }
    }

    private static Rect NormalizeCrop(Rect? crop)
    {
        if (crop is not { } value
            || !double.IsFinite(value.X)
            || !double.IsFinite(value.Y)
            || !double.IsFinite(value.Width)
            || !double.IsFinite(value.Height))
        {
            return new Rect(0, 0, 1, 1);
        }

        var x = Math.Clamp(value.X, 0, 1);
        var y = Math.Clamp(value.Y, 0, 1);
        var width = Math.Clamp(value.Width, 0, 1 - x);
        var height = Math.Clamp(value.Height, 0, 1 - y);
        return width > 0 && height > 0
            ? new Rect(x, y, width, height)
            : new Rect(0, 0, 1, 1);
    }

    private Rect CalculateDestination(Size source, Size target)
    {
        if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0)
        {
            return new Rect(target);
        }

        double width;
        double height;
        switch (Stretch)
        {
            case Stretch.Fill:
                width = target.Width;
                height = target.Height;
                break;

            case Stretch.None:
                width = source.Width;
                height = source.Height;
                break;

            case Stretch.UniformToFill:
                var fillScale = Math.Max(target.Width / source.Width, target.Height / source.Height);
                width = source.Width * fillScale;
                height = source.Height * fillScale;
                break;

            default:
                var fitScale = Math.Min(target.Width / source.Width, target.Height / source.Height);
                width = source.Width * fitScale;
                height = source.Height * fitScale;
                break;
        }

        return new Rect(
            (target.Width - width) / 2,
            (target.Height - height) / 2,
            width,
            height);
    }

    private void DrawHint(DrawingContext context, string? text, double width, double height)
    {
        if (!SurfaceDrawing.HasText(text))
        {
            return;
        }

        var brush = HintBrush;
        if (brush is null)
        {
            return;
        }

        var fontSize = double.IsFinite(HintFontSize) && HintFontSize > 0 ? HintFontSize : 12;
        var formatted = SurfaceDrawing.Format(text!, fontSize, brush, Math.Max(0, width - 8));
        SurfaceDrawing.DrawCentered(context, formatted, width / 2, (height - formatted.Height) / 2, width);
    }

    private enum ImageState
    {
        Empty,
        Loading,
        Ready,
        Error,
    }
}
