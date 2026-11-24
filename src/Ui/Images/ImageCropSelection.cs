using Avalonia;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>源图裁切结果（像素坐标）。</summary>
/// <param name="SourceSize">源图像素尺寸。</param>
/// <param name="SourceRect">源图裁切矩形（像素）。</param>
public readonly record struct ImageCropSelection(PixelSize SourceSize, Rect SourceRect);

/// <summary>选择到的本地图片字节及其裁切结果。</summary>
/// <param name="Bytes">原始图片字节。</param>
/// <param name="Selection">裁切结果。</param>
public readonly record struct ImageCropFileSelection(
    byte[] Bytes,
    ImageCropSelection Selection);
