using System.Text;
using System.Text.Json;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 包内界面声明的读取与校验 —— 缺省值必须来自 schema、硬伤必须拒载、能安全降级的必须只是提示。
/// </summary>
public sealed class ViewDeclarationReaderTests
{
    [Fact]
    public void 没有声明文件时应给出全缺省声明()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        var (ok, document, error, warnings) = Read(null);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        document.Sources.ShouldBeEmpty();
        document.Views.ShouldBeEmpty();
        document.Frame.Labels.ShouldBeEmpty();

        // 窗口缺省**只在 schema 里给一次**。这里要盯的是"schema 写了什么，缺省声明就得是什么"，
        // 不是"宽度是 320" —— 调整 schema 的缺省不该让本用例变红，读不出那份缺省才该红。
        var window = document.Frame.Window;
        window.Width.ShouldBe(WindowDefault(schema, "width"));
        window.Height.ShouldBe(WindowDefault(schema, "height"));
        window.MinWidth.ShouldBe(WindowDefault(schema, "minWidth"));
        window.MinHeight.ShouldBe(WindowDefault(schema, "minHeight"));

        // 无上限：schema 用**显式 null** 表示"带了缺省值、值是空"（与"忘了写"能分开）⇒ 读出来也得是空。
        schema.WindowProperties["maxWidth"].HasDefault.ShouldBeTrue("window.maxWidth 应显式给出缺省");
        schema.WindowProperties["maxWidth"].TryGetNumber(out _).ShouldBeFalse("window.maxWidth 的缺省是无上限");
        window.MaxWidth.ShouldBeNull();
        schema.WindowProperties["maxHeight"].HasDefault.ShouldBeTrue("window.maxHeight 应显式给出缺省");
        schema.WindowProperties["maxHeight"].TryGetNumber(out _).ShouldBeFalse("window.maxHeight 的缺省是无上限");
        window.MaxHeight.ShouldBeNull();

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void 声明应读出控件树且缺省值来自schema()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "log", "kind": "Text" } ],
              "views": [
                {
                  "title": "主视图",
                  "root": {
                    "kind": "container",
                    "children": [ { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "readonly": true, "data": "log", "width": { "fill": true }, "height": { "fill": true, "min": 88 } } ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        document.Views.Count.ShouldBe(1);
        document.Views[0].Title.ShouldBe("主视图");

        var root = document.Views[0].Root;
        root.Kind.ShouldBe("container");

        // 声明里**没写**的键 ⇒ 读出来的必须是 schema 给的那份缺省。测试不另抄一份数值：
        // 改 schema 的缺省不该让本用例变红，读不出那份缺省才该红。
        AssertSchemaDefaultText(schema, root.Kind, "layout", root.Layout);
        AssertSchemaDefaultText(schema, root.Kind, "overflow", root.Overflow);
        AssertSchemaDefaultNumber(schema, root.Kind, "padding", root.Padding);
        AssertSchemaDefaultNumber(schema, root.Kind, "spacing", root.Spacing);
        root.Columns.ShouldBeEmpty();

        var child = root.Children.ShouldHaveSingleItem();
        child.Kind.ShouldBe("textBox");
        AssertSchemaDefaultText(schema, child.Kind, "title", child.Title);
        child.DataSource.ShouldBe("log");
    }

    [Fact]
    public void 语义容器应共享容器的排布缺省()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "paused", "kind": "Value", "valueType": "Bool" } ],
              "views": [
                {
                  "root": {
                    "kind": "group",
                    "children": [ { "kind": "toggleSwitch", "input": "paused", "width": { "fill": true }, "height": { "pixels": 28 } } ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        var root = document.Views[0].Root;

        // group 自己不声明 layout / spacing ⇒ 按容器基线（schema 里 containers.<基线>）取值；值本身不在这里抄。
        AssertSchemaDefaultText(schema, root.Kind, "layout", root.Layout);
        AssertSchemaDefaultNumber(schema, root.Kind, "spacing", root.Spacing);
    }

    [Fact]
    public void 未登记的键应拒载()
    {
        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [],
              "views": [ { "root": { "kind": "toggleSwitch", "input": "a", "colour": "red" } } ]
            }
            """);

        ok.ShouldBeFalse();
        document.ShouldBeNull();
        error.ShouldNotBeNull();
        error.Contains("未登记的键").ShouldBeTrue(error);
    }

    [Fact]
    public void 缺少绑定键应拒载()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [],
              "views": [ { "root": { "kind": "commandButton", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "title": "清空" } } ]
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("绑定键").ShouldBeTrue(error);
    }

    [Fact]
    public void 数据源引用不存在应拒载()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "log", "kind": "Text" } ],
              "views": [ { "root": { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "readonly": true, "data": "missing" } } ]
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("不存在").ShouldBeTrue(error);
    }

    [Fact]
    public void 绝对容器里的元素缺尺寸应拒载()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "layout": "absolute",
                    "children": [ { "kind": "toggleSwitch", "input": "a", "width": { "pixels": 30 } } ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("必须显式给出").ShouldBeTrue(error);
    }

    [Fact]
    public void 绝对容器里的元素尺寸必须是pixels()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "layout": "absolute",
                    "children": [
                      {
                        "kind": "toggleSwitch",
                        "input": "a",
                        "width": { "fill": true },
                        "height": { "pixels": 30 }
                      }
                    ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("必须是 pixels").ShouldBeTrue(error);
    }

    [Fact]
    public void grid里只写跨度不给起点应拒载()
    {
        // **grid 里只有一种落位写法**：`column` + `row`（或**都不写** ⇒ 按声明顺序落位）。
        // 只写 `columnSpan` / `rowSpan` 却不说从哪一格开始，跨度没有意义 —— 判为硬伤。
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "layout": "grid",
                    "columns": [ "*" ],
                    "rows": [ "auto" ],
                    "children": [
                      { "kind": "toggleSwitch", "input": "a", "column": 0, "row": 0, "width": { "fill": true }, "height": { "pixels": 28 } },
                      { "kind": "toggleSwitch", "input": "b", "columnSpan": 2, "width": { "fill": true }, "height": { "pixels": 28 } }
                    ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("跨度").ShouldBeTrue(error);
    }

    [Fact]
    public void grid里不写放置键_按声明顺序落位_应合法()
    {
        // 与流式同一套直觉：**顺序即位置** —— 不写放置键是合法写法，不是漏写。
        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [
                { "name": "a", "kind": "Value", "valueType": "Bool" },
                { "name": "b", "kind": "Value", "valueType": "Bool" }
              ],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "layout": "grid",
                    "columns": [ "*", "*" ],
                    "rows": [ "auto" ],
                    "children": [
                      { "kind": "toggleSwitch", "input": "a", "width": { "fill": true }, "height": { "pixels": 28 } },
                      { "kind": "toggleSwitch", "input": "b", "width": { "fill": true }, "height": { "pixels": 28 } }
                    ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        document!.Views[0].Root.Children.Count.ShouldBe(2);
    }

    [Fact]
    public void 声明版本不认识应拒载()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 99,
              "sources": [],
              "views": []
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("version").ShouldBeTrue(error);
    }

    [Fact]
    public void kind未登记应拒载()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [],
              "views": [ { "root": { "kind": "hologram" } } ]
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("没有登记").ShouldBeTrue(error);
    }

    [Fact]
    public void 尚未渲染的kind应放行但给提示()
    {
        var (ok, document, error, warnings) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "rows", "kind": "RowSet" } ],
              "views": [ { "root": { "kind": "table", "data": "rows" } } ]
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        document.Views[0].Root.Warnings.ShouldContain(item => item.Contains("尚未渲染"));
        warnings.ShouldContain(item => item.Contains("尚未渲染"));
    }

    [Fact]
    public void 未被引用的数据源应给提示()
    {
        // 判据取 `Value` 源：**`Text` 源可以没有界面实体**（模块直接往里写、或由输入通道投值），
        // 所以"没元素绑定它"不算漏接线；而没人绑的 `Value` 源是真的接不上。
        var (ok, _, error, warnings) = Read(
            """
            {
              "version": 1,
              "sources": [
                { "name": "log", "kind": "Text" },
                { "name": "extra", "kind": "Value", "valueType": "Bool" }
              ],
              "views": [ { "root": { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "readonly": true, "data": "log" } } ]
            }
            """);

        ok.ShouldBeTrue(error);
        warnings.ShouldContain(item => item.Contains("extra") && item.Contains("没有被任何元素引用"));
    }

    [Fact]
    public void 没有界面实体的Text源不算漏接线()
    {
        // 例：系统对话框选定的保存路径经输入通道投回来 —— **声明了就是接线了**。
        var (ok, _, error, warnings) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "log", "kind": "Text" }, { "name": "savePath", "kind": "Text" } ],
              "views": [ { "root": { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "readonly": true, "data": "log" } } ]
            }
            """);

        ok.ShouldBeTrue(error);
        warnings.ShouldNotContain(item => item.Contains("savePath"));
    }

    [Fact]
    public void 非grid容器上写了轨道应给提示()
    {
        var (ok, _, error, warnings) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "a", "kind": "Value", "valueType": "Bool" } ],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "columns": [ "*" ],
                    "children": [ { "kind": "toggleSwitch", "input": "a", "width": { "fill": true }, "height": { "pixels": 28 } } ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeTrue(error);
        warnings.ShouldContain(item => item.Contains("columns") && item.Contains("被忽略"));
    }

    [Fact]
    public void 网络标签应只读出边与位置()
    {
        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "frame": {
                "window": { "width": 460, "height": 500, "minWidth": 300, "minHeight": 200 },
                "ports": [ { "name": "in", "edge": "top", "offset": 0.25 } ]
              },
              "sources": [],
              "views": []
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        document.Frame.Window.Width.ShouldBe(460);
        document.Frame.Window.MinWidth.ShouldBe(300);

        var label = document.Frame.Labels.ShouldHaveSingleItem();
        label.Name.ShouldBe("in");
        label.Edge.ShouldBe(PortEdge.Top);
        label.Offset.ShouldBe(0.25);
    }

    [Fact]
    public void 网络标签对不上数据面时应提示但不算硬伤()
    {
        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "frame": { "ports": [ { "name": "in", "edge": "top" } ] },
              "sources": [],
              "views": []
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();

        ViewDeclarationReader.TryCheckLabels(document, [], hasDeclaration: true, out var notes, out var labelError)
            .ShouldBeTrue(labelError);
        labelError.ShouldBeNull("声明了数据面没有的口只是接不上，其余声明照常生效");
        notes.ShouldContain(item => item.Contains("in", StringComparison.Ordinal) && item.Contains("接不上", StringComparison.Ordinal));

        ViewDeclarationReader.TryCheckLabels(document, ["in"], hasDeclaration: true, out var clean, out var cleanError)
            .ShouldBeTrue(cleanError);
        clean.ShouldBeEmpty();
        cleanError.ShouldBeNull();
    }

    [Fact]
    public void 数据面的口没在声明里给出位置时应算硬伤()
    {
        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "frame": { "ports": [ { "name": "in", "edge": "top" } ] },
              "sources": [],
              "views": []
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();

        ViewDeclarationReader.TryCheckLabels(document, ["in", "out"], hasDeclaration: true, out _, out var labelError)
            .ShouldBeFalse("数据面的口 out 没有在 frame.ports[] 里给出位置 ⇒ 硬伤（整份声明忽略）");
        labelError.ShouldNotBeNull();
        labelError!.ShouldContain("out");
        labelError.ShouldContain("没有声明位置");

        ViewDeclarationReader.TryCheckLabels(document, ["in", "out"], hasDeclaration: false, out _, out var absent)
            .ShouldBeTrue("无界面模块（包内根本没有 view.json）不做这项完备性判定");
        absent.ShouldBeNull();
    }

    [Fact]
    public void 端口没有给出边时应拒载()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "frame": { "ports": [ { "name": "in" } ] },
              "sources": [],
              "views": []
            }
            """);

        ok.ShouldBeFalse("每个数据面口都必须显式声明 edge：框架不再按惯例推断");
        error.ShouldNotBeNull();
        error!.ShouldContain("edge");
    }

    [Fact]
    public void 端口的边不接受auto()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "frame": { "ports": [ { "name": "in", "edge": "auto" } ] },
              "sources": [],
              "views": []
            }
            """);

        ok.ShouldBeFalse("auto 只是工作区里「用户没拖过」的存储哨兵，不在声明面的枚举里");
        error.ShouldNotBeNull();
        error!.ShouldContain("edge");
    }

    [Fact]
    public void 声明面没有外观键_写了theme按未登记键拒载()
    {
        var (ok, _, error, _) = Read(
            """
            {
              "version": 1,
              "frame": { "theme": { "accent": "#ff0000" } },
              "sources": [],
              "views": []
            }
            """);

        ok.ShouldBeFalse("声明面完全没有外观键：连预留键都不留（用户裁决 m04883）");
        error.ShouldNotBeNull();
        error!.ShouldContain("未登记的键");
    }

    [Fact]
    public void 真实包_四个模块的声明都应无错误无警告()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        foreach (var name in new[] { "DataMonitor", "TextSender", "HexSender", "HexStringConverter" })
        {
            var packageDirectory = Path.Combine(FindRepoRoot(), "src", "Modules", "Business", name);
            Directory.Exists(packageDirectory).ShouldBeTrue($"应能找到模块源目录：{packageDirectory}");

            ViewDeclarationReader.TryRead(packageDirectory, schema, out var document, out var warnings, out var error)
                .ShouldBeTrue($"{name}：{error}");
            document.ShouldNotBeNull($"{name} 应读出声明");
            warnings.ShouldBeEmpty($"{name} 的声明不应有警告");
        }
    }

    [Fact]
    public void 真实包_数据显示器的声明应读出网格与六个控件()
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        var packageDirectory = Path.Combine(FindRepoRoot(), "src", "Modules", "Business", "DataMonitor");
        ViewDeclarationReader.TryRead(packageDirectory, schema, out var document, out var warnings, out var error)
            .ShouldBeTrue(error);
        warnings.ShouldBeEmpty();
        document.ShouldNotBeNull();
        var documentValue = document!;

        // 判据 = 「声明被框架正确应用」，不是「数值等于某个常量」：读**同一份声明的原始字节**（读取器读的就是它），
        // 按同一节点路径取出声明面，再与读取器产出的模型逐项比对 —— 声明写了什么，读出来的就得是什么。
        // 所以增删控件、把宽度从 128 改成 96 这类观感改动**不该**让本用例变红（声明本身就是这份比对的事实源）；
        // 读取器丢字段 / 改模式 / 错位才该红。
        using var declaration = ReadDeclaration(packageDirectory);
        var declaredRoot = declaration.RootElement
            .GetProperty("views").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("root");
        var model = documentValue.Views.ShouldHaveSingleItem().Root;

        model.Kind.ShouldBe(Declared(declaredRoot, "kind"), "节点 kind 照声明读出");
        model.Layout.ShouldBe(Declared(declaredRoot, "layout"), "容器布局照声明读出（写出 grid 才排得出网格）");

        // **轨道只在声明里写了才比对**：这条用例的判据是"声明写了什么、读出来就是什么"，
        // 而不是"根必须是网格"—— 根从 `grid` 改成 `flow` 是排布选择，不该让本用例变红
        // （`flow` 下没有轨道可声明）。
        if (declaredRoot.TryGetProperty("columns", out _))
        {
            AssertTracks(declaredRoot, "columns", model.Columns);
        }

        if (declaredRoot.TryGetProperty("rows", out _))
        {
            AssertTracks(declaredRoot, "rows", model.Rows);
        }

        AssertSpacing(declaredRoot, schema, model.Kind, "columnSpacing", model.ColumnSpacing);
        AssertSpacing(declaredRoot, schema, model.Kind, "rowSpacing", model.RowSpacing);

        var declaredChildren = declaredRoot.GetProperty("children").EnumerateArray().ToArray();
        model.Children.Count.ShouldBe(declaredChildren.Length, "子节点个数与声明一致");

        for (var index = 0; index < declaredChildren.Length; index++)
        {
            AssertChild(declaredChildren[index], model.Children[index], schema, $"children[{index}]");
        }

        var declaredPorts = declaration.RootElement
            .GetProperty("frame").GetProperty("ports").EnumerateArray()
            .Select(port => Declared(port, "name")).ToArray();

        ViewDeclarationReader.TryCheckLabels(documentValue, declaredPorts, hasDeclaration: true, out var notes, out var labelError)
            .ShouldBeTrue(labelError);
        notes.ShouldBeEmpty();
    }

    /// <summary>读**同一份声明的原始字节**：解析口径与读取器一致（跳过注释、允许尾随逗号）。</summary>
    private static JsonDocument ReadDeclaration(string packageDirectory) =>
        JsonDocument.Parse(
            File.ReadAllText(Path.Combine(packageDirectory, ViewDeclarationReader.FileName)),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

    private static string Declared(JsonElement node, string name) => node.GetProperty(name).GetString()!;

    private static bool IsTrue(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    /// <summary>窗口缺省：数值只在 schema 里给一次，测试只负责"缺省声明读出来的是 schema 那份"。</summary>
    private static double WindowDefault(ViewSchema schema, string key)
    {
        schema.WindowProperties[key].TryGetNumber(out var value).ShouldBeTrue($"schema 的 window.{key} 没有数字缺省");
        return value;
    }

    /// <summary>某个键的缺省怎么找 —— 与读取器同一顺序：kind 自己 → 容器基线 → 通用键。</summary>
    private static PropertySpec? FindProperty(ViewSchema schema, string kind, string key)
    {
        var spec = schema.KindOf(kind);
        if (spec is not null && spec.Properties.TryGetValue(key, out var own))
        {
            return own;
        }

        if (spec is { IsContainer: true }
            && schema.KindOf(schema.ContainerBaselineKind) is { } shared
            && shared.Properties.TryGetValue(key, out var inherited))
        {
            return inherited;
        }

        return schema.CommonProperties.TryGetValue(key, out var common) ? common : null;
    }

    /// <summary>轨道声明 ↔ 模型的轨道：条数与每条轨道的形状（<c>*</c> / <c>2*</c> / <c>auto</c> / 数值）都必须一致。</summary>
    private static void AssertTracks(JsonElement declaredRoot, string name, IReadOnlyList<GridTrack> actual)
    {
        var declared = declaredRoot.GetProperty(name).EnumerateArray().Select(track => track.GetString()!).ToArray();
        actual.Count.ShouldBe(declared.Length, $"{name} 的条数与声明一致");

        for (var index = 0; index < declared.Length; index++)
        {
            GridTrack.TryParse(declared[index], out var track).ShouldBeTrue($"声明 `{declared[index]}` 应是合法轨道写法");
            actual[index].ShouldBe(track, $"{name}[{index}] 的轨道照声明读出");
        }
    }

    private static void AssertSpacing(JsonElement declaredRoot, ViewSchema schema, string kind, string name, double actual)
    {
        if (declaredRoot.TryGetProperty(name, out var declared))
        {
            actual.ShouldBe(declared.GetDouble(), $"{name} 照声明读出");
            return;
        }

        AssertSchemaDefaultNumber(schema, kind, name, actual);
    }

    private static void AssertSchemaDefaultText(ViewSchema schema, string kind, string key, string? actual)
    {
        var spec = FindProperty(schema, kind, key);
        spec.ShouldNotBeNull($"schema 没有登记 {kind}.{key}，通用键里也没有");
        spec!.HasDefault.ShouldBeTrue($"schema 的 {kind}.{key} 没有给缺省值");

        if (spec.Default is { } element && element.ValueKind is JsonValueKind.Null)
        {
            actual.ShouldBeNull($"{kind}.{key} 没声明 ⇒ schema 的缺省就是空");
            return;
        }

        spec.TryGetString(out var expected).ShouldBeTrue($"schema 的 {kind}.{key} 缺省不是字符串");
        actual.ShouldBe(expected, $"{kind}.{key} 没声明 ⇒ 应取 schema 的缺省");
    }

    private static void AssertSchemaDefaultNumber(ViewSchema schema, string kind, string key, double actual)
    {
        var spec = FindProperty(schema, kind, key);
        spec.ShouldNotBeNull($"schema 没有登记 {kind}.{key}，通用键里也没有");
        spec!.TryGetNumber(out var expected).ShouldBeTrue($"schema 的 {kind}.{key} 缺省不是数字");
        actual.ShouldBe(expected, $"{kind}.{key} 没声明 ⇒ 应取 schema 的缺省");
    }

    private static void AssertSchemaDefaultBool(ViewSchema schema, string kind, string key, bool actual)
    {
        var spec = FindProperty(schema, kind, key);
        spec.ShouldNotBeNull($"schema 没有登记 {kind}.{key}，通用键里也没有");
        spec!.TryGetBool(out var expected).ShouldBeTrue($"schema 的 {kind}.{key} 缺省不是布尔");
        actual.ShouldBe(expected, $"{kind}.{key} 没声明 ⇒ 应取 schema 的缺省");
    }

    /// <summary>一个子节点：声明里写了什么，模型里就得是什么（kind / 标题 / 绑定 / 放置 / 跨度 / 轴 / 该 kind 的专有键）。</summary>
    private static void AssertChild(JsonElement declared, ViewNode actual, ViewSchema schema, string where)
    {
        var kind = Declared(declared, "kind");
        actual.Kind.ShouldBe(kind, $"{where} 的 kind 照声明读出");

        if (declared.TryGetProperty("title", out var title))
        {
            actual.Title.ShouldBe(title.GetString(), $"{where} 的标题照声明读出");
        }
        else
        {
            AssertSchemaDefaultText(schema, kind, "title", actual.Title);
        }

        foreach (var role in new[] { ViewBindingRoles.Data, ViewBindingRoles.Input, ViewBindingRoles.Command })
        {
            if (!declared.TryGetProperty(role, out var bound))
            {
                continue;
            }

            var actualBound = role switch
            {
                ViewBindingRoles.Data => actual.DataSource,
                ViewBindingRoles.Input => actual.Input,
                _ => actual.Command,
            };

            actualBound.ShouldBe(bound.GetString(), $"{where} 的绑定键 {role} 照声明读出");
        }

        AssertPlacement(declared, "column", actual.Column, where);
        AssertPlacement(declared, "row", actual.Row, where);
        AssertCount(declared, schema, kind, "columnSpan", actual.ColumnSpan, where);
        AssertCount(declared, schema, kind, "rowSpan", actual.RowSpan, where);
        AssertText(declared, schema, kind, "textPlacement", actual.TextPlacement, where);
        AssertText(declared, schema, kind, "textAnchor", actual.TextAnchor, where);

        // **该 kind 的专有布尔键也必须走同一套缺省口径**：漏了这几条，"声明里没写"就会静默变成
        // `false`（而不是 schema 写的缺省），而 `flag` 类键恰恰是"不写就取缺省"的主力。
        AssertFlag(declared, schema, kind, "multiline", actual.Multiline, where);
        AssertFlag(declared, schema, kind, "expanded", actual.Expanded, where);
        AssertFlag(declared, schema, kind, "wrap", actual.Wrap, where);
        AssertFlag(declared, schema, kind, "showNewlineMark", actual.ShowNewlineMark, where);

        AssertAxis(declared, "width", actual.Width, where);
        AssertAxis(declared, "height", actual.Height, where);
    }

    /// <summary>一根轴：声明写的是 adaptive / fill / pixels，模型就得落在同一个模式上（像素值照声明读，不比对常量）。</summary>
    private static void AssertAxis(JsonElement declared, string axisName, AxisLayout actual, string where)
    {
        if (declared.TryGetProperty(axisName, out var axis))
        {
            if (IsTrue(axis, "adaptive"))
            {
                actual.ResolvedMode.ShouldBe(AxisMode.Adaptive, $"{where} 的 {axisName} 声明为按内容");
                return;
            }

            if (IsTrue(axis, "fill"))
            {
                actual.ResolvedMode.ShouldBe(AxisMode.Fill, $"{where} 的 {axisName} 声明为填满");
                return;
            }

            axis.TryGetProperty("pixels", out var pixels)
                .ShouldBeTrue($"{where} 的 {axisName} 既不是 adaptive / fill，也没有 pixels");
            actual.ResolvedMode.ShouldBe(AxisMode.Pixels, $"{where} 的 {axisName} 声明为固定像素");
            actual.Pixels.ShouldBe(pixels.GetDouble(), $"{where} 的 {axisName} 像素照声明读出");
            return;
        }
    }

    private static void AssertPlacement(JsonElement declared, string name, int? actual, string where)
    {
        if (declared.TryGetProperty(name, out var value))
        {
            actual.ShouldBe((int?)value.GetInt32(), $"{where} 的 {name} 照声明读出");
            return;
        }

        actual.ShouldBeNull($"{where} 没声明 {name} ⇒ 不该被凭空填上");
    }

    private static void AssertCount(JsonElement declared, ViewSchema schema, string kind, string name, int actual, string where)
    {
        if (declared.TryGetProperty(name, out var value))
        {
            actual.ShouldBe(value.GetInt32(), $"{where} 的 {name} 照声明读出");
            return;
        }

        if (FindProperty(schema, kind, name) is null)
        {
            // 这个 kind 根本没登记这个键 ⇒ 读出来的是读取器的兜底值，不属于声明面，这里不作断言。
            return;
        }

        AssertSchemaDefaultNumber(schema, kind, name, actual);
    }

    private static void AssertFlag(JsonElement declared, ViewSchema schema, string kind, string name, bool actual, string where)
    {
        if (declared.TryGetProperty(name, out var value))
        {
            actual.ShouldBe(value.GetBoolean(), $"{where} 的 {name} 照声明读出");
            return;
        }

        if (FindProperty(schema, kind, name) is null)
        {
            // 这个 kind 根本没登记这个键 ⇒ 读出来的是读取器的兜底值，不属于声明面，这里不作断言。
            return;
        }

        AssertSchemaDefaultBool(schema, kind, name, actual);
    }

    private static void AssertText(JsonElement declared, ViewSchema schema, string kind, string name, string? actual, string where)
    {
        if (declared.TryGetProperty(name, out var value))
        {
            actual.ShouldBe(value.GetString(), $"{where} 的 {name} 照声明读出");
            return;
        }

        var spec = FindProperty(schema, kind, name);
        if (spec is null)
        {
            // 这个 kind 根本没登记这个键 ⇒ 读出来的是读取器的兜底值，不属于声明面，这里不作断言。
            return;
        }

        if (spec.Default is { } element && element.ValueKind is JsonValueKind.Null)
        {
            actual.ShouldBeNull($"{where} 没声明 {name} ⇒ schema 的缺省就是空");
            return;
        }

        spec.TryGetString(out var expected).ShouldBeTrue($"schema 的 {kind}.{name} 缺省不是字符串");
        actual.ShouldBe(expected, $"{where} 没声明 {name} ⇒ 应取 schema 的缺省");
    }

    /// <summary>仓库根（测试从自身输出目录往上找解决方案文件；与其它测试同一成例）。</summary>
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

        throw new InvalidOperationException("找不到仓库根");
    }

    private static (bool Ok, ViewDocument? Document, string? Error, IReadOnlyList<string> Warnings) Read(string? json)
    {
        ViewSchema.TryLoad(out var schema, out var schemaError).ShouldBeTrue(schemaError);
        schema.ShouldNotBeNull();

        using var package = new PackageDirectory();
        if (json is not null)
        {
            package.WriteDeclaration(json);
        }

        var ok = ViewDeclarationReader.TryRead(package.Directory, schema, out var document, out var warnings, out var error);
        return (ok, document, error, warnings);
    }

    /// <summary>一次性的模块包目录：只放一份声明文件。</summary>
    private sealed class PackageDirectory : IDisposable
    {
        internal PackageDirectory()
        {
            Directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"hyw-view-declaration-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
        }

        internal string Directory { get; }

        internal void WriteDeclaration(string json) =>
            File.WriteAllText(
                System.IO.Path.Combine(Directory, ViewDeclarationReader.FileName),
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清不掉不影响结论。
            }
        }
    }
}
