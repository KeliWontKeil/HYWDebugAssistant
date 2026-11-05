using System.Reflection;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Views;
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
        var id = ModuleId.Create("hyw", "serial-port");

        id.ToString().ShouldBe("hyw/serial-port");
        ModuleId.TryParse("hyw/serial-port", out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
    }

    [Theory]
    [InlineData("hyw/serial/1")]             // 三段（旧形态）应被拒绝
    [InlineData("HYW/serial")]               // 大写非法
    [InlineData("hyw//serial")]              // 空模块名
    [InlineData("/serial")]                  // 空作者
    [InlineData("-hyw/serial")]              // 段首连字符
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
        attribute!.Id.ToString().ShouldBe("hyw/text-sender");
        attribute.DisplayName.ShouldBeEmpty("字面量已进文本表（DisplayNameKey）");
        attribute.DisplayNameKey.ShouldBe("module.title");
        attribute.DescriptionKey.ShouldBe("module.description");
        attribute.ParticipatesInDataFlow.ShouldBeTrue();
        attribute.ViewFactory.ShouldNotBeNull("界面由模块自己的视图工厂提供");
        attribute.WindowWidth.ShouldBeGreaterThan(0);
        attribute.WindowHeight.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 正式模块_应可实例化并声明口与界面契约()
    {
        var module = new TextSenderModule();
        module.ShouldBeAssignableTo<IModule>();
        module.ShouldBeAssignableTo<IActivitySource>();

        var activity = (IActivitySource)module;
        activity.Receive.ShouldBe(ReceiveMode.Push);

        var context = new TestModuleContext();
        module.OnRegistered(context);

        context.DeclaredPorts.Count.ShouldBe(1);
        var port = context.DeclaredPorts[0];
        port.Name.ShouldBe(TextSenderModule.OutPort);
        port.Direction.ShouldBe(PortDirection.Send);
        port.Shape.SizeInBytes.ShouldBe(2 + 1472, "`{ len:u16, data:bytes[1472] }`");
        port.Edge.ShouldBe(PortEdge.Bottom, "口的默认边是代码声明的一部分");

        context.DeclaredDataTypes.ShouldHaveSingleItem().TypeId.ShouldBe(TextSenderModule.RawBytesTypeId);

        // **界面契约也在代码里**：显示源 / 输入源 / 命令是视图能读什么、能提交什么的唯一来源。
        context.DeclaredDisplaySources.ShouldContain(source =>
            string.Equals(source.Name, TextSenderModule.StatusSource, StringComparison.Ordinal)
            && source.Kind == ViewDataSourceKind.Text);
        context.DeclaredDisplaySources.ShouldContain(source =>
            string.Equals(source.Name, TextSenderModule.SentSource, StringComparison.Ordinal));
        context.DeclaredInputs.ShouldContain(TextSenderModule.TextSourceOf(0));
        context.DeclaredInputs.ShouldContain(TextSenderModule.IntervalSource);
        context.DeclaredCommands.ShouldContain(command =>
            string.Equals(command.Name, TextSenderModule.SendCommand, StringComparison.Ordinal));
    }
}
