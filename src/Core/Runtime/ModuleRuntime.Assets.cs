using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Persistence;

namespace HYWDebugAssistant.Core.Runtime;

/// <summary>实例图片资源的用途：模块壁纸 / 控件图片。两者共用同一条资产链，但各有独立的槽。</summary>
public enum ModuleInstanceAssetKind
{
    /// <summary>模块窗口壁纸（<c>presentation.wallpaper</c>）。</summary>
    Wallpaper = 0,

    /// <summary>模块视图控件的持久化图片（<c>viewState.image</c>）。</summary>
    ViewImage = 1,
}

/// <summary>一个实例的图片资产（内容寻址；壁纸与控件图片共用同一套存储）。</summary>
/// <param name="Ref">包内相对引用（<c>assets/&lt;sha256&gt;.&lt;ext&gt;</c>）。</param>
/// <param name="Bytes">原始字节。</param>
/// <param name="Crop">归一化裁切（仅控件图片使用；壁纸固定整图）。</param>
/// <param name="Revision">呈现版本（视图据此懒加载）。</param>
public sealed record ModuleInstanceAsset(string Ref, byte[] Bytes, ViewImageCrop Crop, long Revision);

public sealed partial class ModuleRuntime
{
    private readonly Dictionary<(string InstanceId, ModuleInstanceAssetKind Kind), ModuleInstanceAsset> _assets =
        new();

    private readonly Lock _assetGate = new();
    private long _assetRevision;
    private long _wallpaperVersion;

    /// <summary>取某实例某用途的图片资产（不存在 ⇒ null）。返回副本。</summary>
    public ModuleInstanceAsset? GetAsset(string instanceId, ModuleInstanceAssetKind kind)
    {
        lock (_assetGate)
        {
            return _assets.TryGetValue((instanceId, kind), out var asset)
                ? asset with { Bytes = [.. asset.Bytes] }
                : null;
        }
    }

    /// <summary>取某实例某用途的资产版本（不存在 ⇒ 0）。</summary>
    public long GetAssetRevision(string instanceId, ModuleInstanceAssetKind kind)
    {
        lock (_assetGate)
        {
            return _assets.TryGetValue((instanceId, kind), out var asset) ? asset.Revision : 0;
        }
    }

    /// <summary>设置/清除某实例某用途的图片资产；实例不存在或扩展名不支持 ⇒ false。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="kind">用途。</param>
    /// <param name="bytes">图片字节；null/空 = 清除。</param>
    /// <param name="crop">归一化裁切（壁纸忽略）。</param>
    /// <param name="extension">真实扩展名（壁纸固定 <c>png</c>；控件图片按字节识别）。</param>
    public bool SetAsset(
        string instanceId,
        ModuleInstanceAssetKind kind,
        byte[]? bytes,
        ViewImageCrop crop,
        string extension)
    {
        if (!Content.IsInstanceIdTaken(instanceId))
        {
            return false;
        }

        lock (_assetGate)
        {
            if (bytes is not { Length: > 0 })
            {
                if (_assets.Remove((instanceId, kind)))
                {
                    BumpRevision(kind);
                }

                return true;
            }

            if (!CompositeAssetRef.TryCreate(bytes, extension, out var reference, out _))
            {
                return false;
            }

            _assets[(instanceId, kind)] = new ModuleInstanceAsset(reference, [.. bytes], crop, ++_assetRevision);
            if (kind == ModuleInstanceAssetKind.Wallpaper)
            {
                _wallpaperVersion++;
            }

            return true;
        }
    }

    /// <summary>从组合件/工作区恢复某实例某用途的图片资产（引用按原样保留，含旧 <c>.img</c>）。</summary>
    public void RestoreAsset(
        string instanceId,
        ModuleInstanceAssetKind kind,
        string reference,
        byte[]? bytes,
        ViewImageCrop crop)
    {
        if (bytes is not { Length: > 0 }
            || !CompositeAssetRef.TryParse(reference, out var normalized, out _))
        {
            RemoveAsset(instanceId, kind);
            return;
        }

        lock (_assetGate)
        {
            _assets[(instanceId, kind)] = new ModuleInstanceAsset(normalized, [.. bytes], crop, ++_assetRevision);
            if (kind == ModuleInstanceAssetKind.Wallpaper)
            {
                _wallpaperVersion++;
            }
        }
    }

    /// <summary>移除某实例某用途的图片资产（实例移除/清空时调用）。</summary>
    public void RemoveAsset(string instanceId, ModuleInstanceAssetKind kind)
    {
        lock (_assetGate)
        {
            if (_assets.Remove((instanceId, kind)))
            {
                BumpRevision(kind);
            }
        }
    }

    /// <summary>
    /// 壁纸的**全局版本**：任何实例的壁纸被设置 / 恢复 / 移除都会递增（清除也递增）。
    /// </summary>
    /// <remarks>呈现层把该值放进实例视图缓存键；控件图片变化不影响它。</remarks>
    public long WallpaperVersion
    {
        get
        {
            lock (_assetGate)
            {
                return _wallpaperVersion;
            }
        }
    }

    /// <summary>取全部实例某用途的图片资产（保存组合件/工作区用）。</summary>
    public IReadOnlyDictionary<string, ModuleInstanceAsset> CaptureAssets(ModuleInstanceAssetKind kind)
    {
        lock (_assetGate)
        {
            var result = new Dictionary<string, ModuleInstanceAsset>(StringComparer.Ordinal);
            foreach (var ((instanceId, assetKind), asset) in _assets)
            {
                if (assetKind == kind)
                {
                    result[instanceId] = asset with { Bytes = [.. asset.Bytes] };
                }
            }

            return result;
        }
    }

    private void BumpRevision(ModuleInstanceAssetKind kind)
    {
        _ = ++_assetRevision;
        if (kind == ModuleInstanceAssetKind.Wallpaper)
        {
            _wallpaperVersion++;
        }
    }
}
