using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Hosting.Context;

/// <summary>
/// 某个输入元素的**最近一次处理结果**（G3）。
/// </summary>
/// <param name="SourceName">输入元素的数据源名（= 视图声明里该元素的 <c>DataSource</c>）。</param>
/// <param name="Succeeded">是否处理成功（false ⇒ 界面渲染失败态）。</param>
/// <param name="Message">可选原因（呈现文本；框架不理解其语义）。</param>
public readonly record struct InputFeedbackEntry(string SourceName, bool Succeeded, string? Message);

/// <summary>
/// **输入处理结果的存放处**（G3）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>谁写</b>：模块经 <see cref="IInputFeedbackReporter.Report"/>（即 <c>IModuleContext.InputFeedback</c>）
///     在**自己的线程**上写；</item>
///   <item><b>谁清</b>：框架在**投递入队时**清（见 <c>ModuleRuntime</c> 的 <c>SubmitInputOperation</c> 分支）
///     ⇒ 语义 = "**下一次提交即刷新**"；**没回报过就没有失败态**（模块默认什么都不做也不受影响）；</item>
///   <item><b>谁读</b>：L4 经 <c>Core.Api</c> 的查询面只读转出；UI 按刷新节拍取；
///     **不参与**建网/兼容/投流/计量，**不进组合件**（纯运行期呈现状态）；</item>
///   <item><b>线程</b>：模块线程写、UI 线程读 ⇒ 一把短锁保护（不在数据面热路径上）。</item>
/// </list>
/// </remarks>
public sealed class InputFeedbackStore : IInputFeedbackReporter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, InputFeedbackEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>回报一个输入的处理结果（覆盖该源名的上一次结果）。</summary>
    /// <param name="sourceName">输入源名（空 ⇒ 忽略，不抛）。</param>
    /// <param name="succeeded">是否成功。</param>
    /// <param name="message">可选原因。</param>
    public void Report(string sourceName, bool succeeded, string? message)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;   // 纪律：上报路径不抛
        }

        lock (_gate)
        {
            _entries[sourceName] = new InputFeedbackEntry(sourceName, succeeded, message);
        }
    }

    /// <summary>清除某个输入的结果（**框架在投递入队时调用** ⇒ "下一次提交即刷新"）。</summary>
    /// <param name="sourceName">输入源名。</param>
    public void Clear(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        lock (_gate)
        {
            _ = _entries.Remove(sourceName);
        }
    }

    /// <summary>清除全部结果（实例重置等）。</summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>取快照（只读，按源名排序以便稳定呈现）。</summary>
    public IReadOnlyList<InputFeedbackEntry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries.Values.OrderBy(e => e.SourceName, StringComparer.Ordinal)];
        }
    }
}
