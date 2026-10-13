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
    /// **模块声明的标准化**：文本面的三个键必须**显式写出来**，不靠 schema 缺省兜底。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这里守的是**声明面自己读得懂**：读取器一旦把"声明里没写"读成 <c>false</c>（而不是取 schema 的缺省），
    /// <c>textBox</c> 的 <c>wrap</c> / <c>showNewlineMark</c> 就会静默变成关闭态，而模块作者
    /// 以为自己什么都没写就是默认行为 —— 界面不会报错，只会不像作者想的那样。
    /// 把"这块日志留多少行、折不折行、画不画换行标记"写出来，这一层歧义就不存在了。
    /// </para>
    /// <para>
    /// 判据只看"这个键在不在这一条声明里"，不看值 —— 值该是什么由模块决定。
    /// </para>
    /// </remarks>
    [Fact]
    public void 模块声明里的文本面_必须显式写出它自己的键()
    {
        var missing = new List<string>();

        foreach (var module in ModulePackages)
        {
            var root = ReadPackagedView(module).GetProperty("views")[0].GetProperty("root");
            CollectTextSurfaceGaps(root, module, missing);
        }

        missing.ShouldBeEmpty(
            "文本面的这些键必须显式声明（不靠 schema 缺省兜底）："
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// **几何必须逐元素显式声明**：每个会渲染的元素都得自己写出两个轴（漏写由读取器报硬伤）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 兜底不够显式：模块作者看自己的声明看不出"这块要多宽"，而界面上一旦表现不符预期，
    /// 也说不清是声明的问题还是框架的问题。写出来之后，"这块多宽多高"就只有一个事实源。
    /// </para>
    /// <para>
    /// **每个轴只写一个口径**（<c>adaptive</c> / <c>fill</c> / <c>pixels</c>）：读取器把"同轴多口径"
    /// 判为硬伤（声明作废）—— 那正是"声明了却没按声明来"的另一种来源，所以这里一并拦下。
    /// </para>
    /// <para>
    /// <c>tabs</c> / <c>collapsible</c> 的直接子节点是**页载体**（<c>tabs</c> 只取它的 <c>children</c>
    /// 与 <c>title</c>，载体自己不生成控件），所以它那两轴不必声明；载体里面的元素照查。
    /// **声明根**同理不必声明两轴：模块根**就是**表面面板本身，面板的尺寸由窗口给。
    /// </para>
    /// </remarks>
    [Fact]
    public void 模块声明里的每个元素_必须显式写出两个轴的口径()
    {
        var problems = new List<string>();

        foreach (var module in ModulePackages)
        {
            var root = ReadPackagedView(module).GetProperty("views")[0].GetProperty("root");
            CollectGeometryGaps(root, module, skipAxes: true, problems);
        }

        problems.ShouldBeEmpty(
            "几何必须逐元素显式声明（漏写是硬伤），且每个轴只能写一个口径："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>递归查几何声明：每个渲染元素两个轴都要有、且每轴只有一个口径。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="module">模块包名（报错定位用）。</param>
    /// <param name="skipAxes">本节点是否不必声明两轴（声明根 / 页载体）。</param>
    /// <param name="problems">问题清单（累加）。</param>
    private static void CollectGeometryGaps(JsonElement node, string module, bool skipAxes, List<string> problems)
    {
        var kind = node.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null;
        var title = node.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
        var where = $"{module} 的 {(title is { Length: > 0 } ? $"「{title}」" : kind ?? "?")}";

        if (!skipAxes && kind is { Length: > 0 })
        {
            foreach (var axis in AxisNames)
            {
                if (!node.TryGetProperty(axis, out var axisElement))
                {
                    problems.Add($"{where} 没写 {axis}");
                    continue;
                }

                var strategies = AxisStrategies
                    .Count(name => axisElement.TryGetProperty(name, out var value)
                        && value.ValueKind is JsonValueKind.True or JsonValueKind.Number);

                if (strategies != 1)
                {
                    problems.Add($"{where} 的 {axis} 写了 {strategies} 个口径（必须恰好一个）");
                }
            }
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        // 页载体：`tabs` / `collapsible` 的直接子节点自己不生成控件，两轴不必声明。
        var childrenAreCarriers = kind is "tabs" or "collapsible";

        foreach (var child in children.EnumerateArray())
        {
            CollectGeometryGaps(child, module, childrenAreCarriers, problems);
        }
    }

    /// <summary>本用例覆盖的模块包名（新增业务模块时一并登记）。</summary>
    private static readonly string[] ModulePackages =
        ["data-monitor", "hex-sender", "hex-string-converter", "text-sender"];

    /// <summary>一个轴上允许的口径（解析顺序 adaptive &gt; fill &gt; pixels）。</summary>
    private static readonly string[] AxisStrategies = ["adaptive", "fill", "pixels"];

    /// <summary>两个轴的键名。</summary>
    private static readonly string[] AxisNames = ["width", "height"];

    /// <summary>递归找文本面节点，逐个查它该显式写的键在不在。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="module">模块包名（报错定位用）。</param>
    /// <param name="missing">缺键清单（累加）。</param>
    private static void CollectTextSurfaceGaps(JsonElement node, string module, List<string> missing)
    {
        if (node.TryGetProperty("kind", out var kindElement)
            && kindElement.GetString() is "textBox")
        {
            var title = node.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            var where = $"{module} 的 {(title is { Length: > 0 } ? $"「{title}」" : "textBox")}";
            var readOnly = node.TryGetProperty("readonly", out var readOnlyElement)
                && readOnlyElement.ValueKind is JsonValueKind.True;

            foreach (var key in TextBoxKeys(readOnly))
            {
                if (!node.TryGetProperty(key, out _))
                {
                    missing.Add($"{where} 没写 {key}");
                }
            }
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            CollectTextSurfaceGaps(child, module, missing);
        }
    }

    /// <summary>文本框在声明里必须显式给出的键（按它是输入面还是输出面分两档）。</summary>
    /// <param name="readOnly">是不是只读（输出）面。</param>
    /// <returns>键名序列。</returns>
    /// <remarks>
    /// 这里只列**该档真的会读**的键：显式的意义是"作者表过态"，所以写上去就必须有人读
    /// （`capacity` 与 `inputKind` 不在列 —— 前者是**数据源**的属性，后者在框架不再替模块判
    /// 字符级合法性之后**没有任何消费者**，两个键都已从声明面删除）。
    /// </remarks>
    private static string[] TextBoxKeys(bool readOnly) => readOnly
        ? ["wrap", "showNewlineMark", "textAlign", "textPlacement"]
        : ["multiline", "wrap", "showNewlineMark", "textAlign", "textPlacement"];

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
