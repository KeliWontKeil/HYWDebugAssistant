using BenchmarkDotNet.Attributes;
using HYWDebugAssistant.DataPlane.Buffers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Benchmarks;

/// <summary>
/// 清单 1 / 4 / 8：单次投递（直调 vs 入队+出队）、每单元分配字节数、池归还开销。
/// </summary>
/// <remarks>
/// 用真实的 L3 原语（<see cref="InboundBuffer"/> / <see cref="PooledPayloadBufferPool"/>），
/// 不是模拟实现——数值才可回写。
/// </remarks>
[MemoryDiagnoser]
public class DeliveryBenchmarks
{
    private const int PayloadSize = 12;   // 与参考桩模块的 {seq:u64, value:f32} 一致
    private const int Operations = 100_000;

    private static readonly ContractLayout Layout = ContractLayout.FromShape(
        ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32))));

    private readonly byte[] _source = new byte[PayloadSize];

    private PooledPayloadBufferPool _pool = null!;
    private InboundBuffer _queue = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pool = new PooledPayloadBufferPool();
        _queue = new InboundBuffer(capacity: 1024);
        Random.Shared.NextBytes(_source);
    }

    /// <summary>基线：同线程直接函数调用（把 12 字节搬到目标缓冲）。</summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public int DirectCopy()
    {
        var sink = new byte[PayloadSize];
        for (var i = 0; i < Operations; i++)
        {
            _source.CopyTo(sink, 0);
        }

        return sink[0];
    }

    /// <summary>池化缓冲的借→还（含归属断言路径）。</summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public long RentReturn()
    {
        for (var i = 0; i < Operations; i++)
        {
            var buffer = _pool.Rent(Layout);
            _pool.Return(buffer);
        }

        return _pool.Rented - _pool.Returned;
    }

    /// <summary>借 + 拷贝 + 入队（生产者侧一次完整投递动作的一半）。</summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public int RentCopyAndEnqueue()
    {
        var accepted = 0;
        for (var i = 0; i < Operations; i++)
        {
            var buffer = _pool.RentCopy(Layout, _source);
            if (_queue.TryEnqueue(new InboundUnit(buffer, "in"), out _))
            {
                accepted++;
            }

            if (_queue.TryDequeue(out var unit))
            {
                _pool.Return(unit.Buffer);
            }
        }

        return accepted;
    }

    /// <summary>入队 + 出队（**不含池操作**：复用同一个已借缓冲，纯队列成本）。</summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public int QueueEnqueueDequeue()
    {
        var buffer = _pool.Rent(Layout);
        var dequeued = 0;

        for (var i = 0; i < Operations; i++)
        {
            _ = _queue.TryEnqueue(new InboundUnit(buffer, "in"), out _);
            if (_queue.TryDequeue(out _))
            {
                dequeued++;
            }
        }

        _pool.Return(buffer);
        return dequeued;
    }
}
