using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Lifecycle;

namespace HYWDebugAssistant.Modules.ImageFrame;

/// <summary>
/// **图片框**：在工作区展示一张本地图片或 GIF 动图。
/// </summary>
/// <remarks>
/// <para>
/// 零端口、零角色、零值、零输入、不参与数据流：它只是工作区里的一个摆件。
/// 图片、裁切与清除全部属于视图控件自己的持久化状态，由视图通过
/// <c>IModuleViewContext.ViewImageStore</c> 读写；模块不保存路径或裁切，也不接触模块壁纸。
/// </para>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "image-frame",
    DisplayNameKey = ImageFrameText.Title,
    DescriptionKey = ImageFrameText.Description,
    Tags = ["other", "image", "decor"],
    Capabilities = ModuleCapability.None,
    ParticipatesInDataFlow = false,
    ViewFactory = typeof(Views.ImageFrameViewFactory))]
public sealed class ImageFrameModule : IModule
{
}
