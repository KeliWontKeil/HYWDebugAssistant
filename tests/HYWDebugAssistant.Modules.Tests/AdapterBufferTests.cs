using HYWDebugAssistant.Access.Adapters;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 适配件读暂存的**分片交付**：目标装不下整块时不许丢——拷满目标、余量留在队头。
/// </summary>
/// <remarks>
/// 这条是"接收不静默丢"的底层判据：旧实现遇到"块比目标大"会整块丢弃并返回 false，
/// 长文本连写/驱动合并出的 &gt;1472 B 块会在没有任何计数的情况下消失。
/// </remarks>
public sealed class AdapterBufferTests
{
    private static readonly int[] ExpectedChunkLengths = [1472, 1472, 1152];

    [Fact]
    public void 块大于目标_应分片交付且字节不丢()
    {
        var buffer = new AdapterBuffer(capacity: 4);
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

        Assert.Equal(ExpectedChunkLengths, lengths);
        received.Count.ShouldBe(payload.Length);
        Assert.Equal(payload, received);
        buffer.Count.ShouldBe(0);
    }

    [Fact]
    public void 分片期间再入队_余量与新单元按顺序交付()
    {
        var buffer = new AdapterBuffer(capacity: 4);
        var first = Enumerable.Repeat((byte)1, 4096).ToArray();
        var second = Enumerable.Repeat((byte)2, 100).ToArray();

        buffer.TryEnqueue(first, out _).ShouldBeTrue();

        var destination = new byte[1472];
        buffer.TryDequeue(destination, out var length).ShouldBeTrue();
        length.ShouldBe(1472);

        buffer.TryEnqueue(second, out _).ShouldBeTrue();

        var rest = new List<byte>();
        while (buffer.TryDequeue(destination, out length))
        {
            rest.AddRange(destination.AsSpan(0, length).ToArray());
        }

        rest.Count.ShouldBe(4096 - 1472 + 100);
        rest.Take(4096 - 1472).ShouldAllBe(value => value == 1);
        rest.Skip(4096 - 1472).ShouldAllBe(value => value == 2);
    }

    [Fact]
    public void 满队挤旧_被挤掉的是剩余长度()
    {
        var buffer = new AdapterBuffer(capacity: 1);
        buffer.TryEnqueue(Enumerable.Repeat((byte)7, 4096).ToArray(), out _).ShouldBeTrue();

        var destination = new byte[1472];
        buffer.TryDequeue(destination, out var length).ShouldBeTrue();
        length.ShouldBe(1472);

        buffer.TryEnqueue(new byte[] { 9, 9, 9 }, out var dropped).ShouldBeTrue();
        dropped.ShouldBe(4096 - 1472, "挤掉的是队头**剩余**长度，不是原始长度");

        buffer.TryDequeue(destination, out length).ShouldBeTrue();
        length.ShouldBe(3);
        destination[0].ShouldBe((byte)9);
    }

    [Fact]
    public void 空目标_不消费队头()
    {
        var buffer = new AdapterBuffer(capacity: 2);
        buffer.TryEnqueue(new byte[] { 1, 2, 3 }, out _).ShouldBeTrue();

        buffer.TryDequeue(Span<byte>.Empty, out var length).ShouldBeFalse();
        length.ShouldBe(0);
        buffer.Count.ShouldBe(1);

        var destination = new byte[8];
        buffer.TryDequeue(destination, out length).ShouldBeTrue();
        length.ShouldBe(3);
    }
}
