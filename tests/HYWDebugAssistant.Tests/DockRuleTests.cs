using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **"接触且对齐"建网判定**（<c>DockRule</c>）的回归网。
/// </summary>
/// <remarks>
/// 本文件只保留"跨缩放时按物理投影判接触"这一条；其余建网判定（接触、标记对齐、重合、
/// 标签退出、契约不兼容）由 <c>CoreRuntimeTests.对齐建网_*</c> 走真实变更入口覆盖。
/// </remarks>
public class DockRuleTests
{
    /// <summary>
    /// **跨缩放接触按物理投影判定**：两块在各自屏上 DIP 数值不同，但物理边贴合 ⇒ 仍然算接触。
    /// </summary>
    [Fact]
    public void 跨缩放接触_按物理投影判定()
    {
        var left = new ModuleLayoutView("a", 0, 0, 200, 100) { Scale = 1.0 };
        var right = new ModuleLayoutView("b", 200.0 / 1.5, 0, 200, 100) { Scale = 1.5 };

        DockRule.TryGetContact(left, right, out var aIsLeftOrTop, out var horizontal).ShouldBeTrue();

        aIsLeftOrTop.ShouldBeTrue();
        horizontal.ShouldBeTrue();
    }

    private static void Place(ModuleRuntime runtime, string instanceId, double x, double y, double width, double height) =>
        runtime.Apply(ChangeSet.OfOne(new UpdateModuleLayoutOperation(
            new ModuleLayoutView(instanceId, x, y, width, height)))).Applied.ShouldBeTrue();

    /// <summary>设置端口沿边偏移（不改边、不改标签）。</summary>
    private static void SetPortOffset(ModuleRuntime runtime, string instanceId, string portName, double offset) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
            instanceId, new PortGeometryView(portName, offset)))).Applied.ShouldBeTrue();

    /// <summary>同时设置端口所在边与沿边偏移（不改标签）。</summary>
    private static void SetPort(
        ModuleRuntime runtime,
        string instanceId,
        string portName,
        double offset,
        PortEdge edge) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
            instanceId, new PortGeometryView(portName, offset, Edge: edge)))).Applied.ShouldBeTrue();

    /// <summary>给端口设置网络标签（走真实变更入口 → 触发推导）。</summary>
    private static void SetLabel(ModuleRuntime runtime, string instanceId, string portName, string? label) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortNetworkOperation(instanceId, portName, label))).Applied.ShouldBeTrue();

    private static void AddModule(ModuleRuntime runtime, string instanceId, string name, Type entryType) =>
        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, TestDefinition(name, entryType))))
            .Applied.ShouldBeTrue();

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(ModuleId.Create("test", name),
            name, string.Empty, [], ModuleCapability.None, [], true,
            entryType, TestViewPackages.For(entryType), string.Empty);

    /// <summary>发送模块（f32 输出口；边在包内声明里给出 ⇒ 有效边由"用户覆盖 &gt; 声明值"决定）。</summary>
    [ModuleDefinition("test", "sender")]
    public sealed class SenderModule : IModule
    {
        /// <summary>输出口名。</summary>
        public const string OutPort = "out";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                OutPort, PortDirection.Send, Sdk.Contracts.ContractShape.OfPrimitive(Sdk.Contracts.ContractPrimitive.F32)));
    }

    /// <summary>接收模块（f32 输入口）。</summary>
    [ModuleDefinition("test", "receiver")]
    public sealed class ReceiverModule : IModule
    {
        /// <summary>输入口名。</summary>
        public const string InPort = "in";

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                InPort, PortDirection.Receive, Sdk.Contracts.ContractShape.OfPrimitive(Sdk.Contracts.ContractPrimitive.F32)));
    }
}
