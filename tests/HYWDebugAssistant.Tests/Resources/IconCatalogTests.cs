using System.Reflection;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Resources;

/// <summary>
/// 图标清单机制的机制级用例：清单解析、SVG 子集、缺键标记、坏表降级、缓存代号、缺表诊断、框架表真的出货。
/// </summary>
/// <remarks>
/// <b>为什么这些要测</b>：图标是磁盘上的数据，出错时界面只会少一个图（不报错、不崩溃）——
/// 那是"看得见但说不清"的故障，只能靠机制用例把解析规则钉住。
/// 用例表由 <see cref="IconCatalogTestRoot"/> 造在临时目录里（布局与出厂一致）。
/// </remarks>
[Collection(nameof(TextCatalogTests))]
public sealed class IconCatalogTests : IDisposable
{
    private static readonly Assembly Owner = typeof(IconCatalogTests).Assembly;

    private readonly IconCatalogTestRoot _tables = IconCatalogTestRoot.CreateWithProbeAssets();

    /// <inheritdoc />
    public void Dispose()
    {
        _tables.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("icon.probe.refresh", 20d)]
    [InlineData("icon.probe.lower", 24d)]
    public void 清单_应按相对路径解析出SVG的路径与画布(string key, double expectedViewBox)
    {
        IconCatalog.TryGet(Owner, key, out var asset).ShouldBeTrue();

        asset.ShouldNotBeNull();
        asset!.PathData.Count.ShouldBe(1, "探针 SVG 只有一条 <path>");
        asset.PathData[0].ShouldNotBeNullOrWhiteSpace();
        asset.ViewBoxWidth.ShouldBe(expectedViewBox);
        asset.ViewBoxHeight.ShouldBe(expectedViewBox);
        asset.EvenOdd.ShouldBeFalse("没写 fill-rule ⇒ nonzero");
    }

    [Fact]
    public void 缺失键_应返回空与键名()
    {
        IconCatalog.TryGet(Owner, "icon.probe.absent", out var asset).ShouldBeFalse();
        asset.ShouldBeNull();

        IconCatalog.Get(Owner, "icon.probe.absent").ShouldBeNull();
        IconCatalog.MissingValue("icon.probe.absent").ShouldBe("icon.probe.absent");
    }

    [Fact]
    public void 多路径与evenodd_应一并读出()
    {
        _tables.WriteAsset("probe/two.svg", """
            <svg viewBox="0 0 20 20" xmlns="http://www.w3.org/2000/svg">
              <path d="M0 0H10V10H0Z" fill-rule="evenodd" />
              <path d="M12 12H20V20H12Z" />
            </svg>
            """);
        _tables.WriteManifest("""
            { "entries": { "icon.probe.two": "probe/two.svg" } }
            """);
        _tables.Use();

        IconCatalog.TryGet(Owner, "icon.probe.two", out var asset).ShouldBeTrue();

        asset!.PathData.Count.ShouldBe(2, "多 path 要全部读出来（缺一片图形就是残缺图标）");
        asset.EvenOdd.ShouldBeTrue("任一 path 写了 evenodd ⇒ 整张图按 evenodd 合成");
    }

    [Fact]
    public void 坏条目_只跳过该条目_同表其他键仍可用()
    {
        _tables.WriteAsset("probe/kept.svg", IconCatalogTestRoot.ProbeLowerSvg);
        _tables.WriteManifest("""
            {
              "entries": {
                "bad.prefix": "probe/kept.svg",
                "icon.bad.absent": "probe/missing.svg",
                "icon.bad.escape": "../outside.svg",
                "icon.bad.extension": "probe/kept.png",
                "icon.good.kept": "probe/kept.svg"
              }
            }
            """);
        _tables.Use();

        var diagnostics = new List<string>();
        IconCatalog.Diagnostics = diagnostics.Add;

        try
        {
            IconCatalog.TryGet(Owner, "icon.good.kept", out var kept).ShouldBeTrue();
            kept!.ViewBoxWidth.ShouldBe(24d);

            IconCatalog.TryGet(Owner, "icon.bad.absent", out _).ShouldBeFalse("清单指向不存在的文件");
            IconCatalog.TryGet(Owner, "icon.bad.escape", out _).ShouldBeFalse("路径不得跳出拥有者目录");
            IconCatalog.TryGet(Owner, "icon.bad.extension", out _).ShouldBeFalse("只认 .svg");
            IconCatalog.TryGet(Owner, "bad.prefix", out _).ShouldBeFalse("键不以 icon. 开头 ⇒ 跳过该条目");
        }
        finally
        {
            IconCatalog.Diagnostics = null;
        }

        diagnostics.ShouldContain(message => message.Contains("icon.bad.absent", StringComparison.Ordinal));
        diagnostics.ShouldContain(message => message.Contains("icon.bad.escape", StringComparison.Ordinal));
        diagnostics.ShouldContain(message => message.Contains("icon.bad.extension", StringComparison.Ordinal));
        diagnostics.ShouldContain(message => message.Contains("bad.prefix", StringComparison.Ordinal));
    }

    [Fact]
    public void 坏SVG_只跳过该条目并报诊断()
    {
        _tables.WriteAsset("probe/broken.svg", "<svg viewBox=\"0 0 20 20\"><path d=\"M0 0H10V10");
        _tables.WriteAsset("probe/nopath.svg", """<svg viewBox="0 0 20 20" xmlns="http://www.w3.org/2000/svg"><rect width="10" height="10" /></svg>""");
        _tables.WriteManifest("""
            {
              "entries": {
                "icon.broken.xml": "probe/broken.svg",
                "icon.broken.nopath": "probe/nopath.svg"
              }
            }
            """);
        _tables.Use();

        var diagnostics = new List<string>();
        IconCatalog.Diagnostics = diagnostics.Add;

        try
        {
            IconCatalog.TryGet(Owner, "icon.broken.xml", out _).ShouldBeFalse();
            IconCatalog.TryGet(Owner, "icon.broken.nopath", out _).ShouldBeFalse();
        }
        finally
        {
            IconCatalog.Diagnostics = null;
        }

        diagnostics.ShouldContain(message => message.Contains("probe/broken.svg", StringComparison.Ordinal));
        diagnostics.ShouldContain(message => message.Contains("probe/nopath.svg", StringComparison.Ordinal));
    }

    [Fact]
    public void 不支持的构造_跳过并报诊断()
    {
        _tables.WriteAsset("probe/unsupported.svg", """
            <svg viewBox="0 0 20 20" xmlns="http://www.w3.org/2000/svg">
              <path d="M0 0H10V10H0Z" transform="translate(2,2)" />
              <use href="other.svg#shape" />
              <path d="M12 12H18V18H12Z" />
            </svg>
            """);
        _tables.WriteManifest("""
            { "entries": { "icon.probe.unsupported": "probe/unsupported.svg" } }
            """);
        _tables.Use();

        var diagnostics = new List<string>();
        IconCatalog.Diagnostics = diagnostics.Add;

        try
        {
            IconCatalog.TryGet(Owner, "icon.probe.unsupported", out var asset).ShouldBeTrue();
            asset!.PathData.Count.ShouldBe(1, "带 transform 的 path 与 <use> 都要跳过，只留可用的那一条");
        }
        finally
        {
            IconCatalog.Diagnostics = null;
        }

        diagnostics.ShouldContain(message => message.Contains("transform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 坏JSON_应跳过整份清单并报诊断()
    {
        _tables.WriteManifest("""{ """);
        _tables.Use();

        var diagnostics = new List<string>();
        IconCatalog.Diagnostics = diagnostics.Add;

        try
        {
            IconCatalog.TryGet(Owner, "icon.probe.refresh", out _).ShouldBeFalse();
        }
        finally
        {
            IconCatalog.Diagnostics = null;
        }

        diagnostics.ShouldContain(message => message.Contains("icons.json", StringComparison.Ordinal));
    }


    [Fact]
    public void 重复键_先者生效并报诊断()
    {
        _tables.WriteManifest("""
            {
              "entries": {
                "icon.probe.refresh": "probe/refresh.svg",
                "icon.probe.refresh": "probe/lower.svg"
              }
            }
            """);
        _tables.Use();

        var diagnostics = new List<string>();
        IconCatalog.Diagnostics = diagnostics.Add;

        try
        {
            IconCatalog.TryGet(Owner, "icon.probe.refresh", out var asset).ShouldBeTrue();
            asset!.ViewBoxWidth.ShouldBe(20d, "重复键先者生效（与文本表的去重口径一致）");
        }
        finally
        {
            IconCatalog.Diagnostics = null;
        }

        diagnostics.ShouldContain(message => message.Contains("icon.probe.refresh", StringComparison.Ordinal));
    }

    [Fact]
    public void 缓存_同根重写文件要UseRoot才重读()
    {
        IconCatalog.TryGet(Owner, "icon.probe.refresh", out var before).ShouldBeTrue();
        before!.ViewBoxWidth.ShouldBe(20d);

        _tables.WriteAsset("probe/replaced.svg", """
            <svg viewBox="0 0 32 32" xmlns="http://www.w3.org/2000/svg"><path d="M0 0H16V16H0Z" /></svg>
            """);
        _tables.WriteManifest("""
            { "entries": { "icon.probe.refresh": "probe/replaced.svg" } }
            """);

        IconCatalog.TryGet(Owner, "icon.probe.refresh", out var cached).ShouldBeTrue();
        cached!.ViewBoxWidth.ShouldBe(20d, "同一代号下不重读磁盘（缓存按拥有者 + 代号）");

        _tables.Use();

        IconCatalog.TryGet(Owner, "icon.probe.refresh", out var reloaded).ShouldBeTrue();
        reloaded!.ViewBoxWidth.ShouldBe(32d, "UseRoot 让代号递增，下一次取值按新文件重读");
    }

    [Fact]
    public void 缺表目录_每个拥有者只报一次()
    {
        using var empty = IconCatalogTestRoot.Create();   // 有根、没有拥有者目录

        var diagnostics = new List<string>();
        IconCatalog.Diagnostics = diagnostics.Add;

        try
        {
            IconCatalog.UseRoot(empty.Root);

            IconCatalog.Get(Owner, "icon.absent.first").ShouldBeNull();
            IconCatalog.Get(Owner, "icon.absent.second").ShouldBeNull();
            IconCatalog.Get(Owner, "icon.absent.third").ShouldBeNull();
        }
        finally
        {
            IconCatalog.Diagnostics = null;
        }

        diagnostics.Count.ShouldBe(1, "整张表缺失按拥有者去重：查多少次键都只报一条");
        diagnostics[0].ShouldContain(IconCatalogTestRoot.OwnerName, Case.Insensitive);
        diagnostics[0].ShouldContain(empty.OwnerDirectory, Case.Insensitive);
    }

    /// <summary>
    /// **框架的图标清单必须真的能被解析**（清单/SVG 是"编译通过却带病出货"的高发区）。
    /// </summary>
    /// <remarks>
    /// 外壳图标的键全部来自 <see cref="ShellIcons"/>；这里逐个查表，缺一个键就红。
    /// 只断言"解析得到、有路径、画布宽高为正"（具体画布尺寸由 SVG 自己声明，不在这里钉死）。
    /// </remarks>
    [Fact]
    public void 框架图标表_外壳图标键应全部解析()
    {
        IconCatalog.UseRoot(null);   // 回到默认根：仓库 resources/icons（或应用输出里的那份）

        var assembly = typeof(ShellIcons).Assembly;
        var keys = typeof(ShellIcons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        keys.ShouldNotBeEmpty("外壳图标键不能为空（否则本用例什么也没验到）");

        foreach (var key in keys)
        {
            key.ShouldStartWith("icon.");
            IconCatalog.TryGet(assembly, key, out var asset).ShouldBeTrue($"{key} 不在 Ui 的图标表里");
            asset!.PathData.ShouldNotBeEmpty($"{key} 必须真的解析出路径");
            asset.ViewBoxWidth.ShouldBeGreaterThan(0, $"{key} 的 viewBox 宽度必须是正的");
            asset.ViewBoxHeight.ShouldBeGreaterThan(0, $"{key} 的 viewBox 高度必须是正的");
        }
    }
}
