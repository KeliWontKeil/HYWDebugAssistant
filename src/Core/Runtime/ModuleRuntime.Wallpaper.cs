using System.Security.Cryptography;

namespace HYWDebugAssistant.Core.Runtime;

/// <summary>一个模块实例的壁纸资产（内容寻址；只在内存/组合件包里，不落模块目录）。</summary>
/// <param name="AssetRef">相对引用（包内 <c>assets/&lt;sha256&gt;.png</c> 的纯文件名部分）。</param>
/// <param name="Png">PNG 字节。</param>
/// <param name="Revision">呈现版本（UI 据此懒加载）。</param>
public sealed record ModuleWallpaperAsset(string AssetRef, byte[] Png, long Revision);

public sealed partial class ModuleRuntime
{
    private readonly Dictionary<string, ModuleWallpaperAsset> _wallpapers = new(StringComparer.Ordinal);
    private readonly Lock _wallpaperGate = new();
    private long _wallpaperRevision;

    /// <summary>取某实例的壁纸（不存在 ⇒ null）。返回副本，调用方不能改内部字节。</summary>
    public ModuleWallpaperAsset? GetWallpaper(string instanceId)
    {
        lock (_wallpaperGate)
        {
            return _wallpapers.TryGetValue(instanceId, out var asset)
                ? asset with { Png = [.. asset.Png] }
                : null;
        }
    }

    /// <summary>取某实例的壁纸版本（无壁纸 ⇒ 0）。</summary>
    public long GetWallpaperRevision(string instanceId)
    {
        lock (_wallpaperGate)
        {
            return _wallpapers.TryGetValue(instanceId, out var asset) ? asset.Revision : 0;
        }
    }

    /// <summary>设置/清除某实例壁纸；实例不存在 ⇒ false。</summary>
    public bool SetWallpaper(string instanceId, byte[]? png)
    {
        if (!Content.IsInstanceIdTaken(instanceId))
        {
            return false;
        }

        lock (_wallpaperGate)
        {
            if (png is not { Length: > 0 })
            {
                _ = _wallpapers.Remove(instanceId);
                return true;
            }

            var hash = Convert.ToHexStringLower(SHA256.HashData(png));
            _wallpapers[instanceId] = new ModuleWallpaperAsset(hash, [.. png], ++_wallpaperRevision);
            return true;
        }
    }

    /// <summary>从组合件/工作区恢复壁纸引用（<paramref name="assetRef"/> 为纯 hash 或 <c>assets/&lt;hash&gt;.png</c>）。</summary>
    public void RestoreWallpaper(string instanceId, string? assetRef, byte[]? png)
    {
        if (png is not { Length: > 0 } || string.IsNullOrWhiteSpace(assetRef))
        {
            RemoveWallpaper(instanceId);
            return;
        }

        lock (_wallpaperGate)
        {
            _wallpapers[instanceId] = new ModuleWallpaperAsset(NormalizeRef(assetRef), [.. png], ++_wallpaperRevision);
        }
    }

    /// <summary>移除某实例壁纸（实例移除/清空时调用）。</summary>
    public void RemoveWallpaper(string instanceId)
    {
        lock (_wallpaperGate)
        {
            _ = _wallpapers.Remove(instanceId);
        }
    }

    /// <summary>取全部实例的壁纸引用与字节（保存组合件/工作区用）。</summary>
    public IReadOnlyDictionary<string, (string Ref, byte[] Png)> CaptureWallpapers()
    {
        lock (_wallpaperGate)
        {
            var result = new Dictionary<string, (string Ref, byte[] Png)>(StringComparer.Ordinal);
            foreach (var (instanceId, asset) in _wallpapers)
            {
                result[instanceId] = (asset.AssetRef, [.. asset.Png]);
            }

            return result;
        }
    }

    /// <summary>把包内引用归一为纯文件名（保存时写 <c>assets/&lt;hash&gt;.png</c>）。</summary>
    public static string NormalizeRef(string assetRef)
    {
        var value = assetRef.Replace('\\', '/').Trim();
        var slash = value.LastIndexOf('/');
        return slash >= 0 ? value[(slash + 1)..] : value;
    }
}
