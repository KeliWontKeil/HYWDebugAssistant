using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Adapters;

/// <summary>
/// 外部接入失败的 Issue 口径：外部环境/对端状态一律是**警告**，不进入错误角标。
/// </summary>
/// <remarks>
/// 打开失败、连接被拒/超时、对端关闭、读写失败、关闭失败、超长单元都属于可重试/可恢复的
/// 运行状态；它们需要被看到，但不是“应用内部错误”。真正阻断模块装载/声明的错误仍由
/// 装配与生命周期路径以 Error 上报。
/// </remarks>
internal static class ExternalAccessIssue
{
    public static IssueCategory CategoryOf(AccessFailure failure) =>
        failure.Kind == AccessFailureKind.Queue
            ? IssueCategory.RuntimeError
            : IssueCategory.CommunicationError;

    public const IssueSeverity Severity = IssueSeverity.Warning;
}
