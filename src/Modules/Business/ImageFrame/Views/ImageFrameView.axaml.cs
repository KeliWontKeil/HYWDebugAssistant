using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.ImageFrame.Views;

/// <summary>图片框视图（布局在 <c>ImageFrameView.axaml</c>）。</summary>
public sealed partial class ImageFrameView : ModuleView, IModuleViewElement
{
    private readonly MenuItem? _clearImageItem;
    private readonly ModuleImageSurface? _surface;
    private IModuleViewContext? _context;
    private long _lastImageRevision = -1;

    /// <summary>构造视图。</summary>
    public ImageFrameView()
    {
        AvaloniaXamlLoader.Load(this);
        _clearImageItem = this.FindControl<MenuItem>("ClearImageItem");
        _surface = this.FindControl<ModuleImageSurface>("Image");
    }

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (context.ViewImageStore is not { } store)
        {
            return;
        }

        var revision = store.Revision;
        if (revision == _lastImageRevision)
        {
            return;
        }

        _lastImageRevision = revision;
        SetImage(store.Read());
    }

    private void SetImage(ModuleViewImage? image)
    {
        if (_clearImageItem is not null)
        {
            _clearImageItem.IsEnabled = image is not null;
        }

        if (_surface is null)
        {
            return;
        }

        _surface.Crop = image?.Crop;
        _surface.ImageBytes = image?.Bytes;
    }

    private async void OnChangeImage(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        var context = _context;
        var surface = _surface;
        if (context?.ImagePicker is not { } picker
            || context.ViewImageStore is not { } store
            || surface is null
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var aspect = surface.Bounds.Width / surface.Bounds.Height;
        if (!double.IsFinite(aspect) || aspect <= 0)
        {
            aspect = 1;
        }

        try
        {
            var result = await picker.PickAsync(
                owner,
                aspect,
                TextCatalog.Get(typeof(ImageFrameView).Assembly, ImageFrameText.PickTitle));

            if (!result.Confirmed
                || result.Bytes is not { Length: > 0 } bytes
                || result.SourceSize.Width <= 0
                || result.SourceSize.Height <= 0)
            {
                return;
            }

            var crop = NormalizeCrop(result.SourceSize, result.CropRect);
            if (!store.Write(bytes, crop))
            {
                return;
            }

            _lastImageRevision = store.Revision;
            SetImage(new ModuleViewImage(bytes, crop));
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            // 选择器/对话框失败时保留当前图片。
        }
    }

    private void OnClearImage(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (_context?.ViewImageStore is { } store)
        {
            if (!store.Write(null, null))
            {
                return;
            }

            _lastImageRevision = store.Revision;
        }

        SetImage(null);
    }

    private static Rect? NormalizeCrop(PixelSize source, Rect crop)
    {
        var width = Math.Max(1, source.Width);
        var height = Math.Max(1, source.Height);
        var x = Math.Clamp(crop.X / width, 0, 1);
        var y = Math.Clamp(crop.Y / height, 0, 1);
        var w = Math.Clamp(crop.Width / width, 0, 1 - x);
        var h = Math.Clamp(crop.Height / height, 0, 1 - y);
        return w > 0 && h > 0
            ? new Rect(x, y, w, h)
            : null;
    }
}

/// <summary>图片框视图工厂。</summary>
public sealed class ImageFrameViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ImageFrameView();
    }
}
