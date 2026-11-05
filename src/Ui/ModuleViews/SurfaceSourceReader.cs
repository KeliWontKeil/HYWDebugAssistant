using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Views;


namespace HYWDebugAssistant.Ui.ModuleViews;

/// <summary>
/// **订阅数据源的取数原语** —— 表面层与模块之间唯一的数据通道。
/// </summary>
/// <remarks>
/// <para>
/// 它不是"旧控件的实现"，是**框架原语**：把"一个声明过的数据源名"翻译成"一个样本 / 一串样本 /
/// 一块显示缓冲"。解码口径（模块注册的解码器优先、原语类型回退、两者都空则不显示）在这里收口一次，
/// 任何呈现这一份数据的控件都走它 ⇒ 同一个源的显示不会因为控件不同而算出不同的数。
/// </para>
/// <para>
/// **滚动窗口的读口径**（<see cref="_seriesScratch"/>）都在这里，不在各个控件里：
/// 一个控件不该需要知道"一次能读几个样本"。
/// </para>
/// <para>
/// 每帧开头调一次 <see cref="RefreshSources"/> 刷新数据源快照（数据源是**每帧重建**的视图对象，
/// 不能跨帧缓存），之后的 <see cref="ReadLatest"/> / <see cref="ReadSeries"/> 都按名字在快照里查。
/// </para>
/// </remarks>
internal sealed class SurfaceSourceReader
{
    /// <summary>曲线最多绘制的点数（超出即在 UI 侧等间隔降采样，不插值）。</summary>
    internal const int MaxSeriesPoints = 256;

    /// <summary>单样本字节数上限（够放下一个双精度值）。</summary>
    private const int MaxSampleSize = sizeof(double);

    /// <summary>最新值读取的落点（比单样本上限宽，容得下"源自己报了个更宽的尺寸"的拒绝判据）。</summary>
    private const int SampleScratchSize = 1024;

    /// <summary>一次曲线读取的落点（够 <see cref="MaxSeriesPoints"/> 个双精度样本）。</summary>
    private const int SeriesScratchSize = MaxSeriesPoints * MaxSampleSize;

    private readonly IRuntimeQuery _query;
    private readonly IViewStateAccess? _viewState;
    private readonly byte[] _sampleScratch = new byte[SampleScratchSize];
    private readonly byte[] _seriesScratch = new byte[SeriesScratchSize];
    private readonly double[] _seriesValues = new double[MaxSeriesPoints];
    private readonly Dictionary<(string InstanceId, string Source), SubscriptionSourceView> _sources = [];

    /// <summary>最新值缓存（同一个 <c>Written</c> ⇒ 样本没有变化，直接复用解码结果）。</summary>
    private readonly Dictionary<(string InstanceId, string Source), (long Written, double? Value)> _latestCache = [];

    /// <summary>曲线缓存（同一个 <c>Written</c> + 取样上限 ⇒ 结果没有变化）。</summary>
    private readonly Dictionary<(string InstanceId, string Source, int MaxSamples), (long Written, IReadOnlyList<double> Values)> _seriesCache = [];

    /// <summary>显示缓冲的最近一次快照（同一个变更号 ⇒ 直接复用，不再复制行表）。</summary>
    private readonly Dictionary<(string InstanceId, string Source), BufferSnapshot> _bufferCache = [];

    /// <summary>造一个取数器。</summary>
    /// <param name="query">运行期查询面（唯一的取数入口）。</param>
    /// <param name="viewState">
    /// 视图状态存取面（显示缓冲在这里）。<see langword="null"/> = 设计期 / 未接线的调用点 ⇒
    /// 显示缓冲一律读成空（**不假造一个本地缓冲**：那样界面会显示出一条谁也没写过的日志）。
    /// </param>
    internal SurfaceSourceReader(IRuntimeQuery query, IViewStateAccess? viewState)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
        _viewState = viewState;
    }

    /// <summary>
    /// 重建数据源快照（每帧一次；数据源视图对象是每帧重建的，不能跨帧持有）。
    /// </summary>
    /// <param name="sources">本帧的订阅源快照（由窗口集统一取一次；<see langword="null"/> = 自己取）。</param>
    internal void RefreshSources(IReadOnlyCollection<SubscriptionSourceView>? sources = null)
    {
        _sources.Clear();

        foreach (var source in sources ?? _query.GetSubscriptionSources())
        {
            _sources[(source.InstanceId, source.DataSourceName)] = source;
        }
    }

    /// <summary>读某个源的**最新值**。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">数据源名。</param>
    /// <returns>解出的数值；没有源 / 尺寸不合 / 解不出 ⇒ <see langword="null"/>（界面显示"—"）。</returns>
    internal double? ReadLatest(string instanceId, string sourceName)
    {
        if (!TrySource(instanceId, sourceName, out var source))
        {
            return null;
        }

        var key = (instanceId, sourceName);

        if (_latestCache.TryGetValue(key, out var cached) && cached.Written == source.Written)
        {
            return cached.Value;
        }

        var sampleSize = SampleSizeOf(source);

        if (sampleSize <= 0 || sampleSize > _sampleScratch.Length)
        {
            _latestCache[key] = (source.Written, null);
            return null;
        }

        var value = _query.TryReadLatest(source.InstanceId, source.DataSourceName, _sampleScratch, out var length)
            && length == sampleSize
            && Decode(source, _sampleScratch.AsSpan(0, length), out var decoded)
                ? decoded
                : (double?)null;

        _latestCache[key] = (source.Written, value);
        return value;
    }

    /// <summary>读某个源的**滚动窗口**（时间升序；超出上限即等间隔降采样，不插值）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">数据源名。</param>
    /// <returns>窗口内的点；没源 / 尺寸不合 ⇒ 空表。</returns>
    internal IReadOnlyList<double> ReadSeries(string instanceId, string sourceName)
    {
        if (!TrySource(instanceId, sourceName, out var source))
        {
            return [];
        }

        var maxSamples = MaxSeriesPoints;
        var key = (instanceId, sourceName, maxSamples);

        if (_seriesCache.TryGetValue(key, out var cached) && cached.Written == source.Written)
        {
            return cached.Values;
        }

        var sampleSize = SampleSizeOf(source);

        if (sampleSize <= 0 || sampleSize > MaxSampleSize)
        {
            _seriesCache[key] = (source.Written, []);
            return [];
        }

        var sampleCapacity = Math.Min(MaxSeriesPoints, _seriesScratch.Length / sampleSize);
        var count = _query.ReadSeries(source.InstanceId, source.DataSourceName, _seriesScratch, sampleCapacity);

        if (count <= 0)
        {
            _seriesCache[key] = (source.Written, []);
            return [];
        }

        var points = new List<double>(count);

        for (var i = 0; i < count; i++)
        {
            if (!Decode(source, _seriesScratch.AsSpan(i * sampleSize, sampleSize), out var value))
            {
                break;
            }

            points.Add(value);
        }

        IReadOnlyList<double> result;

        if (points.Count <= MaxSeriesPoints)
        {
            result = points;
        }
        else
        {
            var step = (double)points.Count / MaxSeriesPoints;

            for (var i = 0; i < MaxSeriesPoints; i++)
            {
                _seriesValues[i] = points[(int)(i * step)];
            }

            result = _seriesValues.ToArray();
        }

        _seriesCache[key] = (source.Written, result);
        return result;
    }

    /// <summary>
    /// 取某个**显示缓冲**自游标以来的窗口（每帧热路径）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <param name="cursor">上一拍读到的位置。</param>
    /// <param name="sealedLines">接收新增已封口行的缓冲（调用方持有）。</param>
    /// <returns>窗口；没有视图状态（设计期/未接线）时为重置窗口。</returns>
    internal TextWindow ReadWindow(
        string instanceId,
        string sourceName,
        in TextCursor cursor,
        List<string> sealedLines,
        int maxChars = 0)
    {
        if (sourceName is not { Length: > 0 } || _viewState is null)
        {
            sealedLines.Clear();
            return TextWindow.ResetRequired(cursor, 0);
        }

        return _viewState.Display(instanceId, sourceName).ReadWindow(cursor, sealedLines, maxChars);
    }

    /// <summary>取某显示缓冲的变更号。</summary>
    internal long ReadRevision(string instanceId, string sourceName) =>
        sourceName is { Length: > 0 } && _viewState is not null
            ? _viewState.Display(instanceId, sourceName).Revision
            : 0;

    /// <summary>
    /// 读某个**显示缓冲**的当前全量行。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">视图绑定的显示源名。</param>
    /// <returns>快照 + 丢弃计数 + 世代号（从没被清空过 ⇒ 世代号 0）。</returns>
    /// <remarks>
    /// 显示缓冲由**框架持有**、由模块的**实例线程直接写**；UI 在这里只读。
    /// 世代号在缓冲被清空时递增 —— 界面据此知道"手里那份副本已经过期"。
    /// </remarks>
    internal BufferSnapshot ReadBuffer(string instanceId, string sourceName)
    {
        if (sourceName is not { Length: > 0 } || _viewState is null)
        {
            return BufferSnapshot.Empty;
        }

        var buffer = _viewState.Display(instanceId, sourceName);
        var key = (instanceId, sourceName);
        var revision = buffer.Revision;

        // **未变就不复制**：每个显示面每帧都会来读；同一个变更号直接复用上一份快照。
        // 变更时才 `Snapshot()`（复制行表）；行字符串本身共享，复制的是引用。
        if (_bufferCache.TryGetValue(key, out var cached) && cached.Revision == revision)
        {
            return cached;
        }

        var snapshot = new BufferSnapshot(
            buffer.Snapshot(), buffer.DroppedCount, buffer.Capacity, revision);

        _bufferCache[key] = snapshot;
        return snapshot;
    }

    /// <summary>
    /// 读某显示缓冲的**尾部窗口**（呈现超预算时跳读用）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <param name="maxLines">最多取多少行。</param>
    /// <param name="cursor">尾部对应的游标（后续增量从这之后开始）。</param>
    /// <returns>尾部快照（只含尾部行）。</returns>
    internal BufferSnapshot ReadTail(
        string instanceId,
        string sourceName,
        int maxLines,
        int maxChars,
        out TextCursor cursor)
    {
        if (sourceName is not { Length: > 0 } || _viewState is null)
        {
            cursor = TextCursor.Empty;
            return BufferSnapshot.Empty;
        }

        var buffer = _viewState.Display(instanceId, sourceName);
        var lines = buffer.ReadTail(maxLines, maxChars, out cursor);

        return new BufferSnapshot(lines, buffer.DroppedCount, buffer.Capacity, buffer.Revision);
    }

    /// <summary>按名字取数据源（不在本帧快照里 ⇒ <see langword="false"/>）。</summary>
    private bool TrySource(string instanceId, string sourceName, out SubscriptionSourceView source)
    {
        if (sourceName is { Length: > 0 }
            && _sources.TryGetValue((instanceId, sourceName), out var found))
        {
            source = found;
            return true;
        }

        source = default;
        return false;
    }

    /// <summary>单样本字节数（源自己报过就用它，否则按契约原语算）。</summary>
    private static int SampleSizeOf(SubscriptionSourceView source) =>
        source.SampleSizeInBytes > 0
            ? source.SampleSizeInBytes
            : source.ValueType is { } primitive ? PrimitiveSampleDecoder.SizeOf(primitive) : 0;

    /// <summary>
    /// 解码一个样本：**模块注册的解码器优先**（框架只回调）；未提供时按**参考实现**回退
    /// （兼容形态——不猜类型，原语未声明即失败）。
    /// </summary>
    private static bool Decode(SubscriptionSourceView source, ReadOnlySpan<byte> sample, out double value)
    {
        if (source.Decoder is { } decoder)
        {
            return decoder(sample, out value);
        }

        if (source.ValueType is { } primitive)
        {
            return PrimitiveSampleDecoder.TryDecode(primitive, sample, out value);
        }

        value = 0;
        return false;
    }
}
