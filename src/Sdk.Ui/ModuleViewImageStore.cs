using Avalonia;

namespace HYWDebugAssistant.Sdk.Ui;

/// <summary>模块控件持久化的图片状态：原始字节 + 归一化裁切。</summary>
/// <param name="Bytes">图片原始字节（PNG/GIF 等）。</param>
/// <param name="Crop">归一化裁切（0–1；null = 整图）。</param>
public readonly record struct ModuleViewImage(byte[] Bytes, Rect? Crop);

/// <summary>
/// **控件持久化的图片状态**：属于当前模块视图中的图片控件，不属于模块壁纸。
/// </summary>
/// <remarks>
/// 实现由宿主提供；图片随组合件/工作区的 <c>viewState</c> 与 <c>assets/</c> 保存。
/// 与模块壁纸是不同层级：壁纸属于模块窗口呈现，图片属于控件内容。
/// </remarks>
public interface IModuleViewImageStore
{
    /// <summary>当前图片版本（无图 = 0；写入/清除后变化）。</summary>
    long Revision { get; }

    /// <summary>读取当前控件图片（无 ⇒ null；返回副本）。</summary>
    ModuleViewImage? Read();

    /// <summary>写入/清除当前控件图片。</summary>
    /// <param name="bytes">图片字节；null/空 = 清除。</param>
    /// <param name="crop">归一化裁切；null = 整图。</param>
    /// <returns>是否写入成功。</returns>
    bool Write(byte[]? bytes, Rect? crop);
}
