namespace HYWDebugAssistant.Core.Changes;

/// <summary>
/// 变更集：一次提交的**操作序列**——整体校验 → 一次性应用与增量重建 → 任一步失败整体拒绝（§6.2）。
/// </summary>
/// <remarks>
/// 无全局事务/锁：并发变更由**变更入口串行化**（运行时内的单一闸门），不与数据面争锁。
/// </remarks>
public sealed class ChangeSet
{
    /// <summary>创建变更集。</summary>
    public ChangeSet(IReadOnlyList<ChangeOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations;
    }

    /// <summary>操作序列（按提交顺序）。</summary>
    public IReadOnlyList<ChangeOperation> Operations { get; }

    /// <summary>是否包含任何操作。</summary>
    public bool IsEmpty => Operations.Count == 0;

    /// <summary>构造变更集。</summary>
    public static ChangeSet Of(params ChangeOperation[] operations) => new(operations);

    /// <summary>仅含一个操作的变更集。</summary>
    public static ChangeSet OfOne(ChangeOperation operation) => new([operation]);
}

/// <summary>变更应用结果。</summary>
/// <param name="Applied">是否已应用（false = 整体拒绝，**零副作用**）。</param>
/// <param name="Failures">拒绝原因（面向用户）。</param>
/// <param name="Notices">应用过程中的提示（如自动建网的歧义提示）。</param>
public readonly record struct ChangeSetResult(
    bool Applied,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Notices)
{
    /// <summary>成功。</summary>
    public static ChangeSetResult Success(IReadOnlyList<string>? notices = null) => new(true, [], notices ?? []);

    /// <summary>整体拒绝（Prepare 阶段失败，未产生任何副作用）。</summary>
    public static ChangeSetResult Rejected(IReadOnlyList<string> failures) => new(false, failures, []);
}
