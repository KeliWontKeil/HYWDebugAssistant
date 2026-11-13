namespace HYWDebugAssistant.Sdk.Context;

/// <summary>
/// **模块可读的「视图状态」**（只读快照）。
/// </summary>
/// <remarks>
/// <para>
/// 当前选中页应当是**可被模块感知的属性**，这有利于 **UI 整体简洁化**；
/// 取向是**符合 winforms 的开发直觉**—— 本接口对应 WinForms 里
/// "**控件属性即状态**"（`textBox.Text` / `tabControl.SelectedIndex`）在本框架的落点。
/// </para>
/// <para>
/// <b>读什么（三样）</b>：① 某 <c>TabPages</c> 的**当前选中页** ② 某输入源的**当前文本**
/// （含用户**正在编辑、尚未提交**的内容）③ 某文本显示源**当前保留的文本**（显示缓冲的内容）。
/// </para>
/// <para>
/// <b>怎么读</b>：**拉取**（在模块的 OnStep 里取快照）—— **不做推送**：输入文本逐键变化，
/// 推进有界收件箱会被淹没。
/// </para>
/// <para>
/// <b>语义边界（务必按此理解）</b>：
/// <list type="bullet">
///   <item><b>只读</b>：模块**不能**通过本接口改 UI 状态（要改就走输入 / 命令通道）；</item>
///   <item><b>快照</b>：读到的是框架**最近一次写入**的值，不保证与用户此刻的眼睛同步（通常差一个刷新节拍）；</item>
///   <item><b>持久化由值声明决定</b>：声明为持久化的值随组合件/工作区保存；未声明的值重启即丢；</item>
///   <item><b>未知即 null / 空</b>：没声明、没渲染、名字写错 ⇒ 一律返回 <see langword="null"/>（行列表为空），
///     **不抛异常、不猜**；</item>
///   <item>**随实例移除清理**（关闭模块即丢，与"不持久化"一致）。</item>
/// </list>
/// </para>
/// <para>
/// <b>怎么写</b>：模块用声明里的名字寻址 —— 输入源 / 显示源用**数据源名**；容器（如 <c>TabPages</c>）
/// **没有数据源名** ⇒ 用声明里的 `stateKey`（可选 `init` 属性）。
/// </para>
/// </remarks>
public interface IViewStateReader
{
    /// <summary>
    /// 某 <c>TabPages</c> 的**当前选中页序号**（0 基）。
    /// </summary>
    /// <param name="stateKey">容器声明里的 `stateKey`。</param>
    /// <returns>选中页序号；未声明该键 / 无此容器 ⇒ <see langword="null"/>。</returns>
    int? GetSelectedPage(string stateKey);

    /// <summary>
    /// 某输入源的**当前文本**（含**正在编辑、尚未提交**的内容）。
    /// </summary>
    /// <param name="sourceName">输入元素的 <c>DataSource</c> 名（与 <c>SubmitInput</c> 同一名字）。</param>
    /// <returns>当前文本；未声明该输入 / 从未渲染 ⇒ <see langword="null"/>。</returns>
    /// <remarks>
    /// 这是"**所见即所发**"的落点：点按钮时模块读到的就是用户在框里看到的文本，
    /// 不依赖「用户是否按过回车」（因此不需要「命令按钮关联输入源」那条机制）。
    /// </remarks>
    string? GetInputText(string sourceName);

    /// <summary>
    /// **把一个输入源的值写回去**（模块对"这一格现在应该是什么值"的宣告）。
    /// </summary>
    /// <param name="sourceName">输入元素的 <c>DataSource</c> 名。</param>
    /// <param name="text">要写入的文本；<see langword="null"/> = 清掉这一格的值。</param>
    /// <remarks>
    /// <para>
    /// <b>为什么需要它</b>：`ResetInteraction` 只能表达"**这次动作不成立**"，表达不了
    /// "**这一格的值应该是 false**"；而"把这一格拨回"的实际实现是"拨回**框架记着的那一份值**"。
    /// 于是模块判定"开关不该开着"时，那一格若没有**值**可拨回，就会停在用户刚拨到的位置上 ——
    /// 看起来就是"弹回去了、但再按一下又能违法地开着"。写回值之后，"拨回"才真正有依据。
    /// </para>
    /// <para>
    /// <b>与输入通道的分工</b>：用户改字走 `SubmitInput`（框架把它投进模块的收件箱）；
    /// 本方法是**反方向**的 —— 模块说"这一格现在是这个值"，框架据此更新界面与视图状态，
    /// **不回投给模块**（否则模块会被自己写的值唤醒，形成回环）。
    /// </para>
    /// <para>
    /// <b>纪律</b>：与同族一致 —— 未声明该输入 / 无此容器 ⇒ **静默忽略，不抛**。
    /// </para>
    /// </remarks>
    void SetInputValue(string sourceName, string? text);

    /// <summary>
    /// 某文本显示源**当前保留的文本**（显示缓冲的**全部内容**，不只是屏幕上可见的那几行）。
    /// </summary>
    /// <param name="sourceName">显示元素的 <c>DataSource</c> 名。</param>
    /// <returns>文本行快照（**调用时复制**；未声明 / 无内容 ⇒ 空列表）。</returns>
    /// <remarks>
    /// 用于「**保存显示内容**」这类需求（读文本框本身的内存即可）：
    /// 缓冲由框架持有（**不清空就一直在**，容量上限后丢最旧），模块**不需要**自己再留一份副本。
    /// </remarks>
    IReadOnlyList<string> GetDisplayLines(string sourceName);
}
