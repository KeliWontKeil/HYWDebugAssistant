using HYWDebugAssistant.Access;
using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.HexStringConverter;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.TcpClient;
using HYWDebugAssistant.Modules.TcpServer;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Modules.Udp;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **八个业务模块的契约自检**：声明面自洽、跨模块连接前提成立。
/// </summary>
/// <remarks>
/// <para>
/// 口径：<see cref="ModuleContract.Probe"/> 的 <c>Problems</c> 与 <c>Advisories</c> 都必须为空。
/// 口的方向 / 边 / 类型全部从**代码声明**读 —— 包目录里没有第二份声明文件。
/// </para>
/// <para>
/// 界面事实（显示源 / 输入 / 命令 / 配置字段）由 `VisualSnapshots` 的视图契约用例覆盖：
/// 那里会真的把 XAML 造出来，再按控件上的键对声明。
/// </para>
/// </remarks>
public class BusinessModulesContractTests
{
    private static readonly ContractShape RawBytesShape = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(1472)));

    // ---------------------------------------------------------------- 基础四模块

    [Fact]
    public void 文本发送器_声明面应自洽()
    {
        var probe = ModuleContract.Probe(typeof(TextSenderModule));

        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty();

        var type = probe.DataTypes.ShouldHaveSingleItem();
        type.TypeId.ShouldBe("RawBytes");
        ContractShape.AreSame(type.Shape, RawBytesShape).ShouldBeTrue("形状 = { len: u16, data: bytes[1472] }（跨模块兼容的前提）");

        var port = probe.Ports.ShouldHaveSingleItem();
        port.Name.ShouldBe("out");
        port.Direction.ShouldBe(PortDirection.Send);
        port.Edge.ShouldBe(PortEdge.Bottom, "宽 > 高 ⇒ 口在下边（与显示器的上边配对）");
        port.TypeId.ShouldBe("RawBytes");

        var role = probe.DeclaredRoles.ShouldHaveSingleItem();
        role.Id.ShouldBe("text-send");
        role.Outputs.ShouldBe(["out"]);
        role.Inputs.ShouldBeEmpty();
    }

    [Fact]
    public void 十六进制发送器_声明面应自洽且与文本发送器同型()
    {
        var probe = ModuleContract.Probe(typeof(HexSenderModule));

        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty();

        var type = probe.DataTypes.ShouldHaveSingleItem();
        type.TypeId.ShouldBe("RawBytes", "**共用同一标识**才能与显示器开箱即连（不同标识默认不兼容）");
        ContractShape.AreSame(type.Shape, RawBytesShape).ShouldBeTrue();

        var port = probe.Ports.ShouldHaveSingleItem();
        port.Edge.ShouldBe(PortEdge.Bottom);
    }

    [Fact]
    public void 数据显示器_声明面应自洽且口在上边()
    {
        var probe = ModuleContract.Probe(typeof(DataMonitorModule));

        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty();

        ContractShape.AreSame(probe.DataTypes.ShouldHaveSingleItem().Shape, RawBytesShape).ShouldBeTrue();

        var port = probe.Ports.ShouldHaveSingleItem();
        port.Name.ShouldBe("in");
        port.Direction.ShouldBe(PortDirection.Receive);
        port.Edge.ShouldBe(PortEdge.Top, "与两个发送器的下边配对 ⇒ 上下叠放即可连");

        var role = probe.DeclaredRoles.ShouldHaveSingleItem();
        role.Id.ShouldBe("monitor");
        role.Inputs.ShouldBe(["in"]);
        role.Outputs.ShouldBeEmpty();
    }

    [Fact]
    public void 转换器_零口零角色应自洽且不出建议()
    {
        var probe = ModuleContract.Probe(typeof(HexStringConverterModule));

        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty("零口零角色**合法且不出建议**（建议的触发条件是'有口却零角色'）");

        probe.Ports.ShouldBeEmpty();
        probe.DataTypes.ShouldBeEmpty();
        probe.DeclaredRoles.ShouldBeEmpty();
    }

    [Fact]
    public void 四个模块_应具备开箱即连的声明前提()
    {
        var text = ModuleContract.Probe(typeof(TextSenderModule));
        var hex = ModuleContract.Probe(typeof(HexSenderModule));
        var monitor = ModuleContract.Probe(typeof(DataMonitorModule));

        // ① 同一个类型标识 + 同一形状（兼容判定：不同标识默认不兼容）
        text.DataTypes[0].TypeId.ShouldBe(hex.DataTypes[0].TypeId);
        text.DataTypes[0].TypeId.ShouldBe(monitor.DataTypes[0].TypeId);
        ContractShape.AreSame(text.DataTypes[0].Shape, monitor.DataTypes[0].Shape).ShouldBeTrue("形状逐字节一致才能连");

        // ② 口朝向同一接触面（上下叠放：发送口在下、接收口在上）
        text.Ports.ShouldHaveSingleItem().Edge.ShouldBe(PortEdge.Bottom);
        hex.Ports.ShouldHaveSingleItem().Edge.ShouldBe(PortEdge.Bottom);
        monitor.Ports.ShouldHaveSingleItem().Edge.ShouldBe(PortEdge.Top);
    }

    // ---------------------------------------------------------------- 外部接入四模块

    /// <summary>
    /// **串口**（第二批样板）：声明面自洽，且与四个基础模块**同型**（同标识 + 同形状 ⇒ 开箱即连）。
    /// </summary>
    [Fact]
    public void 串口_声明面应自洽_且与四个基础模块同型()
    {
        var probe = ModuleContract.Probe(typeof(SerialModule), catalog: new BuiltInExternalAccessCatalog());

        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty();

        var type = probe.DataTypes.ShouldHaveSingleItem();
        type.TypeId.ShouldBe("RawBytes", "**共用同一标识**才能与既有模块开箱即连（不同标识默认不兼容）");
        ContractShape.AreSame(type.Shape, RawBytesShape).ShouldBeTrue("形状 = { len: u16, data: bytes[1472] }（跨模块兼容的前提）");

        probe.Ports.Count.ShouldBe(2);

        var rx = probe.Ports.Single(port => port.Name == "rx");
        rx.Direction.ShouldBe(PortDirection.Send);
        rx.TypeId.ShouldBe("RawBytes");
        rx.Edge.ShouldBe(PortEdge.Bottom, "设备来的字节从下边出去（与数据监视器的上边配对）");

        var tx = probe.Ports.Single(port => port.Name == "tx");
        tx.Direction.ShouldBe(PortDirection.Receive);
        tx.TypeId.ShouldBe("RawBytes");
        tx.Edge.ShouldBe(PortEdge.Top, "发往设备的字节从上边进来（与发送器的下边配对）");

        // **一个分块、一进一出、两侧同型** —— "一个通道 = 一个设备会话"在声明面的样子
        var role = probe.DeclaredRoles.ShouldHaveSingleItem();
        role.Id.ShouldBe("serial");
        role.Inputs.ShouldBe(["tx"], "进 = 发往设备");
        role.Outputs.ShouldBe(["rx"], "出 = 设备来的");
    }

    /// <summary>
    /// **网络类模块**（TCP 客户端 / TCP 服务端 / UDP）：声明面自洽、与基础模块同型、口的分边正确。
    /// </summary>
    /// <param name="entryType">模块入口类型。</param>
    /// <param name="roleId">该模块声明的角色 Id。</param>
    [Theory]
    [InlineData(typeof(TcpClientModule), "tcp-client")]
    [InlineData(typeof(TcpServerModule), "tcp-server")]
    [InlineData(typeof(UdpModule), "udp")]
    public void 网络类模块_声明面应自洽_且与基础模块同型(Type entryType, string roleId)
    {
        var probe = ModuleContract.Probe(entryType, catalog: new BuiltInExternalAccessCatalog());

        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty();

        var type = probe.DataTypes.ShouldHaveSingleItem();
        type.TypeId.ShouldBe("RawBytes", "**共用同一标识**才能与既有模块开箱即连");
        ContractShape.AreSame(type.Shape, RawBytesShape).ShouldBeTrue("形状 = { len: u16, data: bytes[1472] }");

        probe.Ports.Count.ShouldBe(2);
        probe.Ports.Single(port => port.Name == "rx").Direction.ShouldBe(PortDirection.Send);
        probe.Ports.Single(port => port.Name == "tx").Direction.ShouldBe(PortDirection.Receive);
        probe.Ports.Single(port => port.Name == "rx").Edge.ShouldBe(PortEdge.Bottom, "外面来的字节从下边出去（与监视器的上边配对）");
        probe.Ports.Single(port => port.Name == "tx").Edge.ShouldBe(PortEdge.Top, "要发出去的字节从上边进来（与发送器的下边配对）");

        var role = probe.DeclaredRoles.ShouldHaveSingleItem();
        role.Id.ShouldBe(roleId);
        role.Inputs.ShouldBe(["tx"], "进 = 发给外面");
        role.Outputs.ShouldBe(["rx"], "出 = 外面来的");
    }
}
