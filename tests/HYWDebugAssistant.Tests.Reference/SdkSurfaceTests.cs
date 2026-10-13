using System.Reflection;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// SDK 面冒烟测试（M1-3）：验证**模块作者可用的契约面**确实可用——AC-1「新模块 Core 零改动接入」的起点。
/// </summary>
public class SdkSurfaceTests
{
    [Fact]
    public void 模块标识_字面量应可往返解析()
    {
        var id = ModuleId.Create("hyw", "serial-port", "1.2.3");

        id.ToString().ShouldBe("hyw/serial-port/1.2.3");
        ModuleId.TryParse("hyw/serial-port/1.2.3", out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Theory]
    [InlineData("hyw/serial-port")]          // 段数不足
    [InlineData("HYW/serial/1.0.0")]         // 大写非法
    [InlineData("hyw/serial/1.0")]           // 版本非三段
    [InlineData("hyw//1.0.0")]               // 空模块名
    [InlineData("-hyw/serial/1.0.0")]        // 段首连字符
    public void 模块标识_非法字面量应被拒绝(string literal)
    {
        ModuleId.TryParse(literal, out _).ShouldBeFalse();
    }

    [Fact]
    public void 契约形状_定长尺寸应可计算()
    {
        // { seq: u64, value: f32 } = 8 + 4 = 12 字节
        var shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

        shape.SizeInBytes.ShouldBe(12);
        shape.Depth.ShouldBe(2);
    }

    [Fact]
    public void 契约形状_定长数组与字节块应可计算()
    {
        ContractShape.OfArray(ContractPrimitive.U16, 8).SizeInBytes.ShouldBe(16);
        ContractShape.OfBytes(32).SizeInBytes.ShouldBe(32);
    }

    [Fact]
    public void 契约形状_超过定长上限应被拒绝()
    {
        // v1 无变长：数组与字节块长度上限 256（C3-5）
        Should.Throw<ArgumentOutOfRangeException>(() => ContractShape.OfArray(ContractPrimitive.U8, 257));
        Should.Throw<ArgumentOutOfRangeException>(() => ContractShape.OfBytes(0));
    }

    [Fact]
    public void 契约形状_结构嵌套深度超过上限应被拒绝()
    {
        // 深度定义：顶层原语 = 1，每层结构 +1；上限 4（C3-5）
        var level1 = ContractShape.OfStruct(("a", ContractShape.OfPrimitive(ContractPrimitive.U8)));  // 2
        var level2 = ContractShape.OfStruct(("b", level1));                                           // 3
        var level3 = ContractShape.OfStruct(("c", level2));                                           // 4（上限内）

        level3.Depth.ShouldBe(4);
        Should.Throw<ArgumentException>(() => ContractShape.OfStruct(("d", level3)));                 // 5 → 拒绝
    }

    [Fact]
    public void 正式模块_声明特性应可被加载期读取()
    {
        // 判据由**正式模块**承载（参考桩模块不承载这条契约）
        var attribute = typeof(TextSenderModule).GetCustomAttribute<ModuleDefinitionAttribute>();

        attribute.ShouldNotBeNull();
        attribute!.Id.ToString().ShouldBe("hyw/text-sender/1.0.0");
        attribute.DisplayName.ShouldBe("文本发送器");
        attribute.ParticipatesInDataFlow.ShouldBeTrue();
    }

    [Fact]
    public void 正式模块_应可实例化并声明口与数据类型()
    {
        var module = new TextSenderModule();
        module.ShouldBeAssignableTo<IModule>();
        module.ShouldBeAssignableTo<IActivitySource>();
        module.Kind.ShouldBe(ActivityKind.Periodic);

        var context = new DeclarationRecorder();
        module.OnRegistered(context);

        context.DeclaredPorts.Count.ShouldBe(1);
        context.DeclaredPorts[0].Name.ShouldBe(TextSenderModule.OutPort);
        context.DeclaredPorts[0].Direction.ShouldBe(PortDirection.Send);
        context.DeclaredPorts[0].Shape.SizeInBytes.ShouldBe(2 + 1472, "`{ len:u16, data:bytes[1472] }`");
        context.DeclaredPorts[0].ParticipatesInAutoNetwork.ShouldBeTrue();

        // 口的默认边只有一个来源：包内 `view.json`（`PortDeclaration.Edge` 不存在）
        // ⇒ 这条事实由 `BusinessModulesContractTests.DeclaredEdge` 从包里断言（模块代码里再写会被 AR-18 判红）。

        context.DeclaredDataTypes.ShouldHaveSingleItem().TypeId.ShouldBe(TextSenderModule.RawBytesTypeId);

        // 视图（控件树、数据源、命令）**不在模块代码里声明**：整份声明在包内 `view.json`，
        // 由框架的读取器按 schema 解析 ⇒ 模块作者面到这里为止。
    }

    /// <summary>
    /// 测试替身：只实现声明面，其余服务在 M1-3 冒烟中不使用（完整夹具见 M1-12 的参考组合）。
    /// </summary>
    private sealed class DeclarationRecorder : IModuleContext
    {
        public DeclarationRecorder() => Declaration = new DeclarationSink(DeclaredPorts, DeclaredDataTypes);

        public List<PortDeclaration> DeclaredPorts { get; } = [];

        /// <summary>已声明的数据类型。</summary>
        public List<HYWDebugAssistant.Sdk.Contracts.DataTypeDeclaration> DeclaredDataTypes { get; } = [];

        public ModuleId ModuleId => ModuleId.Create("hyw", "test-host", "1.0.0");

        public IModuleDeclaration Declaration { get; }

        /// <summary>元素外观的设定面 —— 本替身不使用。</summary>
        public HYWDebugAssistant.Sdk.Context.IViewAppearanceAccess Appearance => null!;

        public IPortAccess Ports => null!;

        public IConfigurationAccess Configuration => null!;

        public IIssueReporter Issues => null!;

        public IModuleLogger Log => null!;

        public IBusinessStateReporter State => null!;

        public IStopSignal StopSignal => null!;

        public HYWDebugAssistant.Sdk.Context.IModuleInbox Inbox => null!;

        /// <summary>视图状态回读——本替身不使用。</summary>
        public HYWDebugAssistant.Sdk.Context.IViewStateReader ViewState => null!;

        /// <summary>显示内容请求面——本替身不使用。</summary>
        public HYWDebugAssistant.Sdk.Context.IViewDisplayAccess Display => null!;

        /// <summary>文件选择请求面——本替身不使用。</summary>
        public HYWDebugAssistant.Sdk.Context.IFileAccess Files => null!;

        private sealed class DeclarationSink(
            List<PortDeclaration> ports,
            List<HYWDebugAssistant.Sdk.Contracts.DataTypeDeclaration> types)
            : IModuleDeclaration
        {
            public void DeclareDataType(HYWDebugAssistant.Sdk.Contracts.DataTypeDeclaration type) => types.Add(type);

            public void DeclarePort(PortDeclaration port) => ports.Add(port);

            public void DeclareRole(HYWDebugAssistant.Sdk.Contracts.RoleDeclaration role)
            {
                // 测试替身：角色只用于呈现，本替身不记录
            }

            public void DeclareFlow(FlowDeclaration flow)
            {
                // 测试替身：内部处理关系只用于呈现，本替身不记录
            }
        }
    }
}
