using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>壁纸/预览图的居中画刷缓存。</summary>
/// <remarks>
/// 壁纸显示必须**居中裁切**，不能靠左上/底部；因此这里统一产出
/// <c>UniformToFill + Alignment=Center</c> 的 <see cref="ImageBrush"/>。
/// **不做预缩小/预模糊**：清晰度优先，只由渲染器按目标尺寸缩放。
/// </remarks>
public static class WallpaperImageCache
{
    private static readonly Dictionary<string, ImageBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>从字节取壁纸画刷（<paramref name="key"/> 用于缓存；同一 key 内容必须稳定）。</summary>
    public static ImageBrush? FromBytes(string key, byte[]? bytes)
    {
        if (string.IsNullOrWhiteSpace(key) || bytes is not { Length: > 0 })
        {
            return null;
        }

        if (Cache.TryGetValue("bytes:" + key, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return Create(new Bitmap(stream), "bytes:" + key);
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>从 Avalonia 资源 URI 取壁纸画刷（内置主题壁纸）。</summary>
    public static ImageBrush? FromResource(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        if (Cache.TryGetValue("res:" + uri, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return Create(new Bitmap(stream), "res:" + uri);
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>普通预览图（仍然居中裁切）。</summary>
    public static ImageBrush? Preview(Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        var key = "preview:" + bitmap.GetHashCode();
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        return Create(bitmap, key);
    }

    /// <summary>
    /// 移除某个模块实例的全部壁纸缓存（**只丢字典引用，不 Dispose 图片**）。
    /// </summary>
    /// <param name="instanceId">模块实例 ID。</param>
    /// <remarks>
    /// 关闭模块或换壁纸后，旧画刷可能仍被已绑定的视觉短暂持有；此时 Dispose 会让仍在绘制的一帧用坏图，
    /// 因此这里只让缓存不再持有它，等界面释放后由 GC 回收。
    /// </remarks>
    public static void EvictModule(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        var prefix = "bytes:module:" + instanceId + ":";
        foreach (var key in Cache.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _ = Cache.Remove(key);
        }
    }

    private static ImageBrush Create(Bitmap bitmap, string key)
    {
        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TileMode = TileMode.None,
        };

        Cache[key] = brush;
        return brush;
    }
}
