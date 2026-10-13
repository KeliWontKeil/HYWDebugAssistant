using System.Text;
using HYWDebugAssistant.Hosting.Packages;
using Shouldly;
using Xunit;

// 本文件是**文档生成器**：产物是文件内容（Markdown），不是给用户看的界面文本。
// 插值里只有字符串与整数，没有随区域变化的数字/日期格式 ⇒ CA1305 在这里是误报。
#pragma warning disable CA1305

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// **声明面文档的生成器与守卫** —— 控件的键表由 schema 生成，文档不可能与实现走散。
/// </summary>
/// <remarks>
/// <para>
/// 手写的键表迟早会与 schema 不一致，而不一致的手写文档比没有文档更坏：模块作者照着它写，
/// 框架按 schema 读，两边永远对不上，且**没有任何东西会报错**。
/// </para>
/// <para>
/// 所以这一份是"**从唯一事实源生成**"：schema 是键与缺省的唯一登记处，文档是它的投影。
/// 用例在两边不一致时失败，并告诉你怎么重新生成。
/// </para>
/// <para>
/// 想重新生成（改了 schema 之后）：把环境变量 <c>HYW_WRITE_DOCS</c> 设为 <c>1</c> 跑这条用例。
/// </para>
/// </remarks>
public class ViewDeclarationDocTests
{
    private const string WriteSwitch = "HYW_WRITE_DOCS";

    [Fact]
    public void 控件目录必须与声明面schema逐键一致()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        schema.ShouldNotBeNull();

        var generated = BuildControlCatalog(schema!);
        var path = Path.Combine(RepoRoot(), "docs", "view-declaration", "控件目录.md");

        if (Environment.GetEnvironmentVariable(WriteSwitch) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        File.Exists(path).ShouldBeTrue(
            $"文档还没生成：{path}。把环境变量 {WriteSwitch}=1 跑一次这条用例即可生成。");

        var onDisk = File.ReadAllText(path);

        // **比正文，不比文件尾的换行**：编辑器与 git 对行尾的处理不该让这条用例红。
        onDisk.TrimEnd().ShouldBe(
            generated.TrimEnd(),
            $"控件目录与 schema 不一致（{path}）—— 改了 schema 就要重新生成：设 {WriteSwitch}=1 再跑一次");
    }

    /// <summary>把 schema 投影成一份人可读的控件目录。</summary>
    /// <param name="schema">声明面 schema。</param>
    /// <returns>Markdown 全文。</returns>
    private static string BuildControlCatalog(ViewSchema schema)
    {
        var text = new StringBuilder();

        text.AppendLine("# 控件目录（由 schema 生成，**不要手改**）");
        text.AppendLine();
        text.AppendLine("> 本页由 `ViewDeclarationDocTests.控件目录必须与声明面schema逐键一致` 从");
        text.AppendLine("> `src/Hosting/Packages/schema/view.schema.json` 生成 —— schema 是键与缺省值的**唯一登记处**，");
        text.AppendLine("> 本页只是它的投影。两边不一致时那条用例会失败并让你重新生成。");
        text.AppendLine();
        text.AppendLine("## 怎么读这张表");
        text.AppendLine();
        text.AppendLine("| 列 | 含义 |");
        text.AppendLine("|---|---|");
        text.AppendLine("| 键 | 声明面里的键名（写进 `view.json`）。 |");
        text.AppendLine("| 类型 | `string` / `number` / `integer` / `bool` / `enum` / `array`。 |");
        text.AppendLine("| 必填 | ✔ = 这个 kind 必须写它（漏了是**硬伤**，整份声明作废、界面全空）。 |");
        text.AppendLine("| 缺省 | 不写时框架用的值。**`—` = 没有缺省**（必填键才有这一档）。 |");
        text.AppendLine("| **适用** | **什么条件下这个键才被消费**。不满足条件时写了**没有任何效果** —— 那是静默，不是特性。写声明照这一列判：满足就写全，不满足就别写。 |");
        text.AppendLine("| 取值 | `enum` 的合法字面量。 |");
        text.AppendLine("| 说明 | 这个键管什么。 |");
        text.AppendLine();

        text.AppendLine("## 所有节点**结构上**都能写的键");
        text.AppendLine();
        text.AppendLine("> **注意**：这张表是「能写」，不是「写了有人读」。能不能生效看「适用」那一列 ——");
        text.AppendLine("> 它写明的是**消费条件**（如 `x`/`y` 只在父层是 `panel` 时被读）。");
        text.AppendLine();
        text.AppendLine(PropertyTable(schema.CommonProperties.Values));
        text.AppendLine();

        text.AppendLine("## 尺寸轴（`width` / `height` 的写法）");
        text.AppendLine();
        text.AppendLine("两个轴各写一个对象，对象里**三种口径三选一**（**同时写两个 = 硬伤**：读取器拒绝加载）：");
        text.AppendLine();
        text.AppendLine("| 写法 | 含义 |");
        text.AppendLine("|---|---|");
        text.AppendLine("| `{ \"pixels\": 128 }` | 固定像素（DIP）。适合按钮、单行输入这类就该这么大的东西。 |");
        text.AppendLine("| `{ \"fill\": true }` | 吃满这一层分给它的空间。只对**有上限**的那一层有意义（网格的格子、流式里的宽度）。 |");
        text.AppendLine("| `{ \"adaptive\": true }` | 按内容自适应（这一块要多大由它的内容决定）。 |");
        text.AppendLine("| `{ \"min\": 30 }` / `{ \"max\": 200 }` | 下限 / 上限，可与上面三种任一同时写。 |");
        text.AppendLine();
        text.AppendLine("**每个会渲染的元素两轴必写**，一个都不省：轴键整个没写 = 硬伤（几何没有缺省口径）。");
        text.AppendLine("绝对坐标 / `panel` 容器里的元素两轴还必须都是 `pixels`（见下）。");
        text.AppendLine();

        text.AppendLine("## 容器（可含 `children`）");
        text.AppendLine();

        foreach (var (kind, spec) in schema.Kinds.Where(pair => pair.Value.IsContainer).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"### `{kind}`");
            text.AppendLine();
            text.AppendLine($"绑定角色键：{(spec.RoleKeys.Count == 0 ? "无" : string.Join(" / ", spec.RoleKeys.Select(role => $"`{role}`")))}");
            text.AppendLine();
            text.AppendLine(PropertyTable(spec.Properties.Values));
            text.AppendLine();
        }

        text.AppendLine("## 控件（叶子节点）");
        text.AppendLine();
        text.AppendLine("「渲染」那一列：**✔ = 现在真的画得出来**；**✗ = 框架已登记、还没实现** —— 这种 kind 可以写，");
        text.AppendLine("界面会给一块写明「尚未渲染」的占位（**不画一个看起来能用、其实什么都不做的壳**），加载时给一条警告。");
        text.AppendLine();

        foreach (var (kind, spec) in schema.Kinds.Where(pair => !pair.Value.IsContainer).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"### `{kind}`");
            text.AppendLine();
            text.AppendLine($"渲染：{(spec.Rendered ? "✔" : "✗ 尚未渲染（写它会得到占位块 + 一条警告）")}");
            text.AppendLine();
            text.AppendLine($"绑定角色键：{(spec.RoleKeys.Count == 0 ? "无（不绑任何数据：它只显示自己的文字）" : string.Join(" / ", spec.RoleKeys.Select(role => $"`{role}`")) + "（**只能写一个**，写两个是硬伤）")}");
            text.AppendLine();
            text.AppendLine(PropertyTable(spec.Properties.Values));
            text.AppendLine();
        }

        text.AppendLine("## 硬伤与警告");
        text.AppendLine();
        text.AppendLine("**硬伤**（任一出现 ⇒ 整份声明作废、界面全空，模块加载后宿主会报出来）：");
        text.AppendLine();

        foreach (var rule in schema.HardRules)
        {
            text.AppendLine($"- {rule}");
        }

        text.AppendLine();
        text.AppendLine("**警告**（那一条忽略或夹住，其余照常生效）：");
        text.AppendLine();

        foreach (var rule in schema.WarningRules)
        {
            text.AppendLine($"- {rule}");
        }

        text.AppendLine();
        return text.ToString();
    }

    /// <summary>把一个 kind 的键表画成 Markdown 表格。</summary>
    /// <param name="properties">该 kind 的键规格。</param>
    /// <returns>Markdown 表格。</returns>
    private static string PropertyTable(IEnumerable<PropertySpec> properties)
    {
        var text = new StringBuilder();
        text.AppendLine("| 键 | 类型 | 必填 | 缺省 | 适用 | 取值 | 说明 |");
        text.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var values = property.Values.Count == 0 ? string.Empty : string.Join(" / ", property.Values.Select(value => $"`{value}`"));
            text.AppendLine(
                $"| `{property.Name}` | {property.Type} | {(property.Required ? "✔" : string.Empty)} "
                + $"| {DefaultText(property.Default)} | {OneLine(property.Applies)} | {values} | {OneLine(property.Doc)} |");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>把缺省值写成表里看得懂的短串。</summary>
    /// <param name="value">schema 里写的缺省值。</param>
    /// <returns>短串。</returns>
    private static string DefaultText(System.Text.Json.JsonElement? value)
    {
        if (value is not { } element)
        {
            return "—";
        }

        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null => "`null`",
            System.Text.Json.JsonValueKind.String => $"`\"{element.GetString()}\"`",
            System.Text.Json.JsonValueKind.True => "`true`",
            System.Text.Json.JsonValueKind.False => "`false`",
            System.Text.Json.JsonValueKind.Number => $"`{element.GetRawText()}`",
            _ => $"`{element.GetRawText()}`",
        };
    }

    /// <summary>把说明压成一行（表格里不能有换行）。</summary>
    /// <param name="doc">说明原文。</param>
    /// <returns>单行说明。</returns>
    private static string OneLine(string? doc) =>
        string.IsNullOrWhiteSpace(doc)
            ? string.Empty
            : doc.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();

    /// <summary>仓库根（测试从自身输出目录往上找解决方案文件）。</summary>
    /// <returns>仓库根绝对路径。</returns>
    private static string RepoRoot()
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

        throw new InvalidOperationException("找不到仓库根");
    }
}
