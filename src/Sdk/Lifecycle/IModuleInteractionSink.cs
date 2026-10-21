namespace HYWDebugAssistant.Sdk.Lifecycle;

/// <summary>
/// **可选**：模块没有活动体时，由实例循环把命令 / 输入 / "有工作"推给它。
/// </summary>
/// <remarks>
/// <para>
/// 它不改变数据的双向流动，也不替代收件箱：它只是把同一个收件箱从"只能由活动体拉"变成
/// "可被实例循环推给无活动体模块"。命令/输入仍然只有一份来源（<c>IModuleContext.Inbox</c>）。
/// </para>
/// <para>
/// <b>与活动体的关系</b>：模块实现了 <see cref="IActivitySource"/> 时活动体优先，本接口的回调**不会**
/// 被投递（避免两个消费者）；实例创建时会对此给出明确提示。
/// </para>
/// <para>
/// <b>线程</b>：三个方法都在该实例的专属线程上调用；实现里不要阻塞、不要跨线程回调框架。
/// </para>
/// </remarks>
public interface IModuleInteractionSink
{
    /// <summary>一条命令（用户在模块窗口点按钮）。</summary>
    /// <param name="commandName">模块声明过的命令名。</param>
    void OnCommand(string commandName);

    /// <summary>一条输入（用户提交的值）。</summary>
    /// <param name="sourceName">模块声明过的输入源名。</param>
    /// <param name="text">用户输入的文本。</param>
    void OnInput(string sourceName, string text);

    /// <summary>
    /// "这一拍有工作"：实例循环刚刚处理过数据 / 命令 / 状态变化。
    /// </summary>
    /// <remarks>
    /// 它**不携带状态值**：模块应去读显示源与会话快照（唯一事实源）。实现可以只在值变化时写界面。
    /// </remarks>
    void OnWork();
}
