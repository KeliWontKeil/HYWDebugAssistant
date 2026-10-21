using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **"未接线"的接入面**：所有请求都被忽略、读永远无数据、写永远失败、快照为空。
/// </summary>
/// <remarks>
/// 用于 <see cref="Context.IModuleContext.Access"/> 的默认实现、测试替身与"宿主尚未接线"的降级路径——
/// 模块不该因为框架没接线而崩。它不是"外部接入已关闭"：真正的关闭态由框架的快照表达。
/// </remarks>
public sealed class ExternalAccessDefaults : IExternalAccess
{
    /// <summary>单例（无状态）。</summary>
    public static readonly ExternalAccessDefaults Empty = new();

    private ExternalAccessDefaults()
    {
    }

    /// <inheritdoc />
    public bool RequestOpen(string accessName) => false;

    /// <inheritdoc />
    public bool RequestClose(string accessName) => false;

    /// <inheritdoc />
    public bool TryRead(string accessName, Span<byte> destination, out int length)
    {
        _ = accessName;
        _ = destination;
        length = 0;
        return false;
    }

    /// <inheritdoc />
    public void SetReceiveHandler(string accessName, ReceiveHandler handler)
    {
        _ = accessName;
        _ = handler;
    }

    /// <inheritdoc />
    public bool Write(string accessName, ReadOnlySpan<byte> payload)
    {
        _ = accessName;
        _ = payload;
        return false;
    }

    /// <inheritdoc />
    public ExternalAccessSnapshot GetSnapshot(string accessName)
    {
        _ = accessName;
        return ExternalAccessSnapshot.None;
    }
}
