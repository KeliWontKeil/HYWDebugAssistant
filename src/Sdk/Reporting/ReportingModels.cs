namespace HYWDebugAssistant.Sdk.Reporting;

/// <summary>
/// Issue 类别（七类）——主文档 §8.2。框架管理**系统级**故障，不理解具体业务语义。
/// </summary>
public enum IssueCategory
{
    /// <summary>配置错误（配置项缺失、类型不符、取值非法）。</summary>
    ConfigurationError = 0,

    /// <summary>组合错误（组合声明本身不成立）。</summary>
    CompositionError = 1,

    /// <summary>接口错误（口缺失、方向不匹配、契约不兼容）。</summary>
    InterfaceError = 2,

    /// <summary>模块错误（模块自身实现问题，如未在期限内停止）。</summary>
    ModuleError = 3,

    /// <summary>通信错误（链路建立/维持失败）。</summary>
    CommunicationError = 4,

    /// <summary>运行时错误（框架运行期问题）。</summary>
    RuntimeError = 5,

    /// <summary>呈现错误（视图渲染相关问题）。</summary>
    PresentationError = 6,
}

/// <summary>
/// 问题来源：框架判定 / 模块报告（主文档 §8.5 PQ-ER-01）。
/// </summary>
public enum IssueSource
{
    /// <summary>由框架判定（校验、兼容、超时等）。</summary>
    Framework = 0,

    /// <summary>由模块主动报告。</summary>
    Module = 1,
}

/// <summary>
/// 问题严重度（仅影响呈现的打扰度，**不阻断**其他部分运行）。
/// </summary>
public enum IssueSeverity
{
    /// <summary>信息（仅日志/诊断层）。</summary>
    Info = 0,

    /// <summary>
    /// 注意（轻提示：对象角标 + 诊断层）。外部环境/对端状态（如 TCP 对端关闭、连接超时、
    /// 串口不可用）与可恢复的运行失败用这一档，不进入错误角标。
    /// </summary>
    Warning = 1,

    /// <summary>
    /// 错误（Issue 面板 + 对象标注；**不弹模态框**）。只用于框架内部故障、契约/声明错误、
    /// 持久化损坏等真正需要红色角标的问题；不是“任何操作失败”都归这一档。
    /// </summary>
    Error = 2,
}

/// <summary>
/// 模块日志级别（分级 + 来源；默认轻量不打扰——主文档 §13.2 L3）。
/// </summary>
public enum ModuleLogLevel
{
    /// <summary>跟踪（仅在诊断开关打开时写入）。</summary>
    Trace = 0,

    /// <summary>调试（仅在诊断开关打开时写入）。</summary>
    Debug = 1,

    /// <summary>信息。</summary>
    Information = 2,

    /// <summary>警告。</summary>
    Warning = 3,

    /// <summary>错误。</summary>
    Error = 4,
}

/// <summary>
/// 模块向上报告的问题（归属对象由框架补齐为本实例）。
/// </summary>
/// <param name="Category">问题类别（七类）。</param>
/// <param name="Severity">严重度（影响呈现打扰度）。</param>
/// <param name="Message">面向用户的描述（**事实 + 影响 + 建议**，不出错误码——docs/architecture/10 §3.2）。</param>
/// <param name="ObjectPath">可选的更细归属（如口名、配置项名）；为空表示归属整个实例。</param>
/// <param name="Source">来源（框架判定 / 模块报告）；**模块侧无需填写**——框架在模块上报路径上自动置为 <see cref="IssueSource.Module"/>。</param>
public sealed record IssueReport(
    IssueCategory Category,
    IssueSeverity Severity,
    string Message,
    string? ObjectPath = null,
    IssueSource Source = IssueSource.Framework);

/// <summary>
/// 模块的业务状态上报（框架只登记/呈现、**不解释语义**——主文档 §8.4）。
/// </summary>
/// <param name="StateKey">状态名（模块自定义，如 <c>link</c>）。</param>
/// <param name="StateValue">状态值（模块自定义，如 <c>online</c>）。</param>
/// <param name="DisplayText">可选的人类可读文本。</param>
public sealed record BusinessState(
    string StateKey,
    string StateValue,
    string? DisplayText = null);

/// <summary>Issue 上报入口。</summary>
/// <remarks>实现**不抛出**：上报失败只记诊断计数（模块不应因上报失败而崩）。</remarks>
public interface IIssueReporter
{
    /// <summary>上报一个问题。</summary>
    void Report(IssueReport issue);
}

/// <summary>日志上报入口（经框架汇集到 L3；热路径**不逐单元打日志**，应计数 + 定时汇总）。</summary>
public interface IModuleLogger
{
    /// <summary>是否启用了该级别（用于避免昂贵的日志参数构造）。</summary>
    bool IsEnabled(ModuleLogLevel level);

    /// <summary>写一条日志。</summary>
    void Log(ModuleLogLevel level, string message);
}

/// <summary>业务状态上报入口。</summary>
public interface IBusinessStateReporter
{
    /// <summary>更新一个业务状态项（框架只登记/呈现）。</summary>
    void Update(BusinessState state);
}
