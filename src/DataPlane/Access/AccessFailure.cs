namespace HYWDebugAssistant.DataPlane.Access;

/// <summary>外部接入失败的环节（用于 Issue 文案与测试归类）。</summary>
public enum AccessFailureKind
{
    /// <summary>打开会话失败。</summary>
    Open = 0,

    /// <summary>关闭会话失败。</summary>
    Close = 1,

    /// <summary>写入失败。</summary>
    Write = 2,

    /// <summary>适配件报告的状态/读写异常。</summary>
    Adapter = 3,

    /// <summary>数据单元超长 / 目标缓冲不足等数据面问题。</summary>
    Queue = 4,
}

/// <summary>
/// 一次外部接入失败（数据面记录，L4 负责转 Issue）。
/// </summary>
/// <param name="AccessName">接入名（归属到会话）。</param>
/// <param name="Kind">失败环节。</param>
/// <param name="Message">面向用户的具体原因。</param>
public sealed record AccessFailure(string AccessName, AccessFailureKind Kind, string Message);
