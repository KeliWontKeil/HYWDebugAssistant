using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Theming;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>主题目录：内置（代码）+ 磁盘主题（`GetThemeFiles()`）的解析（设置页与主题应用器共用的入口）。</summary>
public class ThemeCatalogTests
{
    [Fact]
    public void 磁盘主题_应出现在目录并按其基类型解析()
    {
        var sakuraTitle = "樱色";
        var themes = ThemeCatalog.All([Theme("sakura", sakuraTitle, ThemeBaseKind.Light)]);

        themes.Count.ShouldBe(3, "内置两套 + 一个磁盘主题");

        var descriptor = ThemeCatalog.Find(themes, "sakura").ShouldNotBeNull();
        descriptor.Title.ShouldBe(sakuraTitle);
        descriptor.Kind.ShouldBe(ThemeKind.Light);
        descriptor.Source.ShouldBe(ThemeSource.File);
        descriptor.PaletteValue("AccentPrimary").ShouldBe("#FFC45C8A");

        var appearance = new AppearanceSettings { ThemeSelection = "sakura" };
        ThemeCatalog.ResolveDisplayTheme(themes, appearance, "sakura").Kind.ShouldBe(ThemeKind.Light);
        ThemeCatalog.VariantFor(themes, appearance, "sakura").ShouldBe(descriptor.LookupVariant);
    }

    [Fact]
    public void 跟随系统_应按浅深模式解析各自主题()
    {
        var themes = ThemeCatalog.All([
            Theme("custom-light", "我的浅色", ThemeBaseKind.Light),
            Theme("custom-dark", "我的深色", ThemeBaseKind.Dark),
        ]);

        var appearance = new AppearanceSettings
        {
            SystemLightScheme = "custom-light",
            SystemDarkScheme = "custom-dark",
        };

        ThemeCatalog.ResolveSystemScheme(themes, appearance, ThemeKind.Light).Id.ShouldBe("custom-light");
        ThemeCatalog.ResolveSystemScheme(themes, appearance, ThemeKind.Dark).Id.ShouldBe("custom-dark");
        ThemeCatalog.ResolveDisplayTheme(themes, appearance, ThemeIds.System).Id.ShouldBe("custom-light");
    }

    [Fact]
    public void 跟随系统_基类型不匹配时应回落内置()
    {
        var themes = ThemeCatalog.All([Theme("custom-dark", "深色方案", ThemeBaseKind.Dark)]);
        var appearance = new AppearanceSettings { SystemLightScheme = "custom-dark" };   // 错：浅色模式指向深色主题

        ThemeCatalog.ResolveSystemScheme(themes, appearance, ThemeKind.Light).Id.ShouldBe(ThemeIds.Light);
    }

    [Fact]
    public void 未知的选择_应回退到浅色而不是抛()
    {
        var themes = ThemeCatalog.All([]);
        var appearance = new AppearanceSettings { ThemeSelection = "missing" };

        ThemeCatalog.Find(themes, "missing").ShouldBeNull();
        ThemeCatalog.ResolveDisplayTheme(themes, appearance, "missing").Id.ShouldBe(ThemeIds.Light);
    }

    private static ThemeFileView Theme(string id, string title, ThemeBaseKind baseKind) =>
        new(
            id,
            title,
            baseKind,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AccentPrimary"] = "#FFC45C8A" },
            null,
            0.55,
            0.70);
}
