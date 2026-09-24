using HYWDebugAssistant.Sdk.Data;

namespace HYWDebugAssistant.DataPlane.Queues;

/// <summary>队列满时的丢弃方式（由模块口声明；默认丢旧）。</summary>
public enum DropPolicy
{
    /// <summary>丢最旧保最新（实时场景默认）。</summary>
    DropOldest = 0,

    /// <summary>丢最新（保留已有积压）。</summary>
    DropNewest = 1,
}

/// <summary>
/// 入站数据单元：缓冲 + **目标口名**。
/// </summary>
/// <remarks>
/// 队列是**每模块实例一个**（ADR-0029 §4.2），因此元素必须携带目标口名以便路由到对应接收处理。
/// </remarks>
/// <param name="Buffer">数据单元缓冲（所有权随入队移交给队列/消费方）。</param>
/// <param name="TargetPort">目标接收口名。</param>
public readonly record struct InboundUnit(PayloadBuffer Buffer, string TargetPort);

/// <summary>
/// 有界输入队列：**绝不阻塞发送方**，满时按策略丢弃并计数（ADR-0012/ADR-0029 §4.2）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>生产者：任意线程（投递方）；消费者：该实例的专属线程。</item>
///   <item>丢弃的缓冲**由调用方归还池**（本类不持有池，避免锁内调用外部代码）。</item>
///   <item>v1 用锁保证正确性；无锁/批量优化属 R 项（ADR-0029 §4.9）。</item>
/// </list>
/// </remarks>
public sealed class InboundBuffer
{
    private readonly Queue<InboundUnit> _queue;
    private readonly Lock _gate = new();
    private long _enqueued;
    private long _dropped;

    /// <summary>创建有界队列。</summary>
    /// <param name="capacity">容量（单元数）。</param>
    /// <param name="policy">满时丢弃方式。</param>
    public InboundBuffer(int capacity, DropPolicy policy = DropPolicy.DropOldest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        Policy = policy;
        _queue = new Queue<InboundUnit>(capacity);
    }

    /// <summary>容量（单元数）。</summary>
    public int Capacity { get; }

    /// <summary>满时丢弃方式。</summary>
    public DropPolicy Policy { get; }

    /// <summary>当前深度。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>累计入队数（含被后续丢弃的）。</summary>
    public long Enqueued => Interlocked.Read(ref _enqueued);

    /// <summary>累计丢弃数。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>入队；满时按策略丢弃。丢弃的缓冲经 <paramref name="dropped"/> 返回，**由调用方归还池**。</summary>
    /// <returns>本次入队是否成功（丢新策略下满队时返回 <see langword="false"/>）。</returns>
    public bool TryEnqueue(InboundUnit unit, out PayloadBuffer? dropped)
    {
        dropped = null;

        lock (_gate)
        {
            if (_queue.Count < Capacity)
            {
                _queue.Enqueue(unit);
                Interlocked.Increment(ref _enqueued);
                return true;
            }

            if (Policy == DropPolicy.DropNewest)
            {
                dropped = unit.Buffer;      // 丢弃新来的
                Interlocked.Increment(ref _dropped);
                return false;
            }

            dropped = _queue.Dequeue().Buffer;   // 丢最旧
            _queue.Enqueue(unit);
            Interlocked.Increment(ref _enqueued);
            Interlocked.Increment(ref _dropped);
            return true;
        }
    }

    /// <summary>出队（非阻塞）。</summary>
    public bool TryDequeue(out InboundUnit unit)
    {
        lock (_gate)
        {
            if (_queue.Count > 0)
            {
                unit = _queue.Dequeue();
                return true;
            }
        }

        unit = default;
        return false;
    }

    /// <summary>清空队列，把其中的缓冲交给 <paramref name="onBuffer"/>（锁外调用）。</summary>
    public void Drain(Action<PayloadBuffer> onBuffer)
    {
        ArgumentNullException.ThrowIfNull(onBuffer);

        List<PayloadBuffer> buffers;
        lock (_gate)
        {
            buffers = new List<PayloadBuffer>(_queue.Count);
            while (_queue.Count > 0)
            {
                buffers.Add(_queue.Dequeue().Buffer);
            }
        }

        foreach (var buffer in buffers)
        {
            onBuffer(buffer);
        }
    }
}
