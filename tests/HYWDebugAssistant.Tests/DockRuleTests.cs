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
/// 判据只有三条：① 跨缩放时按**物理投影**判接触；② **接触且标记对齐**才建网（两条独立条件）；
/// ③ 打了标签的口退出接触建网（标签是命名网络的唯一机制）。
/// 契约不兼容的拒绝由 <c>CoreRuntimeTests.对齐建网_契约不兼容时不得建网</c> 覆盖。
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

    /// <summary>**标记必须对齐**（沿接触面中心差 ≤ 12px），错开就不连 —— 与"接触"是两条独立条件。</summary>
    [Fact]
    public void 建网_标记未对齐时不得建网()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s", "sender", typeof(SenderModule));
        AddModule(runtime, "r", "receiver", typeof(ReceiverModule));

        Place(runtime, "s", 0, 0, 300, 200);
        Place(runtime, "r", 300, 0, 300, 200);

        // 偏移 0.5 vs 0.05 ⇒ 中心相差 90px（容差 12px）
        SetPortOffset(runtime, "s", "out", 0.05);

        runtime.Content.Networks.ShouldBeEmpty("接触但标记错开 90px ⇒ 不建网（严防误建）");

        // 对齐之后建网（证明上一条不是"什么都不连"）
        SetPortOffset(runtime, "s", "out", 0.5);
        runtime.Content.Networks.ShouldHaveSingleItem();
    }

    /// <summary>
    /// **x 轴相等、上下相邻**也能建网：接触面是水平线，端口必须挂在下/上边且标记对齐。
    /// </summary>
    /// <remarks>
    /// 这条用例把“上下拼接本身”与“拖动预览是否刷新”分开：Core 判定通过后，UI 不高亮就只能是
    /// 预览刷新链路的问题，而不是 `DockRule` 的上下路径坏了。
    /// </remarks>
    [Fact]
    public void 上下拼接_标记对齐时应建网()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s", "sender", typeof(SenderModule));
        AddModule(runtime, "r", "receiver", typeof(ReceiverModule));

        Place(runtime, "s", 0, 0, 300, 200);
        Place(runtime, "r", 0, 200, 300, 200);

        SetPort(runtime, "s", "out", 0.5, PortEdge.Bottom);
        SetPort(runtime, "r", "in", 0.5, PortEdge.Top);

        runtime.Content.Networks.ShouldHaveSingleItem("x 相等、上下贴边、标记对齐 ⇒ 应建网");

        // 标记沿接触面错开 ⇒ 仍不建网（对齐是独立条件）
        SetPort(runtime, "s", "out", 0.05, PortEdge.Bottom);
        runtime.Content.Networks.ShouldBeEmpty("上下接触但标记错开 ⇒ 不建网");
    }

    /// <summary>**打了标签的口不参与**接触建网（标签 = 命名网络的唯一机制，两种机制不混用）。</summary>
    [Fact]
    public void 建网_打了标签的口不参与()
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s", "sender", typeof(SenderModule));
        AddModule(runtime, "r", "receiver", typeof(ReceiverModule));

        Place(runtime, "s", 0, 0, 300, 200);
        Place(runtime, "r", 300, 0, 300, 200);

        SetLabel(runtime, "s", "out", "bus");

        runtime.Content.Networks.ShouldBeEmpty("发送口打了标签 ⇒ 退出接触建网（等同名接收端）");
    }

    // ------------------------------------------------------------ 辅助

    /// <summary>摆放模块（写入布局）并触发一次推导（等价于"拖动结束提交"）。</summary>
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
