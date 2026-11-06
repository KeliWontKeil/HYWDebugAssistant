using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **契约自检要能抓出坏声明**：每一类写错都必须当场变成一条 Problem。
/// </summary>
/// <remarks>
/// 载体是故意写坏的测试模块（只在本文件里）；写对的正例用来证明自检不误报。
/// 合法形态与八个正式模块的声明面由 <c>Modules.Tests/BusinessModulesContractTests</c> 负责。
/// </remarks>
public class ModuleContractTests
{
    [Fact]
    public void 内部处理顺序写错_契约自检应当场报出来()
    {
        // 内部处理顺序 = 「**角色 → 角色**」：两端角色必须已声明、不得自环、同（起点, 终点）不得重复。
        var problems = ModuleContract.CheckAll(typeof(BrokenFlowModule)).ToList();

        problems.Count.ShouldBe(3, "自环 + 未声明角色 + 重复声明各一条");
    }

    [Fact]
    public void 角色声明写错_契约自检应当场报出来()
    {
        // SDK 侧自检与框架侧（运行期忽略 + Issue）**同一套规则**：
        // Id 唯一 · 类型可在本模块解析 · 口存在且方向相符 · 一个口最多属于一个角色 · 至少一个口。
        var problems = ModuleContract.CheckAll(typeof(BrokenRoleModule)).ToList();

        problems.Count.ShouldBeGreaterThanOrEqualTo(5, "角色声明规则至少各报一条");

        // **口数不设上限**：3 个输出的角色是合法形态（融合块就是 2 入 1 出）⇒ 只给**建议**，不进 Problems
        var probe = ModuleContract.Probe(typeof(BrokenRoleModule));
        probe.DeclaredRoles.ShouldContain(role => role.Id == "many" && role.Outputs.Count == 3, "多口角色照常成立");
        probe.Advisories.ShouldHaveSingleItem("三输出应当得到一条建议");
        problems.Count.ShouldBeGreaterThanOrEqualTo(5, "建议**不是问题** ⇒ Problems 仍包含那五条");
    }

    [Fact]
    public void 有效长度字段写错_契约自检应当场报出来()
    {
        // `MeteringBlock.PayloadLengthField` 是「逐单元读哪个字段」的声明：
        // 写错只会表现为“有效速率恒等于搬运速率”这种静默归零 ⇒ 加载期必须明确报出来。
        var problems = ModuleContract.CheckAll(typeof(BrokenMeteringModule)).ToList();

        problems.Count.ShouldBe(2, "字段名拼错 + 有效长度字段类型错 ⇒ 各报一条");

        // 写对的形态（形状里有 `len:u16`）⇒ 不得报任何问题
        ModuleContract.CheckAll(typeof(GoodMeteringModule)).ShouldBeEmpty(
            string.Join("；", ModuleContract.CheckAll(typeof(GoodMeteringModule))));
    }

    [Fact]
    public void 契约自检_应能发现重复口名()
    {
        var problems = ModuleContract.CheckAll(typeof(BrokenModule)).ToList();

        problems.ShouldNotBeEmpty();
    }

    [Fact]
    public void 契约自检_应发现端口形状与所引用类型形状不一致()
    {
        var probe = ModuleContract.Probe(typeof(ShapeMismatchContractModule));

        // 判据取“端口被点名”，不钉文案（文案是资源表的事）。
        probe.Problems.ShouldContain(problem => problem.Contains("out", StringComparison.Ordinal));
    }

    /// <summary>
    /// **有效长度字段写错**的计量模块：① 字段名在形状里不存在 ② 字段存在但不是整型。
    /// </summary>
    [ModuleDefinition("test", "broken-metering")]
    public sealed class BrokenMeteringModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfStruct(
                ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
                ("data", ContractShape.OfBytes(16)));

            // ① 字段名拼错：形状里没有 `length`
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-metering-a", shape)
            {
                Metering = new MeteringBlock { PayloadLengthField = "length" },
            });

            // ② 字段存在但**不是整型**（浮点不能当字节数）
            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-metering-b", shape)
            {
                Metering = new MeteringBlock { PayloadLengthField = "data" },
            });
        }
    }

    /// <summary>**有效长度字段写对**的计量模块（对照组：不得报任何问题）。</summary>
    [ModuleDefinition("test", "good-metering")]
    public sealed class GoodMeteringModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfStruct(
                ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
                ("data", ContractShape.OfBytes(16)));

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/good-metering", shape)
            {
                Metering = new MeteringBlock { PayloadLengthField = "len" },
            });
        }
    }

    /// <summary>故意写坏的模块（验证契约自检**确实能发现问题**）。</summary>
    [ModuleDefinition("test", "broken")]
    public sealed class BrokenModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

            context.Declaration.DeclarePort(new PortDeclaration("dup", PortDirection.Send, shape));
            context.Declaration.DeclarePort(new PortDeclaration("dup", PortDirection.Receive, shape));
        }
    }

    /// <summary>内部处理顺序写错的模块：自环 / 引用了未声明的角色 / 重复。</summary>
    [ModuleDefinition("test", "broken-flow")]
    public sealed class BrokenFlowModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var type = "test/broken-flow";

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-flow", shape));
            context.Declaration.DeclarePort(new PortDeclaration("in1", PortDirection.Receive, shape) { TypeId = type });
            context.Declaration.DeclarePort(new PortDeclaration("out1", PortDirection.Send, shape) { TypeId = type });
            context.Declaration.DeclarePort(new PortDeclaration("in2", PortDirection.Receive, shape) { TypeId = type });

            context.Declaration.DeclareRole(RoleDeclaration.Both("a", type, "in1", "out1", "甲"));
            context.Declaration.DeclareRole(RoleDeclaration.Input("b", type, "in2", "乙"));

            context.Declaration.DeclareFlow(new FlowDeclaration("a", "a"));          // 自环
            context.Declaration.DeclareFlow(new FlowDeclaration("nope", "b"));      // 引用了未声明的角色
            context.Declaration.DeclareFlow(new FlowDeclaration("a", "b"));         // 合法
            context.Declaration.DeclareFlow(new FlowDeclaration("a", "b"));         // 重复
        }
    }

    /// <summary>角色声明写错的模块：重复 Id / 类型未声明 / 口不存在 / 方向不符 / 口被两个角色占 / 3 个口。</summary>
    [ModuleDefinition("test", "broken-role")]
    public sealed class BrokenRoleModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.F32);
            var known = "test/broken-role";

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/broken-role", shape));
            context.Declaration.DeclarePort(new PortDeclaration("in", PortDirection.Receive, shape) { TypeId = known });
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, shape) { TypeId = known });

            for (var i = 1; i <= 3; i++)
            {
                context.Declaration.DeclarePort(new PortDeclaration($"out{i}", PortDirection.Send, shape) { TypeId = known });
            }

            context.Declaration.DeclareRole(RoleDeclaration.Both("dup", known, "in", "out", "甲"));
            context.Declaration.DeclareRole(RoleDeclaration.Both("dup", known, "in", "out", "甲（重复 Id）"));   // ① Id 重复
            context.Declaration.DeclareRole(RoleDeclaration.Both(
                "no-type", "test/missing", "in", "out"));                                   // ② 类型未声明
            context.Declaration.DeclareRole(RoleDeclaration.Both("no-port", known, "nope", "out"));            // ③ 口不存在
            context.Declaration.DeclareRole(RoleDeclaration.Output("wrong-dir", known, "in"));                 // ④ 方向不符（拿接收口当输出）
            context.Declaration.DeclareRole(RoleDeclaration.Input("steal", known, "in"));                      // ⑤ 口已被 dup 占用
            context.Declaration.DeclareRole(new RoleDeclaration(
                "many", known, [], ["out1", "out2", "out3"], "三输出"));                                        // ⑥ 口数 > 2：**合法**（不再提示）
        }
    }

    /// <summary>口形状与所引用类型形状不一致（传输与计量会各读一份）。</summary>
    [ModuleDefinition("test", "shape-mismatch-contract")]
    public sealed class ShapeMismatchContractModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var declared = ContractShape.OfStruct(
                ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
                ("data", ContractShape.OfBytes(16)));

            var portShape = ContractShape.OfStruct(
                ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
                ("data", ContractShape.OfBytes(32)));

            context.Declaration.DeclareDataType(new DataTypeDeclaration("test/shape-mismatch", declared));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, portShape)
            {
                TypeId = "test/shape-mismatch",
            });
        }
    }
}
