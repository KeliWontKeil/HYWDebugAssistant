using Avalonia.Controls;

namespace HYWDebugAssistant.Sdk.Ui;

/// <summary>
/// **模块取色结果**（与框架调色窗口同一套语义）。
/// </summary>
/// <param name="Confirmed">是否点了确定/恢复（false = 取消或直接关闭）。</param>
/// <param name="ColorArgb">选中的颜色（<c>0xAARRGGBB</c>；恢复时无意义）。</param>
/// <param name="Reset">是否点了"恢复"（调用方按自己的语义清空覆盖）。</param>
public readonly record struct ModuleColorPickResult(bool Confirmed, uint ColorArgb, bool Reset);

/// <summary>
/// **模块视图可用的框架取色服务**：打开框架统一的调色窗口，不要求模块自建色板。
/// </summary>
/// <remarks>
/// <para>
/// 实现由 FrameworkUi 提供（复用 `ColorPickerDialog`），经宿主组合根注入到
/// <see cref="IModuleViewContext"/>；Sdk.Ui 只暴露接口，不依赖 FrameworkUi。
/// </para>
/// <para>
/// <see cref="SuggestedColors"/> 与调色窗口推荐色同源（主题推荐色），供视图在
/// 通道首次出现数据时做一次自动分配；之后颜色应由模块持久化。
/// </para>
/// </remarks>
public interface IModuleColorPicker
{
    /// <summary>推荐色（<c>0xAARRGGBB</c>；为空 = 无推荐色）。</summary>
    IReadOnlyList<uint> SuggestedColors { get; }

    /// <summary>打开框架调色窗口并等待结果。</summary>
    /// <param name="owner">属主窗口（null = 非模态；设计期/测试用）。</param>
    /// <param name="initialArgb">初始颜色（null = 由实现给中性初值）。</param>
    /// <param name="title">窗口标题（null/空 = 默认标题）。</param>
    /// <param name="resetText">"恢复"按钮文案（null/空 = 不显示）。</param>
    /// <returns>取色结果。</returns>
    Task<ModuleColorPickResult> PickAsync(
        Window? owner,
        uint? initialArgb,
        string? title,
        string? resetText);
}
