using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.HexStringConverter;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **四个基础模块的契约自检**（阶段 2 —— `implementation/06` §E）。
/// </summary>
/// <remarks>
/// <para>
/// 这四个模块组成用户所说的"**最基础的整体测试单元**"：两个发送器 → 一个显示器，外加一个离线转换器。
/// 因此本文件既查各自的声明面，也查**它们之间的连接前提**（同一个类型标识 + 同一形状 + 互补的默认边）。
/// </para>
/// <para>
/// 口径：`ModuleContract` 的 <c>Problems</c> 必须空；<c>Advisories</c> 也必须空
/// （零口零角色的转换器**正是**这条建议的边界案例）。
/// </para>
/// </remarks>
public class BusinessModulesContractTests
{
    private static readonly ContractShape RawBytesShape = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(1472)));

    // ---------------------------------------------------------------- 契约自检

    [Fact]
    public void 文本发送器_声明面应自洽()
    {
        var probe = ModuleContract.Probe(typeof(TextSenderModule));

        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty();

        var type = probe.DataTypes.ShouldHaveSingleItem();
        type.TypeId.ShouldBe("RawBytes");
        type.Version.ShouldBe(1);
        // 形状比较必须用 `AreSame`（record 的字段集合是引用比较 —— 见 `decisions.md` §2 R1）\n        ContractShape.AreSame(type.Shape, RawBytesShape).ShouldBeTrue("形状 = { len: u16, data: bytes[1472] }（跨模块兼容的前提）");

        var port = probe.Ports.ShouldHaveSingleItem();
        port.Name.ShouldBe("out");
        port.Direction.ShouldBe(PortDirection.Send);
        port.Edge.ShouldBe(PortEdge.Bottom, "宽 > 高 ⇒ 口在下边（与显示器的上边配对）");
        port.Type.GetValueOrDefault().ShouldBe(new DataTypeRef("RawBytes", 1));

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

        // 输入框必须声明"只接受 HEX"（否则用户能敲进任意字符，非法输入只在提交后才被拒）
        var inputs = probe.View!.Elements[0].Children
            .SelectMany(page => page.Children)
            .ToList();
        inputs.Count.ShouldBe(HexSenderModule.PageCount);
        inputs.ShouldAllBe(e => e.InputKind == TextInputKind.Hex);
        inputs.ShouldAllBe(e => e.Multiline);
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

        // 保存走**一步**（2026-09 用户裁定）：界面上**只有**一个文件选择器按钮，选中位置即写盘；
        // `save` 命令仍声明（程序化/热键入口），但**不再有第二个按钮**。
        var picker = probe.View!.Elements.Single(e => e.Kind == ViewElementKind.FileSelector);
        picker.FileMode.ShouldBe(FilePickerMode.Save);
        picker.FileFilter!.ShouldContain("*.txt");
        picker.SuggestedFileName.ShouldBe(
            DataMonitorModule.DefaultSaveFileName,
            "保存对话框必须预填默认文件名（用户：'文件名应给一个默认文件名…而不是空着'）");
        picker.Title.ShouldBe("保存", "按钮文案 = 元素标题（界面上就这一个保存按钮）");
        probe.View.Elements.Count(e => e.Kind == ViewElementKind.CommandButton).ShouldBe(
            1,
            "只剩「清空」一个命令按钮（保存不再占第二个按钮）");
        probe.View.Commands.Select(c => c.Name).ShouldContain(DataMonitorModule.SaveCommand);
        probe.View.Commands.Select(c => c.Name).ShouldContain(DataMonitorModule.ClearCommand);
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

        // 两组标签页各带状态键（模块靠它回读"当前选中页"）+ 两个方向各一个按钮
        var strips = probe.View!.Elements.Where(e => e.Kind == ViewElementKind.TabPages).ToList();
        strips.Count.ShouldBe(2);
        strips[0].StateKey.ShouldBe(HexStringConverterModule.CharPagesKey);
        strips[1].StateKey.ShouldBe(HexStringConverterModule.HexPagesKey);
        strips.ShouldAllBe(s => s.Children.Count == HexStringConverterModule.PageCount);

        probe.View.Commands.Count.ShouldBe(2, "两个方向各一个按钮");
    }

    // ---------------------------------------------------------------- 四模块的连接前提

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
        text.Ports[0].Edge.ShouldBe(PortEdge.Bottom);
        hex.Ports[0].Edge.ShouldBe(PortEdge.Bottom);
        monitor.Ports[0].Edge.ShouldBe(PortEdge.Top);
    }
}
