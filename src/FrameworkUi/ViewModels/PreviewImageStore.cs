using Avalonia.Media.Imaging;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>模块/组合预览图的加载与缓存（按文件修改时间失效）。</summary>
public static class PreviewImageStore
{
    private static readonly Dictionary<string, (long Ticks, Bitmap? Bitmap)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>加载预览图；不存在/不可读 ⇒ null（界面显示空图块）。</summary>
    /// <param name="path">图片路径。</param>
    public static Bitmap? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var ticks = File.GetLastWriteTimeUtc(path).Ticks;
            lock (Cache)
            {
                if (Cache.TryGetValue(path, out var cached) && cached.Ticks == ticks)
                {
                    return cached.Bitmap;
                }

                var bitmap = new Bitmap(path);
                Cache[path] = (ticks, bitmap);
                return bitmap;
            }
        }
        // 预览图是可选展示：无渲染平台（无头测试）/不可解码都返回 null，界面走占位块，
        // 不能让页面构建因此中断。
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
