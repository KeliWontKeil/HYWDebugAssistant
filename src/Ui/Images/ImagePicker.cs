using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>全局图片选择入口：选文件 → 解码 → 按比例裁切 → PNG 字节。</summary>
/// <remarks>
/// 所有应用内图片选择（主题壁纸、模块壁纸、组合封面）都走这里；
/// 调用方只拿到裁切后的 PNG 字节，不接触原始文件路径。
/// </remarks>
public static class ImagePicker
{
    /// <summary>输入文件大小上限（32 MB）。</summary>
    public const long MaxInputBytes = 32L * 1024 * 1024;

    /// <summary>源图边长上限（防止病态图片把内存打爆）。</summary>
    public const int MaxSourceEdge = 20_000;

    /// <summary>选择并裁切一张图片；取消/失败 ⇒ null。</summary>
    /// <param name="owner">属主窗口（系统文件选择器需要它）。</param>
    /// <param name="aspect">目标宽高比（宽 / 高）。</param>
    /// <param name="title">选择器标题。</param>
    public static async Task<byte[]?> PickAsync(Window owner, double aspect, string title)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(TextCatalog.Get(typeof(ImagePicker).Assembly, "filePicker.imageType"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"],
                },
            ],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { Length: > 0 } path)
        {
            return null;
        }

        try
        {
            if (new FileInfo(path).Length is <= 0 or > MaxInputBytes)
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            if (bitmap.PixelSize.Width > MaxSourceEdge || bitmap.PixelSize.Height > MaxSourceEdge)
            {
                bitmap.Dispose();
                return null;
            }

            using (bitmap)
            {
                var dialog = new ImagePickerDialog(bitmap, aspect)
                {
                    Title = title,
                };

                return await dialog.ShowDialog<byte[]?>(owner);
            }
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or InvalidOperationException)
        {
            return null;
        }
    }
}
