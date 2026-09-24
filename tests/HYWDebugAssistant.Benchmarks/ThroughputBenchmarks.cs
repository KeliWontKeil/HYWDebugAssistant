using BenchmarkDotNet.Attributes;
using HYWDebugAssistant.DataPlane.Buffers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Benchmarks;

/// <summary>
/// 清单 5 / 6：吞吐上限（单线程 vs 多生产者）与持续负载下的**丢弃率 + GC 次数 + 最长暂停**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>多生产者共用**一个** <see cref="InboundBuffer"/>，消费者单线程出队——即框架里"多个上游投递到同一实例"的形态；</item>
///   <item>持续负载用固定单元数（1,000,000）跑完即止，通过"容量刻意设小"制造丢弃，观察丢弃率；</item>
///   <item>GC 次数与最长暂停取自 <see cref="GC.GetGCMemoryInfo"/>（在 <c>GlobalCleanup</c> 打印，随日志留档）。</item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class ThroughputBenchmarks
{
    private const int TotalUnits = 1_000_000;

    private static readonly ContractLayout Layout = ContractLayout.FromShape(
        ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32))));

    private PooledPayloadBufferPool _pool = null!;
    private InboundBuffer _queue = null!;
    private byte[] _source = null!;

    /// <summary>生产者线程数（1 = 单线程基准）。</summary>
    [Params(1, 2, 4)]
    public int Producers { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool = new PooledPayloadBufferPool();
        _source = new byte[Layout.SizeInBytes];
        Random.Shared.NextBytes(_source);
    }

    /// <summary>多生产者 → 单消费者：吞吐（单元/秒由 BenchmarkDotNet 以 ops/s 呈现，1 op = 全量单元）。</summary>
    [Benchmark(OperationsPerInvoke = TotalUnits)]
    public long MultiProducerThroughput()
    {
        _queue = new InboundBuffer(capacity: 4096);

        var perProducer = TotalUnits / Producers;
        var threads = new Thread[Producers];
        var consumed = 0L;

        var consumer = new Thread(() =>
        {
            var count = 0L;
            while (Volatile.Read(ref _producersDone) < Producers || _queue.Count > 0)
            {
                if (_queue.TryDequeue(out var unit))
                {
                    _pool.Return(unit.Buffer);
                    count++;
                }
            }

            Volatile.Write(ref consumed, count);
        });

        Volatile.Write(ref _producersDone, 0);
        consumer.Start();

        for (var p = 0; p < Producers; p++)
        {
            threads[p] = new Thread(() =>
            {
                for (var i = 0; i < perProducer; i++)
                {
                    var buffer = _pool.RentCopy(Layout, _source);
                    if (!_queue.TryEnqueue(new InboundUnit(buffer, "in"), out var dropped) && dropped is not null)
                    {
                        _pool.Return(dropped);
                    }
                }

                _ = Interlocked.Increment(ref _producersDone);
            });

            threads[p].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        consumer.Join();
        return consumed;
    }

    /// <summary>持续负载：容量刻意偏小（256），观察 10⁶ 单元的丢弃率与 GC 行为。</summary>
    [Benchmark(OperationsPerInvoke = TotalUnits)]
    public double SustainedLoadDropRate()
    {
        var queue = new InboundBuffer(capacity: 256);
        var produced = 0L;

        for (var i = 0; i < TotalUnits; i++)
        {
            var buffer = _pool.RentCopy(Layout, _source);
            if (queue.TryEnqueue(new InboundUnit(buffer, "in"), out var dropped))
            {
                produced++;
            }

            if (dropped is not null)
            {
                _pool.Return(dropped);
            }

            // 消费者明显慢于生产者（每 8 个单元取 1 个）→ 制造"UI/下游跟不上"
            if ((i & 7) == 0 && queue.TryDequeue(out var unit))
            {
                _pool.Return(unit.Buffer);
            }
        }

        queue.Drain(_pool.Return);

        _lastDropRate = produced == 0 ? 0 : (double)queue.Dropped / TotalUnits;
        _lastDropped = queue.Dropped;
        _lastEnqueued = queue.Enqueued;
        _lastCapacity = queue.Capacity;

        return _lastDropRate;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        GC.KeepAlive(_pool);   // 显式触碰实例字段：清理与本次基准实例相关

        // 丢弃率是"返回的诊断值"，而 BenchmarkDotNet 只报时间 → 在这里显式打印，随日志留档
        Console.WriteLine(
            $"[丢弃] 生产者={Producers} 容量={_lastCapacity} 入队={_lastEnqueued} 丢弃={_lastDropped} "
            + $"丢弃率={_lastDropRate:P2}");

        var info = GC.GetGCMemoryInfo();
        Console.WriteLine(
            $"[GC] 代数={GC.MaxGeneration} · 已回收代数={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} · "
            + $"总暂停={info.PauseTimePercentage:F3}% · 最长暂停={MaxPause(info):F3} ms");
    }

    private static double MaxPause(GCMemoryInfo info)
    {
        var max = 0d;
        foreach (var pause in info.PauseDurations)
        {
            max = Math.Max(max, pause.TotalMilliseconds);
        }

        return max;
    }

    private int _producersDone;
    private double _lastDropRate;
    private long _lastDropped;
    private long _lastEnqueued;
    private int _lastCapacity;
}
