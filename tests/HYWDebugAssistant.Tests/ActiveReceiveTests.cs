using System.Buffers.Binary;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using Shouldly;
using Xunit;
using ActivityKind = HYWDebugAssistant.Sdk.Lifecycle.ActivityKind;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **主动接收路径**的端到端验证（`TryReceive` + `ReleaseBuffer`）。
/// </summary>
/// <remarks>
/// 为什么单独保留这组测试：`TryReceive` + `ReleaseBuffer` 是设计保留的主动接收路径 ——
/// 模块允许用自己的循环取数据；在 `WaitForData` 原语补上之前，这条路径必须有组合级覆盖，避免它悄悄坏掉。
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

        services.AddModule("s", "test/burst-sender/1.0.0").Succeeded.ShouldBeTrue();
        services.AddModule("d", "test/active-drain/1.0.0").Succeeded.ShouldBeTrue();
        services.Connect(new Core.Api.PortRef("s", "out"), [new Core.Api.PortRef("d", "in")], "link").Succeeded.ShouldBeTrue();

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
            ModuleId.Create("test", name, "1.0.0"),
            entryType.Name,
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            null,
            true,
            entryType,
            TestViewPackages.For(entryType),
            string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
    }

    /// <summary>源模块：周期发出一批 u32 单元。</summary>
    [ModuleDefinition("test", "burst-sender", "1.0.0")]
    public sealed class BurstSenderModule : IModule, IActivitySource
    {
        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.U32);

        public ActivityKind Kind => ActivityKind.Periodic;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(1);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape));

        public void RunActivity(IModuleContext context)
        {
            Span<byte> unit = stackalloc byte[sizeof(uint)];
            var value = 0u;

            while (!context.StopSignal.IsStopRequested)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(unit, value++);
                _ = context.Ports.TrySend("out", unit);
                _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(1));
            }
        }
    }

    /// <summary>宿模块：**主动接收**——在自己的活动体里取数据、用完归还，并把计数写入订阅缓冲。</summary>
    [ModuleDefinition("test", "active-drain", "1.0.0")]
    public sealed class ActiveDrainModule : IModule, IActivitySource
    {
        public const string ReceivedSource = "received";

        public ActivityKind Kind => ActivityKind.EventDriven;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(1);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.U32)));

            // 本模块**没有控件**（包内 `view.json` 只声明端口与数据源）：
            // 本用例只验证主动接收路径（`TryReceive` + `ReleaseBuffer`），与界面无关；
            // 订阅缓冲由 `RunActivity` 里的 `context.Ports.Publish(ReceivedSource, …)` 写入。
        }

        public void RunActivity(IModuleContext context)
        {
            ulong received = 0;
            Span<byte> sample = stackalloc byte[sizeof(ulong)];

            while (!context.StopSignal.IsStopRequested)
            {
                if (!context.Ports.TryReceive(out var buffer, out _))
                {
                    _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(1));
                    continue;
                }

                if (buffer is null)
                {
                    continue;
                }

                try
                {
                    var reader = new PayloadReader(buffer.Span, buffer.Layout);
                    _ = reader.ReadU32(string.Empty);
                    received++;
                }
                finally
                {
                    context.Ports.ReleaseBuffer(buffer);
                }

                BinaryPrimitives.WriteUInt64LittleEndian(sample, received);
                context.Ports.Publish(ReceivedSource, sample);
            }
        }
    }
}
