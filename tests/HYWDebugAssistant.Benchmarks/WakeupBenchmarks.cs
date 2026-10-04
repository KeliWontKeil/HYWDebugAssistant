using BenchmarkDotNet.Attributes;

namespace HYWDebugAssistant.Benchmarks;

/// <summary>
/// 清单 2：跨线程唤醒延迟（自旋 vs 事件）——两种交接策略的**实测对比**（只记录，不设阈值）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>测法：**两个真实线程**做"生产者放行 → 消费者应答"的往返，除以往返次数得到**单次交接延迟**；</item>
///   <item>注意：自旋只是被实测的**对照策略**；框架实现里不使用自旋锁。</item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class WakeupBenchmarks : IDisposable
{
    private const int Handoffs = 20_000;

    private Thread _spinConsumer = null!;
    private Thread _eventConsumer = null!;
    private int _spinToken;
    private AutoResetEvent _eventPing = null!;
    private AutoResetEvent _eventPong = null!;
    private volatile bool _stop;

    [GlobalSetup]
    public void Setup()
    {
        _eventPing = new AutoResetEvent(false);
        _eventPong = new AutoResetEvent(false);
        _stop = false;

        _spinConsumer = new Thread(SpinConsumerLoop) { IsBackground = true, Name = "bench-spin-consumer" };
        _spinConsumer.Start();

        _eventConsumer = new Thread(EventConsumerLoop) { IsBackground = true, Name = "bench-event-consumer" };
        _eventConsumer.Start();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _stop = true;
        _ = _eventPing.Set();
        _ = _spinConsumer.Join(TimeSpan.FromSeconds(2));
        _ = _eventConsumer.Join(TimeSpan.FromSeconds(2));
        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _eventPing.Dispose();
        _eventPong.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>自旋交接：生产者置令牌 → 消费者清零 → 生产者观察到（单次往返）。</summary>
    [Benchmark(OperationsPerInvoke = Handoffs)]
    public void SpinHandoff()
    {
        for (var i = 0; i < Handoffs; i++)
        {
            Volatile.Write(ref _spinToken, 1);
            while (Volatile.Read(ref _spinToken) != 0)
            {
                // 生产者自旋等待应答（对照策略）
            }
        }
    }

    /// <summary>事件交接：生产者 Set → 消费者 WaitOne 后 Set 回执（单次往返）。</summary>
    [Benchmark(OperationsPerInvoke = Handoffs)]
    public void EventHandoff()
    {
        for (var i = 0; i < Handoffs; i++)
        {
            _ = _eventPing.Set();
            _ = _eventPong.WaitOne();
        }
    }

    private void SpinConsumerLoop()
    {
        while (!_stop)
        {
            if (Volatile.Read(ref _spinToken) != 0)
            {
                Volatile.Write(ref _spinToken, 0);
            }
            else
            {
                Thread.SpinWait(4);
            }
        }
    }

    private void EventConsumerLoop()
    {
        while (true)
        {
            _ = _eventPing.WaitOne();
            if (_stop)
            {
                return;
            }

            _ = _eventPong.Set();
        }
    }
}
