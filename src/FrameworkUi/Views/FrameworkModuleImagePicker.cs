using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Ui.Images;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **模块图片选择服务**：复用应用统一的图片选择/裁切窗口，返回字节与源图裁切矩形。
/// </summary>
/// <remarks>
/// 模块只依赖 <see cref="IModuleImagePicker"/>；本类在 FrameworkUi 里把接口转成
/// <see cref="ImagePicker.PickSelectionAsync"/>，由组合根注入。
/// </remarks>
public sealed class FrameworkModuleImagePicker : IModuleImagePicker
{
    /// <inheritdoc />
    public async Task<ModuleImagePickResult> PickAsync(
        Window? owner,
        double aspectRatio,
        string? title)
    {
        if (owner is null)
        {
            return default;
        }

        try
        {
            var result = await ImagePicker.PickSelectionAsync(
                owner,
                aspectRatio,
                title ?? string.Empty);

            return result is { } selection
                ? new ModuleImagePickResult(
                    true,
                    selection.Bytes,
                    selection.Selection.SourceSize,
                    selection.Selection.SourceRect)
                : default;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or InvalidOperationException)
        {
            return default;
        }
    }
}
