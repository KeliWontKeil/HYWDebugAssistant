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
/// <para>
/// <b>为什么专门写这个文件</b>：`DockRule` 是"接触即连"的**唯一判据**（真正的建网与拖动中的
/// "连接对齐"吸附预览都读它），缺了直接用例就没有任何东西拦得住它的退化（别处的注释若引用一个
/// 不存在的 `DockRuleTests`，等于没有护栏）⇒ 下面这条缺陷无人能拦：
/// </para>
/// <para>
/// <b>被钉住的缺陷</b>：网络标签解锁并换边拖动后，网络标签吸附无效，**只有在默认边才能吸附**：
/// 旧实现把**左/上那个模块直接当成发送端**（`var sender = aIsLeftOrTop ? a : b;`），只在它里面找发送口
/// ⇒ 只要发送端不在左/上（恰恰是"反着摆 + 把标签拖到另一边"的情形），候选**恒为空** ⇒ 建网与吸附同时静默失效。
/// 现在方向由**端口自己**（`IsSend`）+ **接触几何**（谁朝谁）共同决定，**四种摆法都成立**。
/// </para>
/// <para>
/// 用例故意用**真实运行时**（摆放 → 写端口几何 → 看派生网络），而不是直接构造 `DockEnd`：
/// 被判定影响的是"用户拖动之后到底连没连上"，走真实链路才拦得住"预览与落地不一致"。
/// </para>
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

    /// <summary>
    /// **四种摆法都必须连上**（发送端在左/右/上/下都能连，只要两个口各自冲着对方）。
    /// </summary>
    /// <param name="senderFirst">发送端是否摆在"左/上"那一侧。</param>
    /// <param name="horizontal">是否左右并排（false = 上下叠放）。</param>
    /// <remarks>
    /// 旧实现下，<c>senderFirst = false</c> 的两行（发送端在右/下）**必然红** —— 那正是用户报的"换边之后吸不住"。
    /// </remarks>
    [Theory]
    [InlineData(true, true)]     // 左右并排：发送在左（数据向右流）
    [InlineData(false, true)]    // 左右并排：发送在右（用户把标签换了边）
    [InlineData(true, false)]    // 上下叠放：发送在上（数据向下流）
    [InlineData(false, false)]   // 上下叠放：发送在下（用户把标签换了边）
    public void 建网_四种摆法都应连上(bool senderFirst, bool horizontal)
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s", "sender", typeof(SenderModule));
        AddModule(runtime, "r", "receiver", typeof(ReceiverModule));

        // 几何：左右并排 or 上下叠放；`senderFirst` 决定谁在"左/上"
        var (senderX, senderY, receiverX, receiverY) = (senderFirst, horizontal) switch
        {
            (true, true) => (0d, 0d, 300d, 0d),
            (false, true) => (300d, 0d, 0d, 0d),
            (true, false) => (0d, 0d, 0d, 200d),
            _ => (0d, 200d, 0d, 0d),
        };

        Place(runtime, "s", senderX, senderY, 300, 200);
        Place(runtime, "r", receiverX, receiverY, 300, 200);

        // 两个口各自挂到**朝向接触面的那条边**（= 用户把标签拖过去之后的状态）
        var (senderEdge, receiverEdge) = horizontal
            ? (senderFirst ? PortEdge.Right : PortEdge.Left, senderFirst ? PortEdge.Left : PortEdge.Right)
            : (senderFirst ? PortEdge.Bottom : PortEdge.Top, senderFirst ? PortEdge.Top : PortEdge.Bottom);

        SetPortEdge(runtime, "s", "out", senderEdge);
        SetPortEdge(runtime, "r", "in", receiverEdge);

        var network = runtime.Content.Networks.ShouldHaveSingleItem(
            $"发送端在{(senderFirst ? "左/上" : "右/下")}、{(horizontal ? "左右并排" : "上下叠放")}、"
            + $"两口各自朝向接触面（{senderEdge}/{receiverEdge}）⇒ 必须建网"
            + "（旧实现只认『左/上那个模块是发送端』，这一种恒不建网）");

        network.Source.InstanceId.ShouldBe("s");
        network.Targets.ShouldHaveSingleItem().InstanceId.ShouldBe("r");
    }

    /// <summary>
    /// **口不朝接触面 ⇒ 不建网**（必须有护栏，否则"放宽方向"会把这类误判放回来）。
    /// </summary>
    /// <remarks>
    /// 误判形态是"**明明对齐的下侧却导致左右两侧的吸附**"：上下相邻的两个模块，
    /// 若一个把口挂在下边、另一个挂在上边之外的方向（各自冲着别的方向），两个标记**永远不会真的接触**
    /// ⇒ 判定必须拒绝，且容差再宽也不行。
    /// </remarks>
    [Theory]
    [InlineData(PortEdge.Left, PortEdge.Right)]     // 都朝"左右"（这个摆法是上下相邻 ⇒ 冲着的不是接触面）
    [InlineData(PortEdge.Right, PortEdge.Right)]
    [InlineData(PortEdge.Left, PortEdge.Left)]
    public void 建网_口不朝接触面时不得建网(PortEdge senderEdge, PortEdge receiverEdge)
    {
        using var runtime = new ModuleRuntime();
        AddModule(runtime, "s", "sender", typeof(SenderModule));
        AddModule(runtime, "r", "receiver", typeof(ReceiverModule));

        // 上下相邻（发送在上）：接触面是"发送的下边 ↔ 接收的上边"
        Place(runtime, "s", 0, 0, 300, 200);
        Place(runtime, "r", 0, 200, 300, 200);

        SetPortEdge(runtime, "s", "out", senderEdge);
        SetPortEdge(runtime, "r", "in", receiverEdge);

        runtime.Content.Networks.ShouldBeEmpty(
            $"上下相邻时，两个口必须各自朝下/朝上；此处是 {senderEdge}/{receiverEdge} ⇒ 不得建网（防误判）");
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

    /// <summary>
    /// **吸附预览与真实建网同源**（`SuggestAnchorDock` 复用的就是 `DockRule`）。
    /// </summary>
    /// <remarks>
    /// 这条是"预览不骗人"的可执行版本：**反着摆**（发送在右下）时，把发送口拖到朝接触面的边上，
    /// 框架必须给出"吸到哪里就能连上"的建议 —— 旧实现恒为 <see langword="null"/>（用户看到的"吸不住"）。
    /// </remarks>
    [Fact]
    public void 吸附建议_反着摆时应给出可连上的位置()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        AddModule(runtime, "s", "sender", typeof(SenderModule));
        AddModule(runtime, "r", "receiver", typeof(ReceiverModule));

        // 上下叠放、**发送在下面**（反着摆）
        Place(runtime, "r", 0, 0, 300, 200);
        Place(runtime, "s", 0, 200, 300, 200);

        // 接收口挂在下边（朝接触面）、发送口仍在"未拖过的下边"上
        SetPortEdge(runtime, "r", "in", PortEdge.Bottom);

        // 拖动中的预览：发送口在**上边**（朝接触面），偏移偏离中点 6px（0.52）⇒ 应给出"吸到对端口中心"的建议
        var hint = services.SuggestAnchorDock("s", "out", PortEdge.Top, 0.52);

        hint.ShouldNotBeNull("两个口都能朝向接触面 ⇒ 必须给出吸附建议（旧实现在这里恒为 null）");
        hint!.PeerInstanceId.ShouldBe("r");
        hint.PeerPortName.ShouldBe("in");
        hint.Offset.ShouldBe(0.5, 0.001, "对端口在边缘中点 ⇒ 应吸到中点");

        // **边界（现状即如此，写下来免得被误读成缺陷）**：偏出"标记对齐容差"（12px）就不再给建议 ——
        // 吸附的语义是"你已经差不多对准了，我帮你严丝合缝"，不是"从很远的地方把你拽过去"。
        services.SuggestAnchorDock("s", "out", PortEdge.Top, 0.62)
            .ShouldBeNull("偏出对齐容差 ⇒ 不给建议（0.62 距中点 36px > 12px）");
    }

    // ------------------------------------------------------------ 辅助

    /// <summary>摆放模块（写入布局）并触发一次推导（等价于"拖动结束提交"）。</summary>
    private static void Place(ModuleRuntime runtime, string instanceId, double x, double y, double width, double height) =>
        runtime.Apply(ChangeSet.OfOne(new UpdateModuleLayoutOperation(
            new ModuleLayoutView(instanceId, x, y, width, height)))).Applied.ShouldBeTrue();

    /// <summary>设置端口**用户覆盖边**（= 用户把网络标签拖到另一条边之后的状态）。</summary>
    private static void SetPortEdge(ModuleRuntime runtime, string instanceId, string portName, PortEdge edge) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
            instanceId, new PortGeometryView(portName, 0.5, IsAnchorFixed: null, Label: null, Edge: edge))))
            .Applied.ShouldBeTrue();

    /// <summary>设置端口沿边偏移（不改边、不改标签）。</summary>
    private static void SetPortOffset(ModuleRuntime runtime, string instanceId, string portName, double offset) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortGeometryOperation(
            instanceId, new PortGeometryView(portName, offset)))).Applied.ShouldBeTrue();

    /// <summary>给端口设置网络标签（走真实变更入口 → 触发推导）。</summary>
    private static void SetLabel(ModuleRuntime runtime, string instanceId, string portName, string? label) =>
        runtime.Apply(ChangeSet.OfOne(new UpdatePortNetworkOperation(instanceId, portName, label))).Applied.ShouldBeTrue();

    private static void AddModule(ModuleRuntime runtime, string instanceId, string name, Type entryType) =>
        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, TestDefinition(name, entryType))))
            .Applied.ShouldBeTrue();

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(ModuleId.Create("test", name, "1.0.0"),
            name, string.Empty, [], ModuleCapability.None, [], null, true,
            entryType, TestViewPackages.For(entryType), string.Empty);

    /// <summary>发送模块（f32 输出口；边在包内声明里给出 ⇒ 有效边由"用户覆盖 &gt; 声明值"决定）。</summary>
    [ModuleDefinition("test", "sender", "1.0.0")]
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
    [ModuleDefinition("test", "receiver", "1.0.0")]
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
