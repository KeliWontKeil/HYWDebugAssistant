using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>壁纸/预览图的居中画刷缓存。</summary>
/// <remarks>
/// <para>
/// 壁纸显示必须**居中裁切**，不能靠左上/底部；因此这里统一产出
/// <c>UniformToFill + Alignment=Center</c> 的 <see cref="ImageBrush"/>。
/// **不做预缩小/预模糊**：清晰度优先，只由渲染器按目标尺寸缩放。
/// </para>
/// <para>
/// **有界缓存**：主题壁纸按内容寻址（换图即换键），切主题/改壁纸会不断产生新键；
/// 若只增不删，每张 4K 级壁纸的解码像素都会永久驻留。这里按**解码后的像素字节数**计预算，
/// 超出后按最近最少使用淘汰；被淘汰的条目只摘缓存引用、**不 Dispose**——已绑定的视觉可能还要画一帧，
/// 旧图由 GC 在最后一帧之后回收。
/// </para>
/// </remarks>
public static class WallpaperImageCache
{
    /// <summary>
    /// 缓存保留上限（解码后像素字节数的近似和）。
    /// </summary>
    /// <remarks>
    /// 取两张 4K 级壁纸的量级：当前主题 + 最近一张有足够的回头余量，
    /// 又不会把每次主题切换的历史图都留在进程里。
    /// </remarks>
    private const long MaxRetainedBytes = 64L * 1024 * 1024;

    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前缓存条目的像素字节近似和（用于淘汰，不是进程内存读数）。</summary>
    private static long _retainedBytes;

    /// <summary>单调访问序号（LRU 比较用）。</summary>
    private static long _clock;

    /// <summary>从字节取壁纸画刷（<paramref name="key"/> 用于缓存；同一 key 内容必须稳定）。</summary>
    public static ImageBrush? FromBytes(string key, byte[]? bytes)
    {
        if (string.IsNullOrWhiteSpace(key) || bytes is not { Length: > 0 })
        {
            return null;
        }

        var cacheKey = "bytes:" + key;
        if (TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return Create(new Bitmap(stream), cacheKey);
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

        var cacheKey = "res:" + uri;
        if (TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return Create(new Bitmap(stream), cacheKey);
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
        if (TryGet(key, out var cached))
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
            if (Cache.Remove(key, out var removed))
            {
                _retainedBytes -= removed.Bytes;
            }
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

        // 预算按解码后的 RGBA 像素量估算：与显示尺寸无关，且能覆盖不同分辨率的壁纸。
        var bytes = Math.Max(1, (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
        Add(key, brush, bytes);
        return brush;
    }

    private static bool TryGet(string key, out ImageBrush? brush)
    {
        if (Cache.TryGetValue(key, out var entry))
        {
            Cache[key] = entry with { LastUsed = ++_clock };
            brush = entry.Brush;
            return true;
        }

        brush = null;
        return false;
    }

    private static void Add(string key, ImageBrush brush, long bytes)
    {
        if (Cache.Remove(key, out var previous))
        {
            _retainedBytes -= previous.Bytes;
        }

        Cache[key] = new Entry(brush, bytes, ++_clock);
        _retainedBytes += bytes;
        Trim();
    }

    /// <summary>按 LRU 淘汰到预算以内；**至少保留一条**（当前正在用的那条不能被自己挤掉）。</summary>
    private static void Trim()
    {
        while (_retainedBytes > MaxRetainedBytes && Cache.Count > 1)
        {
            string? oldestKey = null;
            var oldestUse = long.MaxValue;

            foreach (var pair in Cache)
            {
                if (pair.Value.LastUsed < oldestUse)
                {
                    oldestUse = pair.Value.LastUsed;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey is null || !Cache.Remove(oldestKey, out var removed))
            {
                return;
            }

            _retainedBytes -= removed.Bytes;
        }
    }

    /// <summary>一条缓存：画刷 + 像素字节近似值 + 最近访问序号。</summary>
    private readonly record struct Entry(ImageBrush Brush, long Bytes, long LastUsed);
}
