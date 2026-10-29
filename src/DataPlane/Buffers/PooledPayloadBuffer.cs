using System.Buffers;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;

namespace HYWDebugAssistant.DataPlane.Buffers;

/// <summary>
/// 池化数据单元缓冲（框架侧实现）：定长扁平字节 + 池借还。
/// </summary>
/// <remarks>
/// <para><b>归属协议</b>：Rent 后所有权归调用方；跨线程移交时**由接收方归还**；
/// 归还后本对象不得再被访问（<see cref="Span"/> 抛 <see cref="ObjectDisposedException"/>）。</para>
/// <para>孤儿线程场景下由池的 <c>Abandon</c> 放弃归还（避免缓冲被复用后被仍在运行的线程写入）。</para>
/// </remarks>
internal sealed class PooledPayloadBuffer : PayloadBuffer
{
    private byte[]? _array;

    internal PooledPayloadBuffer(byte[] array, int length, ContractLayout layout)
    {
        _array = array;
        Length = length;
        Layout = layout;
    }

    /// <inheritdoc />
    public override int Length { get; }

    /// <inheritdoc />
    public override ContractLayout Layout { get; }

    /// <inheritdoc />
    public override ReadOnlySpan<byte> Span => WritableSpan;

    /// <summary>可写视图（框架侧填写数据单元用）。</summary>
    internal Span<byte> WritableSpan
    {
        get
        {
            var array = _array;
            if (array is null)
            {
                throw new ObjectDisposedException(nameof(PayloadBuffer), DataPlaneText.Get("dataplane.buffer.disposed"));
            }

            return array.AsSpan(0, Length);
        }
    }

    /// <summary>取出底层数组并标记为已归还（幂等：第二次返回 <see langword="null"/>）。</summary>
    internal byte[]? Detach()
    {
        var array = _array;
        _array = null;
        return array;
    }
}

/// <summary>数据单元填充委托（ref struct 不能作泛型参数，故用自定义委托）。</summary>
/// <param name="payload">可写视图（长度 = 布局尺寸）。</param>
public delegate void PayloadFill(Span<byte> payload);

/// <summary>
/// 数据单元缓冲池：按契约布局的定长尺寸借还缓冲（扁平 + 池化）。
/// </summary>
/// <remarks>
/// 用 <see cref="ArrayPool{T}"/> 而非自研池：成熟实现、线程安全、零额外维护成本。
/// 诊断计数（借/还/放弃）用于发现归还协议缺陷。
/// </remarks>
public sealed class PooledPayloadBufferPool
{
    private readonly ArrayPool<byte> _pool;
    private long _rented;
    private long _returned;
    private long _abandoned;

    /// <summary>创建池（默认使用 <see cref="ArrayPool{T}.Shared"/>）。</summary>
    public PooledPayloadBufferPool(ArrayPool<byte>? pool = null) => _pool = pool ?? ArrayPool<byte>.Shared;

    /// <summary>累计借出数。</summary>
    public long Rented => Interlocked.Read(ref _rented);

    /// <summary>累计归还数。</summary>
    public long Returned => Interlocked.Read(ref _returned);

    /// <summary>累计放弃数（孤儿线程场景）。</summary>
    public long Abandoned => Interlocked.Read(ref _abandoned);

    /// <summary>借一个与布局同尺寸的缓冲。</summary>
    public PayloadBuffer Rent(ContractLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var array = _pool.Rent(layout.SizeInBytes);
        Interlocked.Increment(ref _rented);
        return new PooledPayloadBuffer(array, layout.SizeInBytes, layout);
    }

    /// <summary>借一个缓冲并就地填充（投递方构造数据单元用）。</summary>
    public PayloadBuffer RentAndFill(ContractLayout layout, PayloadFill fill)
    {
        ArgumentNullException.ThrowIfNull(fill);

        var buffer = (PooledPayloadBuffer)Rent(layout);
        try
        {
            fill(buffer.WritableSpan);
            return buffer;
        }
        catch
        {
            // 填充失败：立即归还，避免泄漏（异常继续向上传播）
            Return(buffer);
            throw;
        }
    }

    /// <summary>借一个缓冲并**从给定字节拷贝**（多播投递构造多个独立单元时用）。</summary>
    public PayloadBuffer RentCopy(ContractLayout layout, ReadOnlySpan<byte> source)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var buffer = (PooledPayloadBuffer)Rent(layout);
        try
        {
            var target = buffer.WritableSpan;
            source[..Math.Min(source.Length, target.Length)].CopyTo(target);
            return buffer;
        }
        catch
        {
            Return(buffer);
            throw;
        }
    }
    public void Return(PayloadBuffer buffer)
    {
        if (buffer is not PooledPayloadBuffer pooled)
        {
            return;
        }

        var array = pooled.Detach();
        if (array is null)
        {
            return; // 已归还过 → 幂等忽略（双重归还不会破坏池）
        }

        _pool.Return(array);
        Interlocked.Increment(ref _returned);
    }

    /// <summary>
    /// 放弃缓冲：**不归还池**（交给 GC）。
    /// </summary>
    /// <remarks>用于孤儿线程场景——归还后缓冲可能被复用，而仍在运行的线程可能继续写入它。</remarks>
    public void Abandon(PayloadBuffer buffer)
    {
        if (buffer is not PooledPayloadBuffer pooled)
        {
            return;
        }

        var array = pooled.Detach();
        if (array is null)
        {
            return;
        }

        // 有意不归还：让 GC 回收，避免被复用
        _ = array;
        Interlocked.Increment(ref _abandoned);
    }
}
