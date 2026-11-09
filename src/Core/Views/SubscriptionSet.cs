using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Core.Views;

/// <summary>订阅写入结果（决定是否向 Issue 中心提示）。</summary>
public enum SubscriptionWriteOutcome
{
    /// <summary>已写入。</summary>
    Written = 0,

    /// <summary>被丢弃，**应提示**（未声明名字首次出现 / 该缓冲首次丢弃）。</summary>
    UnknownDataSource = 1,

    /// <summary>被丢弃且**不提示**（重复出现的问题，已提示过）。</summary>
    Suppressed = 2,

    /// <summary>被丢弃且**应提示**（样本尺寸与已定槽尺寸不一致等）。</summary>
    Dropped = 3,
}

/// <summary>
/// 一个模块实例的**订阅缓冲集合**：按视图声明建立（每个数据源一个缓冲），供 UI 按刷新节拍读取。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>形态判定（04 §4.3 的交接形态表）</b>：数据源声明的**类别**定了形态 ——
///     <see cref="ViewDataSourceKind.Value"/> → 最新值槽（数值 / 状态灯 / 仪表），
///     其余（序列 / 行集 / 字节块 / 文本 / 帧）→ 环形缓冲；</item>
///   <item>形态只看**声明的类别**：界面元素种类不再参与判定（同一条判定散在"谁引用了它"里，
///     就会出现"换个控件显示就换了缓冲形态"这种看不出因果的行为）；</item>
///   <item><b>未声明的名字</b>：写入被丢弃并**只提示一次**（不刷屏）；</item>
///   <item><b>无 UI 模块</b>：声明为空 → 不建立任何缓冲（合法，§10.4）。</item>
/// </list>
/// </remarks>
public sealed class SubscriptionSet
{
    private readonly Dictionary<string, SubscriptionBuffer> _buffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownNames = new(StringComparer.Ordinal);

    /// <summary>按模块的**显示源声明**建立集合。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sources">显示源声明（空 = 这个模块没有显示源，属合法形态）。</param>
    /// <param name="ringCapacity">环形缓冲容量（样本数）。</param>
    public SubscriptionSet(
        string instanceId,
        IReadOnlyList<DisplaySourceDeclaration> sources,
        int ringCapacity = SubscriptionBuffer.DefaultRingCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ringCapacity, 1);
        ArgumentNullException.ThrowIfNull(sources);

        InstanceId = instanceId;

        if (sources.Count == 0)
        {
            return;
        }

        foreach (var source in sources)
        {
            // SeriesWindow 的完整数据走不可变快照槽（ViewStateStore），不占环形容量；
            // 这里保留一个 1 槽条目，让“源已声明”这一事实仍可被诊断面看到。
            var isByteRing = source.Kind is not (ViewDataSourceKind.Value or ViewDataSourceKind.SeriesWindow);

            _buffers[source.Name] = new SubscriptionBuffer(
                instanceId,
                source.Name,
                isByteRing ? SubscriptionBufferForm.Ring : SubscriptionBufferForm.LatestSlot,
                isByteRing ? ringCapacity : 1,
                source.ValueType,
                source.Kind);
        }
    }

    /// <summary>所属实例 ID。</summary>
    public string InstanceId { get; }

    /// <summary>全部订阅缓冲（按数据源名）。</summary>
    public IReadOnlyCollection<SubscriptionBuffer> Buffers => _buffers.Values;

    /// <summary>数据源数。</summary>
    public int Count => _buffers.Count;

    /// <summary>取某数据源的缓冲。</summary>
    public bool TryGet(string dataSourceName, out SubscriptionBuffer? buffer) =>
        _buffers.TryGetValue(dataSourceName, out buffer);

    /// <summary>
    /// 写入一个样本（模块线程调用）。
    /// </summary>
    /// <param name="dataSourceName">数据源名（须在视图声明中登记）。</param>
    /// <param name="value">样本字节。</param>
    /// <param name="failure">丢弃原因（成功时为 null）。</param>
    /// <returns>写入结果。</returns>
    public SubscriptionWriteOutcome TryWrite(string dataSourceName, ReadOnlySpan<byte> value, out string? failure)
    {
        failure = null;

        if (string.IsNullOrWhiteSpace(dataSourceName))
        {
            failure = CoreText.Get("core.views.sourceNameEmpty");
            return SubscriptionWriteOutcome.UnknownDataSource;
        }

        if (!_buffers.TryGetValue(dataSourceName, out var buffer))
        {
            failure = CoreText.Get("core.views.sourceNotDeclared", dataSourceName);

            // **只提示一次**：未声明的名字反复出现不刷屏
            return _unknownNames.Add(dataSourceName)
                ? SubscriptionWriteOutcome.UnknownDataSource
                : SubscriptionWriteOutcome.Suppressed;
        }

        if (buffer.Write(value, out failure))
        {
            return SubscriptionWriteOutcome.Written;
        }

        // 同一缓冲的丢弃**只报首次**（尺寸持续不匹配时不刷屏）
        return buffer.Dropped == 1 ? SubscriptionWriteOutcome.Dropped : SubscriptionWriteOutcome.Suppressed;
    }
}
