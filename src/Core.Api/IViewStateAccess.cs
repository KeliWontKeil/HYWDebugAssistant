using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **视图状态存取面**（UI 侧）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>谁写</b>：**UI**（切页 ⇒ 选中页；折叠区展开/收起 ⇒ 折叠态；输入框文本变化 ⇒ 当前文本；日志追加 ⇒ 显示缓冲）；</item>
///   <item><b>谁读</b>：① **模块**经 <see cref="IViewStateReader"/>（只读快照，模块在 OnStep 里拉取）
///     ② **UI 自己**（元素表重建时把用户**未提交**的文本恢复回输入框 —— 修掉"切页即丢编辑"）；</item>
///   <item><b>为什么不经变更入口</b>：这是**视图状态**（UI 的、不进组合件、不持久化），
///     不是模块状态 ⇒ 不走 L4 的串行变更路径（输入文本**逐键变化**，走那条路既无必要也会淹掉变更队列）；</item>
///   <item><b>生命周期</b>：随实例移除清理（<see cref="RemoveInstance"/>，由 L4 在移除实例时调用）。</item>
/// </list>
/// </remarks>
public interface IViewStateAccess
{
    /// <summary>某实例的**只读视图状态**（交给模块的那一份）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>只读读取面（未知项一律 <see langword="null"/> / 空）。</returns>
    IViewStateReader ReaderFor(string instanceId);

    /// <summary>写入"某容器当前选中页"（UI 切页时调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="stateKey">容器的 <c>ViewElement.StateKey</c>。</param>
    /// <param name="page">选中页序号（0 基）。</param>
    void SetSelectedPage(string instanceId, string stateKey, int page);

    /// <summary>读取"某容器当前选中页"（UI 重建元素表时用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="stateKey">容器的 <c>ViewElement.StateKey</c>。</param>
    /// <returns>选中页序号；未知 ⇒ <see langword="null"/>。</returns>
    int? GetSelectedPage(string instanceId, string stateKey);

    /// <summary>写入"某输入源当前文本"（UI 文本变化时调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">输入源名。</param>
    /// <param name="text">当前文本（含未提交内容）。</param>
    void SetInputText(string instanceId, string sourceName, string? text);

    /// <summary>写入"某容器是否折叠"（UI 展开/收起折叠区时调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="stateKey">容器的 <c>ViewElement.StateKey</c>。</param>
    /// <param name="collapsed">是否折叠。</param>
    void SetCollapsed(string instanceId, string stateKey, bool collapsed);

    /// <summary>读取"某容器是否折叠"（UI 重建元素表时恢复折叠态）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="stateKey">容器的 <c>ViewElement.StateKey</c>。</param>
    /// <returns>是否折叠；未知 ⇒ <see langword="null"/>（⇒ 用默认：展开）。</returns>
    bool? GetCollapsed(string instanceId, string stateKey);

    /// <summary>读取"某输入源当前文本"（UI 重建元素表时恢复输入框）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">输入源名。</param>
    /// <returns>当前文本；未知 ⇒ <see langword="null"/>。</returns>
    string? GetInputText(string instanceId, string sourceName);

    /// <summary>取某显示源的**只读视图**（UI 读取窗口/尾部；不能写）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <returns>只读显示视图（首次调用即创建；随实例移除清理）。</returns>
    IDisplayBufferReader Display(string instanceId, string sourceName);

    /// <summary>移除某实例的全部视图状态（L4 移除实例时调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    void RemoveInstance(string instanceId);
}
