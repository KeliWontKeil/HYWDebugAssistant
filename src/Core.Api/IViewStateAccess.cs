using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **视图状态存取面**（UI 侧）〔2026-09 用户裁定 · `decisions.md` 7.28/7.31〕。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>谁写</b>：**UI**（切页 ⇒ 选中页；输入框文本变化 ⇒ 当前文本；日志追加 ⇒ 显示缓冲）；</item>
///   <item><b>谁读</b>：① **模块**经 <see cref="IViewStateReader"/>（只读快照，模块在活动体里拉取）
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

    /// <summary>读取"某输入源当前文本"（UI 重建元素表时恢复输入框）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">输入源名。</param>
    /// <returns>当前文本；未知 ⇒ <see langword="null"/>。</returns>
    string? GetInputText(string instanceId, string sourceName);

    /// <summary>
    /// 取某显示源的**共享文本缓冲**（UI 渲染与模块回读**同一份**）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <returns>该源的缓冲（首次调用即创建；随实例移除清理）。</returns>
    DisplayBuffer Display(string instanceId, string sourceName);

    /// <summary>移除某实例的全部视图状态（L4 移除实例时调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    void RemoveInstance(string instanceId);

    /// <summary>
    /// **模块请求"清空某显示源"**〔2026-09〕：进 L4 的挂起表，等 **UI 在刷新节拍里取走并执行**
    /// （与"UI 拉取"同一模型：框架不跨线程碰 UI）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    void RequestClear(string instanceId, string sourceName);

    /// <summary>
    /// 取走一个待执行的"清空显示源"请求（UI 在刷新节拍里调用）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <returns>取到为 <see langword="true"/>；没有待处理请求为 <see langword="false"/>。</returns>
    bool TryTakeClear(string instanceId, out string? sourceName);

    /// <summary>
    /// **模块请求"往某显示源追加文本"**〔2026-09 用户裁定 · WinForms 模型〕。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="RequestClear"/> 同一模型：**模块线程只入队，UI 在刷新节拍里落地**
    /// （显示缓冲的唯一写者仍是 UI 线程 ⇒ 界面绑定的集合不会被跨线程改）。
    /// <paramref name="lineBreak"/> = true 表示"这一段的换行由**通道**表达"（数据里没有换行字符）。
    /// </remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <param name="text">文本（原样）。</param>
    /// <param name="lineBreak">这一段之后是否由通道换行。</param>
    void RequestAppend(string instanceId, string sourceName, string text, bool lineBreak);

    /// <summary>
    /// 取走某实例**全部**待落地的追加请求（UI 在刷新节拍里调用；取走即清空）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>按入队顺序的追加请求（没有则空表）。</returns>
    IReadOnlyList<DisplayAppend> DrainAppends(string instanceId);

    /// <summary>
    /// **查看**（不取走）某显示源待落地的追加请求 —— 模块**回读**时据此把"还没被 UI 取走的尾巴"叠上。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <returns>按入队顺序的追加请求（没有则空表）。</returns>
    /// <remarks>"所见即所存"要求这条路径存在：UI 一个节拍（50 ms）才落地一次，而保存随时可能发生。</remarks>
    IReadOnlyList<DisplayAppend> PeekAppends(string instanceId, string sourceName);
}

/// <summary>
/// 一条待落地的**显示追加**（模块 → 框架的请求；见 <see cref="IViewStateAccess.RequestAppend"/>）。
/// </summary>
/// <param name="SourceName">显示源名。</param>
/// <param name="Text">文本（原样）。</param>
/// <param name="LineBreak">这一段之后是否由**通道**换行（不是文本里的字符）。</param>
public readonly record struct DisplayAppend(string SourceName, string Text, bool LineBreak);
