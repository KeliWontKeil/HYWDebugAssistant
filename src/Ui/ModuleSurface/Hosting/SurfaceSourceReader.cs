using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;

namespace HYWDebugAssistant.Ui.ModuleSurface.Hosting;

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
    private readonly Dictionary<(string InstanceId, string Source), long> _bufferSequences = [];

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

    /// <summary>重建数据源快照（每帧一次；数据源视图对象是每帧重建的，不能跨帧持有）。</summary>
    internal void RefreshSources()
    {
        _sources.Clear();

        foreach (var source in _query.GetSubscriptionSources())
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

        var sampleSize = SampleSizeOf(source);

        if (sampleSize <= 0 || sampleSize > _sampleScratch.Length)
        {
            return null;
        }

        return _query.TryReadLatest(source.InstanceId, source.DataSourceName, _sampleScratch, out var length)
            && length == sampleSize
            && Decode(source, _sampleScratch.AsSpan(0, length), out var decoded)
                ? decoded
                : null;
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

        var sampleSize = SampleSizeOf(source);

        if (sampleSize <= 0 || sampleSize > MaxSampleSize)
        {
            return [];
        }

        var maxSamples = Math.Min(MaxSeriesPoints, _seriesScratch.Length / sampleSize);
        var count = _query.ReadSeries(source.InstanceId, source.DataSourceName, _seriesScratch, maxSamples);

        if (count <= 0)
        {
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

        if (points.Count <= MaxSeriesPoints)
        {
            return points;
        }

        var step = (double)points.Count / MaxSeriesPoints;

        for (var i = 0; i < MaxSeriesPoints; i++)
        {
            _seriesValues[i] = points[(int)(i * step)];
        }

        return _seriesValues.ToArray();
    }

    /// <summary>
    /// 读某个**显示缓冲**的当前全量行。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">声明面的 <c>data</c> 取值。</param>
    /// <returns>快照 + 丢弃计数 + 世代号（从没被清空过 ⇒ 世代号 0）。</returns>
    /// <remarks>
    /// 显示缓冲是 <b>UI 侧的、模块只写的</b>一份状态：模块经 <c>IViewDisplayAccess</c> 入队，
    /// <c>ModuleWindowsViewModel</c> 在刷新节拍里落地，这里只读。世代号在缓冲被清空时递增 ——
    /// 界面据此知道"手里那份副本已经过期"。
    /// </remarks>
    internal SurfaceBufferView ReadBuffer(string instanceId, string sourceName)
    {
        if (sourceName is not { Length: > 0 } || _viewState is null)
        {
            return SurfaceBufferView.Empty;
        }

        var buffer = _viewState.Display(instanceId, sourceName);
        var key = (instanceId, sourceName);
        var sequence = _bufferSequences.TryGetValue(key, out var value) ? value : 0L;

        return new SurfaceBufferView(
            buffer.Snapshot(), buffer.DroppedCount, buffer.Capacity, new SurfaceBufferSequence(sequence));
    }

    /// <summary>
    /// 记下"某个显示缓冲刚被清空"（世代号递增 ⇒ 界面手里的副本作废）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="sourceName">显示源名。</param>
    /// <remarks>
    /// 由落地清空请求的那一方（<c>ModuleSurfaceHost</c>）在 <c>Display(...).Clear()</c> 之后调用 ——
    /// <b>清空的落地仍然只有一处</b>，这里只维护"版本"这件事。
    /// </remarks>
    internal void ForgetBuffer(string instanceId, string sourceName)
    {
        var key = (instanceId, sourceName);

        _bufferSequences[key] = (_bufferSequences.TryGetValue(key, out var value) ? value : 0L) + 1;
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

        source = null!;
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
