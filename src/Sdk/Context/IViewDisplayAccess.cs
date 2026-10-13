namespace HYWDebugAssistant.Sdk.Context;

/// <summary>
/// **模块对自己发布的显示内容的请求面**。
/// </summary>
/// <remarks>
/// <para>
/// <b>方向纪律</b>：`IModuleContext` 的注释写明"模块 → 框架 = **声明 / 上报 / 请求**；框架 → 模块 = 钩子 + 投递回调"。
/// 本接口属其中的**请求**：模块说"把我这个显示源的内容清掉"，**不是**"改 UI 状态"——
/// 它与 `IViewStateReader`（只读：选中页 / 输入框文本）**互不越界**：
/// 模块**读得到**界面事实，但**改不了**用户的选中页与编辑内容；这里能动的是**模块自己发布出去的那份显示内容**。
/// </para>
/// <para>
/// <b>为什么不能靠「再发一行空行」代替</b>：显示缓冲的语义是「**不清空就一直在**」
/// ⇒ 只有**真正的清空**才能把它变空；
/// 而缓冲由**框架持有**（模块不留副本），所以模块够不到它 —— 必须由模块**请求**、框架执行。
/// </para>
/// <para>
/// <b>落地</b>：请求进 L4 的挂起表，**UI 在刷新节拍里取走并执行**（与"UI 拉取"同一模型：
/// 框架不跨线程碰 UI）。**不进组合件、不持久化**。
/// </para>
/// <para>
/// <b>只增</b>：<c>Clear</c> 与 <see cref="AppendText"/> / <see cref="AppendLine"/> 都是新增成员
/// （后者把「换行归谁表达」从**文本里的标记**改成**通道调用**）；
/// 两者都保证既有模块零改动（老的「发布定长样本」路径照常工作）。
/// </para>
/// </remarks>
public interface IViewDisplayAccess
{
    /// <summary>
    /// **追加文本**（**不换行**）：数据**直接接在后面**，不加任何标记。
    /// </summary>
    /// <param name="dataSourceName">显示元素的 <c>DataSource</c> 名（在视图声明里登记过）。</param>
    /// <param name="text">要追加的文本（已经是显示形态：文本或 `AA BB CC`）。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>追加语义</b>：不开换行或时间戳时，追加数据应**直接追加在后面**、
    ///     不做任何标记，以保证数据的完整性。</item>
    ///   <item><b>它不表示换行</b>：一行多长由**文本框宽度**（自动折行）决定，数据侧不规定任何字节数
    ///     —— 于是不存在"半行/pending tail"：数据**到达即落地**；</item>
    ///   <item><b>换行是"通道的事"</b>：数据里真的有换行**字符**时，那些字符就在 <paramref name="text"/> 里；
    ///     而"每个数据包一行/加时间戳"这类**通道换行**由 <see cref="AppendLine"/> 表达 —— 它不是字符，
    ///     因此**显示层不会给它画 `↵`**。</item>
    ///   <item><b>命名</b>：与框架侧的 <c>DisplayBuffer.AppendText</c> 同名同义（"追加文本"）；
    ///     老的 <c>DisplayBuffer.Append</c> 是"追加一条（= 一行）"的历史成员，语义不同，故**不复用那个名字**。</item>
    /// </list>
    /// </remarks>
    void AppendText(string dataSourceName, string text);

    /// <summary>
    /// **追加一行**（文本 + 由**通道**补一个换行）：与本口既有的"每个数据包一行 / 加时间戳"对应。
    /// </summary>
    /// <param name="dataSourceName">显示元素的 <c>DataSource</c> 名（在视图声明里登记过）。</param>
    /// <param name="text">本行的文本（不含任何标记）。</param>
    /// <remarks>
    /// 与 <see cref="AppendText"/> 的差别**只在换行归谁表达**：这里由**通道调用**表达换行，而不是往文本里塞换行字符
    /// ⇒ 显示层据此**不画 `↵`**（通道引进的换行不是字符，永远不该有 ↵）。
    /// </remarks>
    void AppendLine(string dataSourceName, string text);

    /// <summary>
    /// **把某个显示源的文本替换成这一句**（消息 / 状态这类"只有当前值"的内容用它）。
    /// </summary>
    /// <param name="dataSourceName">显示元素的 <c>DataSource</c> 名（在视图声明里登记过）。</param>
    /// <param name="text">新的全部内容；空串 ⇒ 等价清空。</param>
    /// <remarks>
    /// 与 <see cref="AppendText"/> / <see cref="AppendLine"/> 的分工：那两条是**只增**（日志、流），
    /// 本方法是**替换**。消息类内容要的是替换语义 —— 连写两次该只看到后一句，而不是两句都留着。
    /// </remarks>
    void SetText(string dataSourceName, string text);

    /// <summary>
    /// 请求**清空**某个显示源的当前内容（模块的「清空」命令用它落点）。
    /// </summary>
    /// <param name="dataSourceName">显示元素的 <c>DataSource</c> 名（在视图声明里登记过）。</param>
    /// <remarks>未声明的源名 ⇒ 请求被忽略（**不抛异常** —— 与"未知一律安全"同一纪律）。</remarks>
    void Clear(string dataSourceName);
}
