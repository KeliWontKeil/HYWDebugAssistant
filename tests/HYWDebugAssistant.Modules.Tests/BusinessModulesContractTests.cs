using HYWDebugAssistant.Modules.DataMonitor;
using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.HexStringConverter;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using System.Text.Json;
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
        // 形状比较必须用 `AreSame`（record 的字段集合按引用比较）
        ContractShape.AreSame(type.Shape, RawBytesShape).ShouldBeTrue("形状 = { len: u16, data: bytes[1472] }（跨模块兼容的前提）");

        var port = probe.Ports.ShouldHaveSingleItem();
        port.Name.ShouldBe("out");
        port.Direction.ShouldBe(PortDirection.Send);
        DeclaredEdge("text-sender", "out").ShouldBe("bottom", "宽 > 高 ⇒ 口在下边（与显示器的上边配对）—— 裁决 ③ 之后这条声明在包内 `view.json` 里");
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
        DeclaredEdge("hex-sender", "out").ShouldBe("bottom");
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
        DeclaredEdge("data-monitor", "in").ShouldBe("top", "与两个发送器的下边配对 ⇒ 上下叠放即可连");

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

        // ② 口朝向同一接触面（上下叠放：发送口在下、接收口在上）—— 声明在各自包内 `view.json`（裁决 ③）
        DeclaredEdge("text-sender", "out").ShouldBe("bottom");
        DeclaredEdge("hex-sender", "out").ShouldBe("bottom");
        DeclaredEdge("data-monitor", "in").ShouldBe("top");
    }

    /// <summary>
    /// 读**包内 `view.json`** 里某个口声明的默认边（裁决 ③：口的默认位置只有这一个来源）。
    /// </summary>
    /// <remarks>
    /// 本项目是"模块作者视角"（只引 `Sdk` + `Sdk.Testing`）⇒ **不引 `Hosting` 的读取器**，
    /// 直接用 `System.Text.Json` 断包里的原始声明。
    /// </remarks>
    private static string DeclaredEdge(string moduleName, string portName) =>
        ReadPackagedView(moduleName).GetProperty("frame").GetProperty("ports").EnumerateArray()
            .Single(port => string.Equals(port.GetProperty("name").GetString(), portName, StringComparison.Ordinal))
            .GetProperty("edge").GetString()!;

    private static JsonElement ReadPackagedView(string moduleName)
    {
        var path = Path.Combine(FindRepoRoot(), "artifacts", "modules", moduleName, "view.json");
        File.Exists(path).ShouldBeTrue($"模块包内应有 view.json：{path}（请先构建模块）");

        // 包内 json 允许注释与尾逗号（框架读取器的口径一致）⇒ 这里必须用同一套选项解析。
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return document.RootElement.Clone();
    }

    /// <summary>仓库根（模块包归拢目录在它下面）。</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HYWDebugAssistant.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根（HYWDebugAssistant.slnx）");
    }
}
