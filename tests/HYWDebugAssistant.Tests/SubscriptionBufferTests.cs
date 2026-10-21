using System.Buffers.Binary;
using System.Diagnostics;
using HYWDebugAssistant.Core.Views;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T5/M1-9：订阅缓冲（Q4-4/R4-12）——形态判定、最新值槽、环形丢旧、序列读取、并发无撕裂。
/// </summary>
public class SubscriptionBufferTests
{
    // ------------------------------------------------------------ 形态判定（04 §4.3 交接形态表）

    [Fact]
    public void 形态_数值类元素用最新值槽_曲线类元素用环形缓冲()
    {
        var set = new SubscriptionSet("m1",
        [
            new DisplaySourceDeclaration("value", ViewDataSourceKind.Value, ContractPrimitive.F64),
            new DisplaySourceDeclaration("wave", ViewDataSourceKind.Sequence, ContractPrimitive.F64),
        ]);

        set.Count.ShouldBe(2);
        set.TryGet("value", out var value).ShouldBeTrue();
        value!.Form.ShouldBe(SubscriptionBufferForm.LatestSlot, "A1 数值 = 最新值槽（容量 1）");
        value.Capacity.ShouldBe(1);
        value.ValueType.ShouldBe(ContractPrimitive.F64);

        set.TryGet("wave", out var wave).ShouldBeTrue();
        wave!.Form.ShouldBe(SubscriptionBufferForm.Ring, "A3 曲线 = 环形缓冲");
        wave.Capacity.ShouldBe(SubscriptionBuffer.DefaultRingCapacity);
    }

    [Fact]
    public void 形态_声明为序列的即使绑到数值元素也用环形缓冲()
    {
        var set = new SubscriptionSet("m1",
        [
            new DisplaySourceDeclaration("seq", ViewDataSourceKind.Sequence, ContractPrimitive.F64),
        ]);

        set.TryGet("seq", out var buffer).ShouldBeTrue();
        buffer!.Form.ShouldBe(SubscriptionBufferForm.Ring, "模块声明了序列语义 → 保留历史");
    }

    [Fact]
    public void 无UI模块_不建立任何订阅缓冲()
    {
        var set = new SubscriptionSet("m1", []);

        set.Count.ShouldBe(0);
        set.Buffers.ShouldBeEmpty();
    }

    [Fact]
    public void 未声明的数据源_丢弃并只提示一次()
    {
        var set = new SubscriptionSet("m1",
        [
            new DisplaySourceDeclaration("value", ViewDataSourceKind.Value, ContractPrimitive.F64),
        ]);

        var sample = new byte[4];

        set.TryWrite("ghost", sample, out var firstFailure).ShouldBe(SubscriptionWriteOutcome.UnknownDataSource);
        firstFailure.ShouldNotBeNull();
        firstFailure!.ShouldContain("未在模块声明中登记");

        // 重复出现 → 不再提示（不刷屏）
        set.TryWrite("ghost", sample, out _).ShouldBe(SubscriptionWriteOutcome.Suppressed);
    }

    // ------------------------------------------------------------ 最新值槽

    [Fact]
    public void 最新值槽_只保留最新样本()
    {
        var buffer = new SubscriptionBuffer("m1", "value", SubscriptionBufferForm.LatestSlot, 1, ContractPrimitive.I32);

        WriteInt32(buffer, 11).ShouldBeTrue();
        WriteInt32(buffer, 22).ShouldBeTrue();

        ReadInt32(buffer).ShouldBe(22);
        buffer.Count.ShouldBe(1);
        buffer.Written.ShouldBe(2);
        buffer.Overwritten.ShouldBe(0, "最新值槽覆盖旧值是正常行为，不算丢弃");
    }

    [Fact]
    public void 未写入过_读取应返回无数据且不抛异常()
    {
        var buffer = new SubscriptionBuffer("m1", "value", SubscriptionBufferForm.LatestSlot, 1, ContractPrimitive.F32);
        var destination = new byte[4];

        buffer.TryReadLatest(destination, out var length).ShouldBeFalse();
        length.ShouldBe(0);
        buffer.SampleSizeInBytes.ShouldBe(0);
    }

    // ------------------------------------------------------------ 环形缓冲

    [Fact]
    public void 环形缓冲_满则丢旧并计数()
    {
        var buffer = new SubscriptionBuffer("m1", "wave", SubscriptionBufferForm.Ring, capacity: 4, ContractPrimitive.I32);

        for (var i = 0; i < 6; i++)
        {
            WriteInt32(buffer, i).ShouldBeTrue();
        }

        buffer.Count.ShouldBe(4);
        buffer.Written.ShouldBe(6);
        buffer.Overwritten.ShouldBe(2, "容量 4 写 6 个 → 覆盖 2 个旧样本");

        var samples = ReadInt32Series(buffer, 4);
        samples.ShouldBe([2, 3, 4, 5], "保留最新的 4 个样本");
    }

    [Fact]
    public void 环形缓冲_序列读取应按时间升序()
    {
        var buffer = new SubscriptionBuffer("m1", "wave", SubscriptionBufferForm.Ring, capacity: 8, ContractPrimitive.I32);
        for (var i = 1; i <= 5; i++)
        {
            WriteInt32(buffer, i);
        }

        ReadInt32Series(buffer, 8).ShouldBe([1, 2, 3, 4, 5], "未满时按写入顺序");
        ReadInt32Series(buffer, 3).ShouldBe([3, 4, 5], "只取最近 3 个");
        ReadInt32Series(buffer, 0).ShouldBeEmpty();
    }

    [Fact]
    public void 样本尺寸不一致_应丢弃并给出原因()
    {
        var buffer = new SubscriptionBuffer("m1", "value", SubscriptionBufferForm.LatestSlot, 1, ContractPrimitive.F32);

        buffer.Write([1, 2, 3, 4], out _).ShouldBeTrue();
        buffer.Write([1, 2, 3, 4, 5, 6, 7, 8], out var failure).ShouldBeFalse();

        failure.ShouldNotBeNull();
        failure!.ShouldContain("超过已定槽尺寸");
        buffer.Dropped.ShouldBe(1);
        buffer.SampleSizeInBytes.ShouldBe(4);
    }

    [Fact]
    public void 过大样本_应被拒绝()
    {
        var buffer = new SubscriptionBuffer("m1", "block", SubscriptionBufferForm.Ring, capacity: 8);

        buffer.Write(new byte[SubscriptionBuffer.MaxSampleSizeInBytes + 1], out var failure).ShouldBeFalse();

        failure.ShouldNotBeNull();
        failure!.ShouldContain("样本过大");
        buffer.Dropped.ShouldBe(1);
    }

    // ------------------------------------------------------------ 并发（模块线程写 / UI 线程读）

    [Fact]
    public void 并发读写_读到的样本不应撕裂且绝不阻塞写者()
    {
        const int iterations = 20000;
        var buffer = new SubscriptionBuffer("m1", "wave", SubscriptionBufferForm.Ring, capacity: 64, ContractPrimitive.I64);
        var destination = new byte[8];
        var torn = 0;
        var reads = 0;

        // 写者：每个样本写两遍同一个序号（撕裂时两半会不相等）
        var writer = new Thread(() =>
        {
            var payload = new byte[8];
            for (var i = 0; i < iterations; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload, i);
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), i);
                _ = buffer.Write(payload, out _);
            }
        });

        writer.Start();

        // 写者进度由缓冲自带计数给出（无需额外同步原语）
        while (buffer.Written < iterations || reads < 100)
        {
            if (buffer.TryReadLatest(destination, out var length))
            {
                reads++;
                var first = BinaryPrimitives.ReadInt32LittleEndian(destination);
                var second = BinaryPrimitives.ReadInt32LittleEndian(destination.AsSpan(4));
                if (first != second)
                {
                    torn++;
                }
            }

            if (reads > 300000)
            {
                break;   // 写者已结束且读不到新数据
            }
        }

        writer.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue("写者不应被读者阻塞");
        torn.ShouldBe(0, "序号锁保证读侧永远不会读到写了一半的样本");
        buffer.Written.ShouldBe(iterations);
    }

    [Fact]
    public void 并发写入_稳态不应产生分配()
    {
        // 稳态零分配：槽尺寸只在首次写入时确定，之后只做 memcpy。
        // 断言方式：**写入次数翻倍不增加分配**（排除 JIT/首次调用的一次性开销，这类噪声与写入次数无关）。
        var buffer = new SubscriptionBuffer("m1", "wave", SubscriptionBufferForm.Ring, capacity: 256, ContractPrimitive.F32);
        var payload = new byte[4];

        var warmup = MeasureAllocations(buffer, payload, 20000);
        var baseline = MeasureAllocations(buffer, payload, 50000);
        var doubled = MeasureAllocations(buffer, payload, 100000);

        _ = warmup;
        (doubled - baseline).ShouldBeLessThan(
            128,
            $"写入次数从 5 万翻到 10 万不应新增分配（实际 {doubled - baseline} 字节）");

        buffer.Written.ShouldBe(170000);
    }

    /// <summary>执行若干次写入并返回期间的分配字节数。</summary>
    private static long MeasureAllocations(SubscriptionBuffer buffer, byte[] payload, int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            _ = buffer.Write(payload, out _);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // ------------------------------------------------------------ 辅助

    private static bool WriteInt32(SubscriptionBuffer buffer, int value)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value);
        return buffer.Write(payload, out _);
    }

    private static int ReadInt32(SubscriptionBuffer buffer)
    {
        var destination = new byte[4];
        buffer.TryReadLatest(destination, out var length).ShouldBeTrue();
        length.ShouldBe(4);
        return BinaryPrimitives.ReadInt32LittleEndian(destination);
    }

    private static int[] ReadInt32Series(SubscriptionBuffer buffer, int maxSamples)
    {
        var destination = new byte[Math.Max(4, maxSamples * 4)];
        var count = buffer.ReadSeries(destination, maxSamples);
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadInt32LittleEndian(destination.AsSpan(i * 4));
        }

        return result;
    }
}
