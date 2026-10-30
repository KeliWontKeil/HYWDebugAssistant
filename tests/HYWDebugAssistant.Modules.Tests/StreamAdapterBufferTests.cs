using HYWDebugAssistant.Access.Adapters;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 流式适配件暂存：按字节流收、按目标大小切；小块合并、大块切片、满时丢最旧字节并交回丢弃字节数。
/// </summary>
public sealed class StreamAdapterBufferTests
{
    private static readonly int[] ExpectedLengths = [1472, 1472, 1152];

    [Fact]
    public void 单次大块_按目标切片且字节不丢()
    {
        var buffer = new StreamAdapterBuffer();
        var payload = Enumerable.Range(0, 4096).Select(index => (byte)(index & 0xFF)).ToArray();

        buffer.TryEnqueue(payload, out var dropped).ShouldBeTrue();
        dropped.ShouldBe(0);

        var destination = new byte[1472];
        var lengths = new List<int>();
        var received = new List<byte>();

        while (buffer.TryDequeue(destination, out var length))
        {
            lengths.Add(length);
            received.AddRange(destination.AsSpan(0, length).ToArray());
        }

        Assert.Equal(ExpectedLengths, lengths);
        Assert.Equal(payload, received);
    }

    [Fact]
    public void 多次小块_合并成一次交付()
    {
        var buffer = new StreamAdapterBuffer();
        buffer.TryEnqueue(new byte[] { 1, 2, 3 }, out _).ShouldBeTrue();
        buffer.TryEnqueue(new byte[] { 4, 5, 6 }, out _).ShouldBeTrue();
        buffer.TryEnqueue(new byte[] { 7, 8, 9 }, out _).ShouldBeTrue();

        buffer.Count.ShouldBe(9);

        var destination = new byte[1472];
        buffer.TryDequeue(destination, out var length).ShouldBeTrue();

        length.ShouldBe(9, "驱动拆成的小块必须能合成一次契约单元交付，而不是一块一单元");
        destination.AsSpan(0, 9).ToArray().ShouldBe(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        buffer.Count.ShouldBe(0);
    }

    [Fact]
    public void 容量满_丢最旧字节并交回丢弃字节数()
    {
        var buffer = new StreamAdapterBuffer(capacityBytes: 100);
        buffer.TryEnqueue(Enumerable.Repeat((byte)1, 60).ToArray(), out _).ShouldBeTrue();

        buffer.TryEnqueue(Enumerable.Repeat((byte)2, 60).ToArray(), out var dropped).ShouldBeTrue();
        dropped.ShouldBe(20, "60 + 60 超过 100 字节，丢最旧的 20 字节");

        buffer.Count.ShouldBe(100);

        var destination = new byte[100];
        buffer.TryDequeue(destination, out var length).ShouldBeTrue();
        length.ShouldBe(100);
        destination.AsSpan(0, 40).ToArray().ShouldAllBe(value => value == 1);
        destination.AsSpan(40).ToArray().ShouldAllBe(value => value == 2);
    }

    [Fact]
    public void 空目标_不消费队头()
    {
        var buffer = new StreamAdapterBuffer();
        buffer.TryEnqueue(new byte[] { 1, 2, 3 }, out _).ShouldBeTrue();

        buffer.TryDequeue(Span<byte>.Empty, out var length).ShouldBeFalse();
        length.ShouldBe(0);
        buffer.Count.ShouldBe(3);
    }
}
