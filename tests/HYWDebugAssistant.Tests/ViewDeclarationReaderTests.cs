using System.Text;
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
        var (ok, document, error, warnings) = Read(null);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        document.Sources.ShouldBeEmpty();
        document.Views.ShouldBeEmpty();
        document.Frame.Labels.ShouldBeEmpty();
        document.Frame.Window.Width.ShouldBe(320);
        document.Frame.Window.Height.ShouldBe(220);
        document.Frame.Window.MinWidth.ShouldBe(180);
        document.Frame.Window.MinHeight.ShouldBe(160);
        document.Frame.Window.MaxWidth.ShouldBeNull();
        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void 声明应读出控件树且缺省值来自schema()
    {
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
                    "children": [ { "kind": "textLog", "data": "log" } ]
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
        root.Layout.ShouldBe("flow");
        root.Overflow.ShouldBe("scroll");
        root.Padding.ShouldBe(0);
        root.Spacing.ShouldBe(2);
        root.Columns.ShouldBeEmpty();
        root.Width.Fill.ShouldBeTrue("基线口径：宽度填满");
        root.Height.Adaptive.ShouldBeTrue("基线口径：高度按内容");

        var child = root.Children.ShouldHaveSingleItem();
        child.Kind.ShouldBe("textLog");
        child.Title.ShouldBeEmpty("标题缺省是空串");
        child.DataSource.ShouldBe("log");
        child.Height.Fill.ShouldBeTrue("日志类的高度口径是填满");
        child.ShowInlineResult.ShouldBeTrue();
        child.InputKind.ShouldBe("text");
        child.FileMode.ShouldBe("open");
    }

    [Fact]
    public void 语义容器应共享容器的排布缺省()
    {
        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "paused", "kind": "Value", "valueType": "Bool" } ],
              "views": [
                {
                  "root": {
                    "kind": "group",
                    "children": [ { "kind": "toggleSwitch", "input": "paused" } ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        var root = document.Views[0].Root;
        root.Layout.ShouldBe("flow", "group 不重复声明 layout，按容器基线取值");
        root.Spacing.ShouldBe(2, "group 不重复声明 spacing，按容器基线取值");
    }

    [Fact]
    public void 多行输入的高度缺省按schema切换()
    {
        var (ok, document, error, _) = Read(
            """
            {
              "version": 1,
              "sources": [
                { "name": "a", "kind": "Text" },
                { "name": "b", "kind": "Text" }
              ],
              "views": [
                {
                  "root": {
                    "kind": "container",
                    "children": [
                      { "kind": "textInput", "input": "a" },
                      { "kind": "textInput", "input": "b", "multiline": true }
                    ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeTrue(error);
        document.ShouldNotBeNull();
        var children = document.Views[0].Root.Children;
        children.Count.ShouldBe(2);
        children[0].Height.Adaptive.ShouldBeTrue("单行输入按内容");
        children[1].Height.Fill.ShouldBeTrue("多行输入按 schema 的 heightWhenMultiline 口径");
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
              "views": [ { "root": { "kind": "commandButton", "title": "清空" } } ]
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
              "views": [ { "root": { "kind": "textLog", "data": "missing" } } ]
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
    public void grid里的两种放置写法混用应拒载()
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
                    "layout": "grid",
                    "columns": [ "*" ],
                    "rows": [ "auto" ],
                    "children": [
                      { "kind": "toggleSwitch", "input": "a", "column": 0, "row": 0 },
                      { "kind": "toggleSwitch", "input": "b", "auto": true }
                    ]
                  }
                }
              ]
            }
            """);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.Contains("混用").ShouldBeTrue(error);
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
        var (ok, _, error, warnings) = Read(
            """
            {
              "version": 1,
              "sources": [ { "name": "log", "kind": "Text" }, { "name": "extra", "kind": "Text" } ],
              "views": [ { "root": { "kind": "textLog", "data": "log" } } ]
            }
            """);

        ok.ShouldBeTrue(error);
        warnings.ShouldContain(item => item.Contains("extra") && item.Contains("没有被任何元素引用"));
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
                    "children": [ { "kind": "toggleSwitch", "input": "a" } ]
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

        var root = document!.Views.ShouldHaveSingleItem().Root;
        root.Kind.ShouldBe("container");
        root.Layout.ShouldBe("grid");
        root.Columns.Count.ShouldBe(3, "三列等宽（用户裁定：表格式排列和列左对齐）");
        root.Rows.Count.ShouldBe(3, "显示区一行 + 按钮两行");
        root.Children.Count.ShouldBe(7);

        var log = root.Children[0];
        log.Kind.ShouldBe("textLog");
        log.ColumnSpan.ShouldBe(3, "显示区跨满三列");
        log.FeedbackSource.ShouldBe("savePath", "保存结果落在显示区左下角（用户裁定）");
        log.Width.ResolvedMode.ShouldBe(AxisMode.Fill, "显示区宽度满宽");
        log.Height.ResolvedMode.ShouldBe(AxisMode.Fill, "显示区高度吃 `*` 行的剩余 ⇒ 自适应窗口");

        for (var index = 1; index < 7; index++)
        {
            root.Children[index].Width.Pixels.ShouldBe(128d, "六个控件同宽（用户裁定：按钮大小相等）");
            root.Children[index].Height.Pixels.ShouldBe(30d, "六个控件同高");
        }

        root.Children[6].ShowInlineResult.ShouldBeFalse("保存按钮的就地结果行让位给显示区左下角");

        ViewDeclarationReader.TryCheckLabels(document, ["in"], hasDeclaration: true, out var notes, out var labelError)
            .ShouldBeTrue(labelError);
        notes.ShouldBeEmpty();
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
