using System.Buffers.Binary;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Timing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **主动接收路径**的端到端验证（`TryReceive` + `ReleaseBuffer`）。
/// </summary>
/// <remarks>
/// 为什么单独保留这组测试：`TryReceive` + `ReleaseBuffer` 是设计保留的主动接收路径 ——
/// step Pull 模块在 OnStep 中自取数据；这条路径必须有组合级覆盖，避免它悄悄坏掉。
/// </remarks>
public class ActiveReceiveTests
{
    [Fact]
    public void 主动接收_应能从输入队列取到单元并归还缓冲()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Register(services, "burst-sender", typeof(BurstSenderModule));
        Register(services, "active-drain", typeof(ActiveDrainModule));

        services.AddModule("s", "test/burst-sender").Succeeded.ShouldBeTrue();
        services.AddModule("d", "test/active-drain").Succeeded.ShouldBeTrue();
        TestLink.Link(services, new Core.Api.PortRef("s", "out"), new Core.Api.PortRef("d", "in"), "link");

        // 模块在自己的活动体里用 TryReceive 取数据（主动接收），计数写入订阅缓冲供框架侧观察
        WaitUntil(() => services.GetSubscriptionSources()
            .Where(x => x.InstanceId == "d" && x.DataSourceName == "received")
            .Select(x => x.Written)
            .FirstOrDefault() >= 3, TimeSpan.FromSeconds(5))
            .ShouldBeTrue("主动接收应真的从队列取到数据单元");

        // 取到的缓冲必须归还：孤儿计数为 0（未归还才会出现孤儿）
        runtime.Carriers.Buffers.Abandoned.ShouldBe(0, "主动接收的缓冲应被 ReleaseBuffer 归还，而非放弃");
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    private static void Register(RuntimeServices services, string name, Type entryType)
    {
        var definition = new ModuleDefinition(
            ModuleId.Create("test", name),
            entryType.Name,
            string.Empty,
            [],
            ModuleCapability.None,
            [], true,
            entryType,
            TestViewPackages.For(entryType),
            string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
    }

    /// <summary>源模块：周期发出一批 u32 单元。</summary>
    [ModuleDefinition("test", "burst-sender")]
    public sealed class BurstSenderModule : IModule, IActivitySource
    {
        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.U32);

        private uint _value;
        private IModuleTimer? _timer;
        private IModuleContext? _context;

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape));
            _context = context;
            _timer = context.Timers.Create();
            _timer.Interval = TimeSpan.FromMilliseconds(1);
            _timer.Tick += OnTimerTick;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_context is { } context)
            {
                Send(context);
            }
        }

        public void OnStep(IModuleContext context, StepReason reason)
        {
            _context = context;

            if (reason.HasFlag(StepReason.Start))
            {
                _timer?.Start();
            }
        }

        private void Send(IModuleContext context)
        {
            Span<byte> unit = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(unit, _value++);
            _ = context.Ports.TrySend("out", unit);
        }
    }

    /// <summary>宿模块：**主动接收**——在 OnStep 里取数据、用完归还，并把计数写入订阅缓冲。</summary>
    [ModuleDefinition("test", "active-drain")]
    public sealed class ActiveDrainModule : IModule, IActivitySource
    {
        public const string ReceivedSource = "received";

        public ReceiveMode Receive => ReceiveMode.Pull;

        private ulong _received;

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.U32)));

            // 本用例只验证主动接收路径（`TryReceive` + `ReleaseBuffer`），与界面无关；
            // 但**订阅缓冲只为声明过的显示源建立** —— 于是这里把那条源按代码声明出来。
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
                ReceivedSource,
                ViewDataSourceKind.Value,
                ContractPrimitive.U64));
        }

        public void OnStep(IModuleContext context, StepReason reason)
        {
            Span<byte> sample = stackalloc byte[sizeof(ulong)];

            while (context.Ports.TryReceive(out var buffer, out _))
            {
                if (buffer is null)
                {
                    continue;
                }

                try
                {
                    var reader = new PayloadReader(buffer.Span, buffer.Layout);
                    _ = reader.ReadU32(string.Empty);
                    _received++;
                }
                finally
                {
                    context.Ports.ReleaseBuffer(buffer);
                }

                BinaryPrimitives.WriteUInt64LittleEndian(sample, _received);
                context.Ports.Publish(ReceivedSource, sample);
            }
        }
    }
}
