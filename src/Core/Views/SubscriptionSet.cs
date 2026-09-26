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
///   <item><b>形态判定（04 §4.3 的交接形态表）</b>：数据源被**序列类元素**（A3 曲线 / A4 表格 / A5 日志 /
///     A6 字节视图 / A8 图像）引用 → 环形缓冲；否则 → 最新值槽（A1 数值 / A2 状态灯 / A7 仪表）；</item>
///   <item>数据源未被任何元素引用时，退回按其**类型**判定（<see cref="ViewDataSourceKind.Value"/> → 槽，其余 → 环）；</item>
///   <item><b>未声明的名字</b>：写入被丢弃并**只提示一次**（不刷屏）；</item>
///   <item><b>无 UI 模块</b>：声明为空 → 不建立任何缓冲（合法，§10.4）。</item>
/// </list>
/// </remarks>
public sealed class SubscriptionSet
{
    /// <summary>序列类元素（引用其数据源即用环形缓冲）。</summary>
    private static readonly HashSet<ViewElementKind> SeriesElementKinds =
    [
        ViewElementKind.Curve,
        ViewElementKind.Table,
        ViewElementKind.TextLog,
        ViewElementKind.HexView,
        ViewElementKind.Image,
    ];

    private readonly Dictionary<string, SubscriptionBuffer> _buffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownNames = new(StringComparer.Ordinal);

    /// <summary>按视图声明建立集合。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="view">视图声明（可为空 = 无 UI 模块）。</param>
    /// <param name="dataTypes">
    /// 该实例**声明的数据类型**（用于把数据源的 <c>Type</c> 引用解析成**解码器**）【ADR-0046 D2/D1 ③】。
    /// </param>
    /// <param name="ringCapacity">环形缓冲容量（样本数）。</param>
    public SubscriptionSet(
        string instanceId,
        ViewDeclaration? view,
        IReadOnlyList<DataTypeDeclaration>? dataTypes = null,
        int ringCapacity = SubscriptionBuffer.DefaultRingCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ringCapacity, 1);

        InstanceId = instanceId;

        if (view is null || view.DataSources.Count == 0)
        {
            return;
        }

        var seriesNames = CollectSeriesDataSources(view.Elements);

        foreach (var source in view.DataSources)
        {
            // 环形缓冲当且仅当：模块声明为非值类（序列/行集/字节块/文本/帧），
            // 或该数据源被序列类元素引用（04 §4.3 的交接形态表以元素类型为准）
            var isSeries = source.Kind != ViewDataSourceKind.Value || seriesNames.Contains(source.Name);

            var buffer = new SubscriptionBuffer(
                instanceId,
                source.Name,
                isSeries ? SubscriptionBufferForm.Ring : SubscriptionBufferForm.LatestSlot,
                isSeries ? ringCapacity : 1,
                source.ValueType,
                source.Kind)
            {
                // **解码器来自模块声明的类型**（框架不内建解码）：引用查得到才给句柄
                Decoder = ResolveDecoder(source, dataTypes),
                DataType = source.Type,
            };

            _buffers[source.Name] = buffer;
        }
    }

    /// <summary>把数据源的类型引用解析为**模块提供的解码器**（引用缺失/未声明 ⇒ <see langword="null"/>）。</summary>
    private static SampleDecoder? ResolveDecoder(
        ViewDataSource source,
        IReadOnlyList<DataTypeDeclaration>? dataTypes)
    {
        if (source.Type is not { } reference || dataTypes is null)
        {
            return null;
        }

        foreach (var type in dataTypes)
        {
            if (type.Reference == reference)
            {
                return type.Decoder;
            }
        }

        return null;
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
            failure = "数据源名为空（已丢弃）";
            return SubscriptionWriteOutcome.UnknownDataSource;
        }

        if (!_buffers.TryGetValue(dataSourceName, out var buffer))
        {
            failure = $"数据源未在视图声明中登记：'{dataSourceName}'（已丢弃）";

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

    private static HashSet<string> CollectSeriesDataSources(IReadOnlyList<ViewElement> elements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            Collect(element, names);
        }

        return names;
    }

    private static void Collect(ViewElement element, HashSet<string> names)
    {
        if (SeriesElementKinds.Contains(element.Kind) && element.DataSource is { Length: > 0 } name)
        {
            _ = names.Add(name);
        }

        foreach (var child in element.Children)
        {
            Collect(child, names);
        }
    }
}
