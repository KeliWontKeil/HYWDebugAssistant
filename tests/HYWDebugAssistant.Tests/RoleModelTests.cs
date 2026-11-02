using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1【判据 1–4】：**角色 = 模块声明的功能分块**——声明、`m/n` 派生、非法声明与「不参与判定」。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>角色来自声明</b>（一种类型 + 输入口/输出口），**不再由框架派生**；</item>
///   <item><b>`m/n` 是框架派生量</b>：输入/输出侧连接的**对端角色数**（去重键 =（对端实例, 对端角色））；
///     **无该方向的口 ⇒ null**（界面写 `--`）；**有口未连 ⇒ 0**；同一对端角色的多个口仍算 **1**；</item>
///   <item><b>只呈现</b>：加/删/改角色声明**不改变**网络与投递结果（反向断言）。</item>
/// </list>
/// </remarks>
public class RoleModelTests
{
    private static readonly ContractShape Sample = ContractShape.OfStruct(
        ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    private const string TypeId = "test/role-sample";

    [Fact]
    public void 判据1_声明一个角色就画一个方块_与方向或类型数量无关()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "one", "role-single", typeof(SingleRoleModule));
        Add(runtime, "two", "role-double", typeof(DoubleRoleModule));

        var single = services.GetRoles("one").ShouldHaveSingleItem();
        single.RoleId.ShouldBe("only");
        single.DisplayName.ShouldNotBeNullOrWhiteSpace("功能名来自角色声明");
        single.Inputs.ShouldBe(["in"]);
        single.Outputs.ShouldBe(["out"]);
        single.TypeKey.ShouldBe(TypeId);

        var two = services.GetRoles("two");
        two.Count.ShouldBe(2, "声明两个角色 ⇒ 两个方块（与「方向×类型」的组合数无关）");
        two.Select(r => r.RoleId).ShouldBe(["first", "second"], "顺序 = 声明顺序");
    }

    [Fact]
    public void 判据2_无该侧口为null_有口未连为0_连到两个对端角色为2_同对端多口仍算1()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "hub", "role-hub", typeof(HubModule));          // 1 入 2 出（out1/out2）
        Add(runtime, "a", "role-sink-a", typeof(SinkA));
        Add(runtime, "b", "role-sink-b", typeof(SinkB));

        // 未连：有输入口与输出口 ⇒ 两侧都应是 0（而不是 null）
        var before = services.GetRoles("hub").ShouldHaveSingleItem();
        before.InboundRoleCount.ShouldBe(0, "有输入口但没连 ⇒ 0");
        before.OutboundRoleCount.ShouldBe(0, "有输出口但没连 ⇒ 0");

        // 连上：本角色两个输出口指向同一个对端角色 ⇒ 仍只算 1 个对端角色
        Connect(services, "hub", "out1", "a", "in", "n1");
        Connect(services, "hub", "out2", "a", "in", "n2");

        var one = services.GetRoles("hub").ShouldHaveSingleItem();
        one.OutboundRoleCount.ShouldBe(1, "同一对端角色的多个口仍算 1 个对端角色");

        // 再连第二个对端 ⇒ 2
        Connect(services, "hub", "out1", "b", "in", "n3");
        services.GetRoles("hub").ShouldHaveSingleItem().OutboundRoleCount.ShouldBe(2);

        // 纯源/纯汇：没有那一侧 ⇒ null（界面写 `--`）
        var sink = services.GetRoles("a").ShouldHaveSingleItem();
        sink.Outputs.ShouldBeEmpty();
        sink.OutboundRoleCount.ShouldBeNull("没有输出口 ⇒ null（`--`），与「有口未连 = 0」含义不同");
        sink.InboundRoleCount.ShouldBe(1);
    }

    [Fact]
    public void 判据3_非法角色声明应报Issue并忽略_其余照常()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "bad", "role-bad", typeof(BadRoleModule));

        var roles = services.GetRoles("bad");
        roles.Count.ShouldBe(1, "只有那条合法声明生效");
        roles[0].RoleId.ShouldBe("good");

        var issues = runtime.Issues.Recent(20).Select(e => e.Issue).ToList();
        issues.Count.ShouldBe(4, "四条角色声明问题各一条");
        issues.ShouldAllBe(i => i.Category == HYWDebugAssistant.Sdk.Reporting.IssueCategory.InterfaceError);
    }

    [Fact]
    public void 判据3b_口数不设上限_多口角色照常生效且不报警()
    {
        // 框架**不对角色的口数设限、也不给建议**：它分不清「2 路合成 1 路」（多源融合器，
        // 合法形态）与「两件不相关的事」 ⇒ 说不了的就不说（与「声明 ≠ 证明」一致）。
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Add(runtime, "many", "role-many", typeof(ManyPortRoleModule));

        services.GetRoles("many").ShouldHaveSingleItem().Outputs.Count.ShouldBe(3, "3 个输出口照常生效");
        runtime.Issues.Recent(20).ShouldNotContain(
            e => e.Issue.Severity == HYWDebugAssistant.Sdk.Reporting.IssueSeverity.Error
                || e.Issue.Severity == HYWDebugAssistant.Sdk.Reporting.IssueSeverity.Warning,
            "角色声明合法 ⇒ 不产生任何警告或错误");
    }

    [Fact]
    public void 判据4_角色不参与判定_加删角色声明不改变网络与投递()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        // 两个模块：一个有角色声明、一个没有 —— 建网与兼容结果必须一致
        Add(runtime, "withRole", "role-single", typeof(SingleRoleModule));
        Add(runtime, "noRole", "role-single", typeof(SingleRoleModule));

        TestLink.Link(
            services,
            new HYWDebugAssistant.Core.Api.PortRef("withRole", "out"),
            new HYWDebugAssistant.Core.Api.PortRef("noRole", "in"),
            "link");

        services.GetNetworks().ShouldHaveSingleItem();
        runtime.Bindings.Bindings.Count.ShouldBe(1, "角色不参与绑定构建");

        // 角色视图是**只读派生**：读它不改变任何状态
        _ = services.GetRoles("withRole");
        _ = services.GetRoleFlows("withRole");
        runtime.Bindings.Bindings.Count.ShouldBe(1);
        services.GetNetworks().ShouldHaveSingleItem();
    }

    // ------------------------------------------------------------ 测试辅助

    private static void Add(ModuleRuntime runtime, string instanceId, string name, Type entryType)
    {
        var definition = new ModuleDefinition(
            ModuleId.Create("test", name),
            entryType.Name,
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            true,
            entryType,
            string.Empty,
            string.Empty);

        runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(instanceId, definition))).Applied.ShouldBeTrue();
    }

    private static void Connect(
        RuntimeServices services,
        string sourceInstance,
        string sourcePort,
        string targetInstance,
        string targetPort,
        string name)
    {
        services.UpdatePortNetwork(sourceInstance, sourcePort, name).Succeeded.ShouldBeTrue();
        services.UpdatePortNetwork(targetInstance, targetPort, name).Succeeded.ShouldBeTrue();
        TestLink.HasNetwork(services, name).ShouldBeTrue();
    }

    // ------------------------------------------------------------ 测试模块

    /// <summary>一个角色（一进一出）。</summary>
    [ModuleDefinition("test", "role-single")]
    public sealed class SingleRoleModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Sample)
            {
                TypeId = TypeId,
            });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Sample)
            {
                TypeId = TypeId,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "only", TypeId, "in", "out", "单块"));
        }
    }

    /// <summary>两个角色（各自一进一出，同类型）。</summary>
    [ModuleDefinition("test", "role-double")]
    public sealed class DoubleRoleModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Sample));

            foreach (var (input, output) in new[] { ("in1", "out1"), ("in2", "out2") })
            {
                context.Declaration.DeclarePort(new PortDeclaration(input, PortDirection.Receive, Sample)
                {
                    TypeId = TypeId,
                });
                context.Declaration.DeclarePort(new PortDeclaration(output, PortDirection.Send, Sample)
                {
                    TypeId = TypeId,
                });
            }

            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "first", TypeId, "in1", "out1", "第一块"));
            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "second", TypeId, "in2", "out2", "第二块"));
        }
    }

    /// <summary>一个角色 + 两个输出口（用于 `n` 的去重与"未连=0"）。</summary>
    [ModuleDefinition("test", "role-hub")]
    public sealed class HubModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Sample)
            {
                TypeId = TypeId,
            });
            context.Declaration.DeclarePort(new PortDeclaration("out1", PortDirection.Send, Sample)
            {
                TypeId = TypeId,
            });
            context.Declaration.DeclarePort(new PortDeclaration("out2", PortDirection.Send, Sample)
            {
                TypeId = TypeId,
            });

            context.Declaration.DeclareRole(new RoleDeclaration(
                "hub", TypeId, ["in"], ["out1", "out2"], "转发"));
        }
    }

    /// <summary>接收端 A（一个角色、一个输入口）。</summary>
    [ModuleDefinition("test", "role-sink-a")]
    public sealed class SinkA : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) => DeclareSink(context, "sink-a");
    }

    /// <summary>接收端 B。</summary>
    [ModuleDefinition("test", "role-sink-b")]
    public sealed class SinkB : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) => DeclareSink(context, "sink-b");
    }

    /// <summary>四条非法声明 + 一条合法（判据 3）。</summary>
    [ModuleDefinition("test", "role-bad")]
    public sealed class BadRoleModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Sample));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Sample)
            {
                TypeId = TypeId,
            });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Sample)
            {
                TypeId = TypeId,
            });

            // ① 引用了未声明的类型
            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "no-type", "test/missing", "in", "out"));

            // ② 引用了不存在的口
            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "no-port", TypeId, "nope", "out"));

            // ③ 方向不符（把发送口当输入口）
            context.Declaration.DeclareRole(RoleDeclaration.Input("wrong-dir", TypeId, "out"));

            // ④ 口已被合法角色占用
            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "good", TypeId, "in", "out", "合法"));
            context.Declaration.DeclareRole(RoleDeclaration.Input("steal", TypeId, "in"));
        }
    }

    /// <summary>一个角色但有 3 个输出口（判据 3b：口数不设上限）。</summary>
    [ModuleDefinition("test", "role-many")]
    public sealed class ManyPortRoleModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Sample));

            var outputs = new List<string>();

            for (var i = 1; i <= 3; i++)
            {
                var name = $"out{i}";
                outputs.Add(name);
                context.Declaration.DeclarePort(new PortDeclaration(name, PortDirection.Send, Sample)
                {
                    TypeId = TypeId,
                });
            }

            context.Declaration.DeclareRole(new RoleDeclaration(
                "many", TypeId, [], outputs, "多输出"));
        }
    }

    private static void DeclareSink(IModuleContext context, string roleId)
    {
        context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Sample));
        context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, Sample)
        {
            TypeId = TypeId,
        });
        context.Declaration.DeclareRole(RoleDeclaration.Input(roleId, TypeId, "in", "接收"));
    }
}
