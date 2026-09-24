using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Core.Views;

/// <summary>订阅缓冲形态（04 §4.3）：单值/状态 = 最新值槽；流数据 = 环形缓冲（满则丢旧）。</summary>
public enum SubscriptionBufferForm
{
    /// <summary>最新值槽（容量 1；UI 只关心当前值）。</summary>
    LatestSlot = 0,

    /// <summary>环形缓冲（有界，满则丢旧；供曲线/表格/日志等序列呈现）。</summary>
    Ring = 1,
}

/// <summary>
/// 单个订阅缓冲：**模块线程写、UI 线程读**（Q4-4）——写不阻塞、读不阻塞，稳态零分配。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>单写者</b>：一个实例的订阅写入只发生在其专属线程上（活动体或投递回调），故写侧无需互斥；</item>
///   <item><b>无锁读</b>：以**序号锁（seqlock）**做乐观读——写侧先置"写入中"，写完后复原；
///     读侧发现版本变化即**本次放弃**（返回"暂无数据"），**绝不等待、绝不阻塞写者**（§13.1 "UI 慢永不阻塞模块"）；</item>
///   <item><b>丢旧</b>：环形满时覆盖最旧样本并计数（<see cref="Overwritten"/> = 被覆盖的样本数，
///     即"UI 读取是否跟得上"的事实指标，计入 L4 诊断）；</item>
///   <item><b>槽尺寸</b>：首次写入时确定（= 首次样本长度），此后**更大的样本被丢弃并计数**——
///     稳态零分配，且不需要模块预先声明字节数。</item>
/// </list>
/// </remarks>
public sealed class SubscriptionBuffer
{
    /// <summary>环形缓冲默认容量（样本数；M1-9 取值，实测后可调）。</summary>
    public const int DefaultRingCapacity = 512;

    /// <summary>单样本字节数上限（护栏：拒绝畸形/过大样本，不做静默截断）。</summary>
    public const int MaxSampleSizeInBytes = 1024;

    /// <summary>读侧乐观重试上限（超出即本次放弃，下次刷新节拍再来）。</summary>
    private const int MaxReadAttempts = 8;

    private readonly Lock _allocationGate = new();

    private byte[]? _storage;
    private int _slotSize;
    private long _version;
    private int _head;
    private int _count;
    private long _written;
    private long _overwritten;
    private long _dropped;

    /// <summary>创建订阅缓冲。</summary>
    /// <param name="instanceId">所属实例 ID。</param>
    /// <param name="dataSourceName">数据源名（模块声明）。</param>
    /// <param name="form">缓冲形态。</param>
    /// <param name="capacity">容量（样本数；最新值槽恒为 1）。</param>
    /// <param name="valueType">单样本契约原语（可为空 = 非数值）。</param>
    /// <param name="kind">数据源类型（声明值；决定 UI 呈现方式）。</param>
    public SubscriptionBuffer(
        string instanceId,
        string dataSourceName,
        SubscriptionBufferForm form,
        int capacity,
        ContractPrimitive? valueType = null,
        ViewDataSourceKind kind = ViewDataSourceKind.Value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceName);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        InstanceId = instanceId;
        DataSourceName = dataSourceName;
        Form = form;
        Capacity = form == SubscriptionBufferForm.LatestSlot ? 1 : capacity;
        ValueType = valueType;
        Kind = kind;
    }

    /// <summary>所属实例 ID。</summary>
    public string InstanceId { get; }

    /// <summary>数据源名。</summary>
    public string DataSourceName { get; }

    /// <summary>缓冲形态。</summary>
    public SubscriptionBufferForm Form { get; }

    /// <summary>容量（样本数）。</summary>
    public int Capacity { get; }

    /// <summary>单样本契约原语（可为空）。</summary>
    public ContractPrimitive? ValueType { get; }

    /// <summary>数据源类型（模块声明值）。</summary>
    public ViewDataSourceKind Kind { get; }

    /// <summary>样本字节数（尚未写入过时为 0）。</summary>
    public int SampleSizeInBytes => Volatile.Read(ref _slotSize);

    /// <summary>累计写入的样本数。</summary>
    public long Written => Interlocked.Read(ref _written);

    /// <summary>被新样本覆盖掉的旧样本数（环形满时递增；最新值槽恒为 0）。</summary>
    public long Overwritten => Interlocked.Read(ref _overwritten);

    /// <summary>被丢弃的样本数（尺寸超过已定槽尺寸 / 超过上限）。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>当前有效样本数（≤ 容量）。</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// 写入一个样本（**模块线程**）。
    /// </summary>
    /// <returns>是否写入（false = 被丢弃，原因见 <paramref name="failure"/>）。</returns>
    public bool Write(ReadOnlySpan<byte> value, out string? failure)
    {
        failure = null;

        if (value.Length == 0)
        {
            _ = Interlocked.Increment(ref _dropped);
            failure = "空样本（已丢弃）";
            return false;
        }

        if (value.Length > MaxSampleSizeInBytes)
        {
            _ = Interlocked.Increment(ref _dropped);
            failure = $"样本过大：{value.Length} 字节（上限 {MaxSampleSizeInBytes} 字节）";
            return false;
        }

        var slotSize = Volatile.Read(ref _slotSize);
        if (slotSize == 0)
        {
            slotSize = Allocate(value.Length);
        }

        if (value.Length > slotSize)
        {
            _ = Interlocked.Increment(ref _dropped);
            failure = $"样本长度 {value.Length} 超过已定槽尺寸 {slotSize}（已丢弃；同一数据源的样本长度必须一致）";
            return false;
        }

        var storage = Volatile.Read(ref _storage)!;

        // 序号锁：变奇 = 写入中
        _ = Interlocked.Increment(ref _version);

        var index = _head;
        var target = storage.AsSpan(index * slotSize, slotSize);
        value.CopyTo(target);

        if (value.Length < slotSize)
        {
            // 尾部清零：避免读到上一次的残留字节
            target[value.Length..].Clear();
        }

        _head = index + 1 == Capacity ? 0 : index + 1;
        var count = Volatile.Read(ref _count);
        if (count < Capacity)
        {
            Volatile.Write(ref _count, count + 1);
        }
        else if (Form == SubscriptionBufferForm.Ring)
        {
            _ = Interlocked.Increment(ref _overwritten);
        }

        _ = Interlocked.Increment(ref _written);
        _ = Interlocked.Increment(ref _version);   // 复原为偶：读侧可安全读取
        return true;
    }

    /// <summary>读**最新**样本（UI 线程；无数据或抖动过大时返回 false，**不等待**）。</summary>
    /// <param name="destination">目标缓冲（至少 <see cref="SampleSizeInBytes"/> 字节）。</param>
    /// <param name="length">读到的字节数。</param>
    public bool TryReadLatest(Span<byte> destination, out int length)
    {
        length = 0;

        var slotSize = Volatile.Read(ref _slotSize);
        if (slotSize == 0 || destination.Length < slotSize)
        {
            return false;
        }

        for (var attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            var version = Volatile.Read(ref _version);
            if ((version & 1) != 0)
            {
                continue;   // 正在写入：不等待，直接重试
            }

            var count = Volatile.Read(ref _count);
            if (count == 0)
            {
                return false;
            }

            var head = Volatile.Read(ref _head);
            var latest = count < Capacity ? count - 1 : head == 0 ? Capacity - 1 : head - 1;

            var storage = Volatile.Read(ref _storage)!;
            storage.AsSpan(latest * slotSize, slotSize).CopyTo(destination);

            if (Volatile.Read(ref _version) == version)
            {
                length = slotSize;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 读**序列快照**（按时间升序，最多 <paramref name="maxSamples"/> 个；用于曲线等）。
    /// </summary>
    /// <remarks>抽样/降采样在 UI 侧做（§4.3）：本方法只按"最近 N 个"截取，不做插值。</remarks>
    /// <param name="destination">目标缓冲（至少 <c>maxSamples × SampleSizeInBytes</c> 字节）。</param>
    /// <param name="maxSamples">最多读取的样本数。</param>
    /// <returns>实际读到的样本数。</returns>
    public int ReadSeries(Span<byte> destination, int maxSamples)
    {
        var slotSize = Volatile.Read(ref _slotSize);
        if (slotSize == 0 || maxSamples <= 0)
        {
            return 0;
        }

        var capacity = Math.Min(maxSamples, destination.Length / slotSize);
        if (capacity <= 0)
        {
            return 0;
        }

        for (var attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            var version = Volatile.Read(ref _version);
            if ((version & 1) != 0)
            {
                continue;
            }

            var count = Volatile.Read(ref _count);
            if (count == 0)
            {
                return 0;
            }

            var take = Math.Min(count, capacity);
            var head = Volatile.Read(ref _head);
            var storage = Volatile.Read(ref _storage)!;

            // 时间升序：未满时样本在 [0, count)，满时最旧样本位于 head（下一个写入位置）
            var start = count < Capacity
                ? count - take
                : (head + Capacity - take) % Capacity;

            for (var i = 0; i < take; i++)
            {
                var index = start + i;
                if (index >= Capacity)
                {
                    index -= Capacity;
                }

                storage.AsSpan(index * slotSize, slotSize).CopyTo(destination[(i * slotSize)..]);
            }

            if (Volatile.Read(ref _version) == version)
            {
                return take;
            }
        }

        return 0;
    }

    private int Allocate(int slotSize)
    {
        lock (_allocationGate)
        {
            if (_slotSize != 0)
            {
                return _slotSize;
            }

            _storage = new byte[slotSize * Capacity];
            Volatile.Write(ref _slotSize, slotSize);
            return slotSize;
        }
    }
}
