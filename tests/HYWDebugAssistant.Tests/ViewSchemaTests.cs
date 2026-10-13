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
        schema.Kinds.Count.ShouldBe(16);
        schema.Kinds.Values.Count(kind => kind.IsContainer).ShouldBe(4);
        schema.Kinds.Values.Count(kind => !kind.IsContainer).ShouldBe(12);
        schema.Kinds.Values.Count(kind => !kind.Rendered).ShouldBe(4);
        schema.HardRules.ShouldNotBeEmpty();
        schema.WarningRules.ShouldNotBeEmpty();
    }

    [Fact]
    public void 窗口缺省值只在schema里给出()
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        schema.ShouldNotBeNull();

        // 窗口缺省"只在 schema 里给一次"：本用例要盯的是**每个键都带了合规的缺省**，
        // 而不是"宽度是 320" —— 调数值不该变红，漏给缺省 / 缺省落在自己的下限之外才该红。
        foreach (var name in WindowKeys)
        {
            var spec = schema.WindowProperties[name];
            spec.TryGetNumber(out var value).ShouldBeTrue($"window.{name} 没有数字缺省值");
            spec.Min.ShouldNotBeNull($"window.{name} 没有给出下限");
            value.ShouldBeGreaterThanOrEqualTo(spec.Min!.Value, $"window.{name} 的缺省不该小于它自己的下限");
            if (spec.Max is { } max)
            {
                value.ShouldBeLessThanOrEqualTo(max, $"window.{name} 的缺省不该超过它自己的上限");
            }
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

        schema.PortProperties["offset"].TryGetNumber(out var offset).ShouldBeTrue("offset 应有数字缺省值");
        // 口位置是归一化坐标：缺省必须落在自己的定义域 0–1 内（具体取值属于观感，会随设计调整，不在这里钉死）
        offset.ShouldBeGreaterThanOrEqualTo(0, "offset 的缺省必须落在 0–1 之间");
        offset.ShouldBeLessThanOrEqualTo(1, "offset 的缺省必须落在 0–1 之间");
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
    public void 版本不认识或不是json都要拒绝()
    {
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

    /// <summary>窗口的四个"有值"尺寸键：缺省是否存在、是否落在自己的区间里由用例判定 —— 数值本身只在 schema 里给。</summary>
    private static readonly string[] WindowKeys = ["width", "height", "minWidth", "minHeight"];

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
          "defaultText": { "*": "none" },
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
