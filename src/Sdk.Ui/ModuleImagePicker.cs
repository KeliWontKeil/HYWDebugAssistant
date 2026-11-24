using Avalonia;
using Avalonia.Controls;

namespace HYWDebugAssistant.Sdk.Ui;

/// <summary>
/// **模块图片选择结果**：原始字节 + 源图像素尺寸 + 源图裁切矩形。
/// </summary>
/// <param name="Confirmed">是否确认选择（false = 取消、关闭或不可用）。</param>
/// <param name="Bytes">原始图片字节（含 GIF）；用于控件图片状态。</param>
/// <param name="SourceSize">源图像素尺寸。</param>
/// <param name="CropRect">源图像素坐标中的裁切矩形。</param>
public readonly record struct ModuleImagePickResult(
    bool Confirmed,
    byte[]? Bytes,
    PixelSize SourceSize,
    Rect CropRect);

/// <summary>
/// **模块视图可用的框架图片选择服务**：选择本地图片并打开统一裁切窗口。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IModuleColorPicker"/> 同一模式：Sdk.Ui 只暴露接口，具体实现由
/// 框架 UI 复用已有的 <c>ImagePicker</c> / <c>ImagePickerDialog</c>，经宿主组合根注入。
/// 模块视图拿到结果后写入控件的持久化图片状态，不建立新的数据通道。
/// </para>
/// <para>
/// 设计期或未接线时 <see cref="IModuleViewContext.ImagePicker"/> 为 <see langword="null"/>，
/// 视图不得弹窗。
/// </para>
/// </remarks>
public interface IModuleImagePicker
{
    /// <summary>选择图片并裁切。</summary>
    /// <param name="owner">属主窗口（null = 不可用）。</param>
    /// <param name="aspectRatio">裁切框目标宽高比（宽 / 高）。</param>
    /// <param name="title">选择窗口标题（null/空 = 默认标题）。</param>
    /// <returns>选择结果；取消/失败时 <see cref="ModuleImagePickResult.Confirmed"/> 为 false。</returns>
    Task<ModuleImagePickResult> PickAsync(
        Window? owner,
        double aspectRatio,
        string? title);
}
