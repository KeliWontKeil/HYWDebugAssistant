using HYWDebugAssistant.Hosting.Packages;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 界面声明面 schema 的自查 —— 它是全部默认值与约束的唯一事实源，
/// 所以"框架自带的这份能不能用、缺不缺默认值"必须是可判定的，而不是等某个模块渲染时才炸。
/// </summary>
public sealed class ViewSchemaTests
{
    [Fact]
    public void 框架自带的声明面schema必须能加载并通过自查()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);

        schema.ShouldNotBeNull();
        schema.DocumentVersion.ShouldBe(ViewSchema.SupportedDocumentVersion);
        schema.Kinds.Count.ShouldBe(19);
        schema.Kinds.Values.Count(kind => kind.IsContainer).ShouldBe(4);
        schema.Kinds.Values.Count(kind => !kind.IsContainer).ShouldBe(15);
        schema.Kinds.Values.Count(kind => !kind.Rendered).ShouldBe(6);
        schema.HardRules.ShouldNotBeEmpty();
        schema.WarningRules.ShouldNotBeEmpty();
    }

    [Fact]
    public void 每个kind都必须算得出缺省宽高()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        schema.ShouldNotBeNull();

        foreach (var kind in schema.Kinds.Keys)
        {
            var geometry = schema.GeometryOf(kind);
            geometry.ShouldNotBeNull($"kind {kind} 取不到缺省尺寸口径");
            geometry.Width.ShouldNotBeNull($"kind {kind} 算不出缺省宽度");
            geometry.Height.ShouldNotBeNull($"kind {kind} 算不出缺省高度");
        }
    }

    [Fact]
    public void 缺省尺寸按kind分档_基线填满_日志填满_多行输入按内容()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        schema.ShouldNotBeNull();

        var baseline = schema.GeometryOf("numeric");
        baseline.ShouldNotBeNull();
        baseline.Width.ShouldBe(AxisDefault.Fill);
        baseline.Height.ShouldBe(AxisDefault.Adaptive);

        var log = schema.GeometryOf("textLog");
        log.ShouldNotBeNull();
        log.Height.ShouldBe(AxisDefault.Fill);

        var input = schema.GeometryOf("textInput");
        input.ShouldNotBeNull();
        input.Height.ShouldBe(AxisDefault.Adaptive);
        input.HeightWhenMultiline.ShouldBe(AxisDefault.Fill);
    }

    [Fact]
    public void 窗口缺省值只在schema里给出()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        schema.ShouldNotBeNull();

        foreach (var (name, expected) in WindowDefaults)
        {
            schema.WindowProperties[name].TryGetNumber(out var value).ShouldBeTrue($"window.{name} 没有数字缺省值");
            value.ShouldBe(expected);
        }

        // 无上限用显式 null 表示（"带了缺省值，值是空"与"忘了写"必须能分开）
        schema.WindowProperties["maxWidth"].HasDefault.ShouldBeTrue();
        schema.WindowProperties["maxWidth"].TryGetNumber(out _).ShouldBeFalse();
    }

    [Fact]
    public void 网络标签只声明默认位置_标记几何与可拖动性不在声明面()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        schema.ShouldNotBeNull();

        schema.PortProperties.Keys.ShouldBe(PortKeys, ignoreOrder: true);

        var edge = schema.PortProperties["edge"];
        edge.Values.ShouldBe(EdgeLiterals, ignoreOrder: true);
        edge.Required.ShouldBeTrue("每个数据面口都必须显式给出 edge —— 框架不再按惯例推断（decisions.md 7.61）");
        edge.TryGetString(out _).ShouldBeFalse("声明值就是唯一位置来源：没有默认值");

        schema.PortProperties["offset"].TryGetNumber(out var offset).ShouldBeTrue();
        offset.ShouldBe(0.5);
        schema.PortProperties["name"].Required.ShouldBeTrue();
    }

    [Fact]
    public void 声明面完全没有外观键_连预留键都不留()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        schema.ShouldNotBeNull();

        schema.FrameKeys.ShouldBe(FrameSectionNames, ignoreOrder: true, "框架级只有窗口与端口两组");
        schema.FrameProperties.ShouldBeEmpty("外观键一个都不留 —— 全局主题唯一，没有按模块覆盖这回事");
    }

    [Fact]
    public void 缺缺省值的键属于schema自身坏了_加载即失败()
    {
        var broken = MinimalSchema.Replace(
            "\"unit\": { \"type\": \"string\", \"default\": \"\" }",
            "\"unit\": { \"type\": \"string\" }",
            StringComparison.Ordinal);

        ViewSchema.TryParse(broken, out var schema, out var error).ShouldBeFalse();
        schema.ShouldBeNull();
        error.ShouldNotBeNull().ShouldContain("没有缺省值");
    }

    [Fact]
    public void 角色键必须真的是该控件自己的属性_否则加载即失败()
    {
        var broken = MinimalSchema.Replace(
            "\"roleKeys\": [\"data\"]",
            "\"roleKeys\": [\"value\"]",
            StringComparison.Ordinal);

        ViewSchema.TryParse(broken, out var schema, out var error).ShouldBeFalse();
        schema.ShouldBeNull();
        error.ShouldNotBeNull().ShouldContain("角色键");
    }

    [Fact]
    public void 基线口径缺失或版本不认识都要拒绝()
    {
        ViewSchema.TryParse(
            MinimalSchema.Replace("\"*\": { \"width\": { \"fill\": true }, \"height\": { \"adaptive\": true } },", string.Empty, StringComparison.Ordinal),
            out _,
            out var noBaseline).ShouldBeFalse();
        noBaseline.ShouldNotBeNull().ShouldContain("算不出缺省宽高");

        ViewSchema.TryParse(
            MinimalSchema.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal),
            out _,
            out var version).ShouldBeFalse();
        version.ShouldNotBeNull().ShouldContain("schemaVersion");

        ViewSchema.TryParse("{ not json", out _, out var broken).ShouldBeFalse();
        broken.ShouldNotBeNull().ShouldContain("解析失败");
    }

    private static readonly string[] PortKeys = ["name", "edge", "offset"];

    /// <summary>`edge` 的合法字面量（常量数组，避免把它写成调用实参）。</summary>
    private static readonly string[] EdgeLiterals = ["left", "right", "top", "bottom"];

    /// <summary>框架级允许出现的分组名（常量数组，避免把它写成调用实参）。</summary>
    private static readonly string[] FrameSectionNames = ["window", "ports"];

    private static readonly (string Name, double Value)[] WindowDefaults =
    [
        ("width", 320),
        ("height", 220),
        ("minWidth", 180),
        ("minHeight", 160),
    ];

    private const string MinimalSchema = """
        {
          "schemaVersion": 1,
          "documentVersion": 1,
          "window": {
            "width": { "type": "number", "min": 1, "default": 320 },
            "height": { "type": "number", "min": 1, "default": 220 },
            "minWidth": { "type": "number", "min": 1, "default": 180 },
            "minHeight": { "type": "number", "min": 1, "default": 160 },
            "maxWidth": { "type": "number", "min": 1, "default": null },
            "maxHeight": { "type": "number", "min": 1, "default": null }
          },
          "axes": {
            "strategies": {
              "pixels": { "type": "number", "min": 1 },
              "fill": { "type": "bool", "const": true },
              "adaptive": { "type": "bool", "const": true }
            }
          },
          "defaultGeometry": {
            "*": { "width": { "fill": true }, "height": { "adaptive": true } },
            "textLog": { "height": { "fill": true } },
            "textInput": { "height": { "adaptive": true }, "heightWhenMultiline": { "fill": true } }
          },
          "tracks": { "forms": [ "*", "N*", "auto", "number>=1" ] },
          "commonProperties": {
            "kind": { "type": "string", "required": true },
            "title": { "type": "string", "default": "" },
            "width": { "$ref": "axes" }
          },
          "containers": {
            "container": { "children": { "type": "array", "required": true } }
          },
          "atoms": {
            "numeric": {
              "rendered": true,
              "roleKeys": ["data"],
              "properties": {
                "data": { "type": "string", "required": true },
                "unit": { "type": "string", "default": "" }
              }
            }
          },
          "document": {
            "version": { "type": "integer", "const": 1, "required": true },
            "frame": {
              "window": { "$ref": "window" },
              "ports": {
                "type": "array", "required": false, "default": [],
                "item": {
                  "name": { "type": "string", "required": true },
                  "edge": { "type": "enum", "values": ["left", "right", "top", "bottom"], "required": true },
                  "offset": { "type": "number", "min": 0, "max": 1, "default": 0.5 }
                }
              }
            },
            "sources": {
              "type": "array", "required": true,
              "item": { "name": { "type": "string", "required": true } }
            },
            "views": {
              "type": "array", "required": true,
              "item": {
                "title": { "type": "string", "default": "" },
                "root": { "type": "node", "required": true }
              }
            }
          },
          "validation": { "hard": ["x"], "warning": ["y"] }
        }
        """;
}
