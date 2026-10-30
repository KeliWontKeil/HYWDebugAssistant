using HYWDebugAssistant.Access.Adapters;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.Sdk.Access;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.ExternalAccess;

/// <summary>
/// 串口探活的端到端链路：真实适配件 + 假传输 + 真实会话。
/// </summary>
/// <remarks>
/// 判据是"谁在哪条路径上释放传输"：探活只报事实，传输由会话在实例线程上释放一次。
/// </remarks>
public sealed class SerialLivenessEndToEndTests
{
    [Fact]
    public void 串口设备消失_两次探活后会话故障_推进一拍释放传输()
    {
        var transport = new FakeSerialTransport();
        var adapter = new SerialAccessAdapter(() => transport);
        var session = new ExternalAccessSession(
            new ExternalAccessDeclaration("serial", ExternalAccessStandardIds.Serial),
            adapter,
            typeof(SerialConfig),
            _ => new SerialConfig { PortName = "COM_TEST" },
            new AccessInboundBuffer(4),
            TimeSpan.FromSeconds(1),
            signal: null);

        session.RequestOpen();
        _ = session.Pump();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Open);

        transport.Present = false;

        adapter.CheckLiveness();
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Open, "单次缺失不判失联");

        adapter.CheckLiveness();

        var faulted = session.GetSnapshot();
        faulted.State.ShouldBe(ExternalAccessState.Faulted);
        faulted.Failure!.Kind.ShouldBe(ExternalAccessFailureKind.DeviceLost);
        transport.Disposed.ShouldBeFalse("探活路径不得释放传输");

        _ = session.Pump();

        transport.Disposed.ShouldBeTrue("传输释放归实例线程上的会话");
        session.GetSnapshot().State.ShouldBe(ExternalAccessState.Faulted, "释放适配件不得清掉故障态");
        session.GetSnapshot().Failure.ShouldNotBeNull("释放适配件不得清掉失败事实");
    }

    /// <summary>
    /// 端到端用例只走"探活 → 故障 → 释放"，不产生数据与读故障事件，因此事件访问器是空的。
    /// </summary>
    private sealed class FakeSerialTransport : ISerialTransport
    {
        public event Action<byte[]>? DataReceived
        {
            add { }
            remove { }
        }

        public event Action<Exception>? ReadFaulted
        {
            add { }
            remove { }
        }

        /// <summary>端口是否仍在系统中；置假模拟设备拔出。</summary>
        public bool Present { get; set; } = true;

        /// <summary>传输是否已被释放。</summary>
        public bool Disposed { get; private set; }

        public bool IsPresent() => Present;

        public void Open(SerialConfig config) => _ = config;

        public void Write(ReadOnlySpan<byte> payload) => _ = payload;

        /// <summary>写超时（本用例不涉及写，只记录最后一次下发值）。</summary>
        public TimeSpan WriteTimeout { get; private set; }

        public void SetWriteTimeout(TimeSpan timeout) => WriteTimeout = timeout;

        public void Close() => Dispose();

        public void Dispose() => Disposed = true;
    }
}
