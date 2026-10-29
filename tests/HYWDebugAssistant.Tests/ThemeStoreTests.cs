using System.Globalization;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Themes;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **主题文件夹存储**：`&lt;主题根&gt;\&lt;id&gt;\theme.json` + 同目录壁纸的读 / 写 / 删与降级。
/// </summary>
/// <remarks>
/// 一条用例尽量走三个出口：① 返回值 ② 磁盘上的文件（清单 / 壁纸）③ 重新读回 ——
/// 只断言其中一个会漏（例如"写了但没落盘"或"落了盘但读不回"）。
/// </remarks>
public class ThemeStoreTests
{
    [Fact]
    public void 保存_清单与壁纸应同时落盘并可读回()
    {
        using var scope = new ThemeScope();
        var store = new ThemeStore(scope.ThemesRoot);
        var palette = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccentPrimary"] = "#FFC45C8A",
            ["BgChrome"] = "#FFFCEFF6",
        };
        byte[] wallpaper = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02];

        var result = store.Save(new ThemeSaveRequest("sakura", "樱色", ThemeBaseKind.Light, palette, wallpaper, false, 0.55, 0.70));

        result.Succeeded.ShouldBeTrue(string.Join("；", result.Failures));

        // ① 返回值（读出口）
        var view = store.Read("sakura").ShouldNotBeNull();
        view.Id.ShouldBe("sakura");
        view.Title.ShouldBe("樱色");
        view.Base.ShouldBe(ThemeBaseKind.Light);
        view.Palette["AccentPrimary"].ShouldBe("#FFC45C8A");
        view.WallpaperFileName.ShouldBe("wallpaper.png");
        view.WallpaperVeilOpacity.ShouldBe(0.55);
        view.SurfaceOpacity.ShouldBe(0.70);

        // ② 磁盘（清单形状 + 壁纸同目录）
        var directory = Path.Combine(scope.ThemesRoot, "sakura");
        var manifestPath = Path.Combine(directory, "theme.json");
        File.Exists(manifestPath).ShouldBeTrue();

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["schemaVersion"]!.GetValue<int>().ShouldBe(1);
        manifest["id"]!.GetValue<string>().ShouldBe("sakura");
        manifest["base"]!.GetValue<string>().ShouldBe("light");
        manifest["wallpaper"]!.GetValue<string>().ShouldBe("wallpaper.png");
        manifest["palette"]!["AccentPrimary"]!.GetValue<string>().ShouldBe("#FFC45C8A");

        File.Exists(Path.Combine(directory, "wallpaper.png")).ShouldBeTrue("壁纸必须与清单同目录（主题自包含）");
        store.TryReadWallpaper("sakura", out var bytes).ShouldBeTrue();
        bytes.ShouldNotBeNull();
        bytes!.SequenceEqual(wallpaper).ShouldBeTrue();

        // ③ 重新枚举（目录 = 文件夹）
        store.LoadAll().ShouldHaveSingleItem().Id.ShouldBe("sakura");
    }

    [Fact]
    public void 保存_不换图时应保留现有壁纸_清除时应删文件并去掉字段()
    {
        using var scope = new ThemeScope();
        var store = new ThemeStore(scope.ThemesRoot);

        store.Save(new ThemeSaveRequest("sakura", "樱色", ThemeBaseKind.Light, EmptyPalette(), [1, 2, 3], false, 0.55, 0.70))
            .Succeeded.ShouldBeTrue();

        // 只改标题（不换图、不删除）⇒ 壁纸原样保留
        store.Save(new ThemeSaveRequest("sakura", "樱色 2", ThemeBaseKind.Light, EmptyPalette(), null, false, 0.55, 0.70))
            .Succeeded.ShouldBeTrue();

        var kept = store.Read("sakura").ShouldNotBeNull();
        kept.Title.ShouldBe("樱色 2");
        kept.WallpaperFileName.ShouldBe("wallpaper.png");
        store.TryReadWallpaper("sakura", out _).ShouldBeTrue();

        // 清除 ⇒ 清单不再有 wallpaper，文件被删
        store.Save(new ThemeSaveRequest("sakura", "樱色 2", ThemeBaseKind.Light, EmptyPalette(), null, true, 0.55, 0.70))
            .Succeeded.ShouldBeTrue();

        var cleared = store.Read("sakura").ShouldNotBeNull();
        cleared.WallpaperFileName.ShouldBeNull();

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(scope.ThemesRoot, "sakura", "theme.json")))!.AsObject();
        manifest.ContainsKey("wallpaper").ShouldBeFalse("没有壁纸就不该写这条");
        store.TryReadWallpaper("sakura", out _).ShouldBeFalse();
    }

    [Fact]
    public void 保存_非法id与非法token键应被拒绝()
    {
        using var scope = new ThemeScope();
        var store = new ThemeStore(scope.ThemesRoot);

        store.Save(new ThemeSaveRequest("light", "保留名", ThemeBaseKind.Light, EmptyPalette(), null, false, 0.55, 0.70))
            .Succeeded.ShouldBeFalse("light 是保留名");

        store.Save(new ThemeSaveRequest("../../evil", "越界", ThemeBaseKind.Light, EmptyPalette(), null, false, 0.55, 0.70))
            .Succeeded.ShouldBeFalse("id 不能带路径分隔符");

        var badToken = new Dictionary<string, string>(StringComparer.Ordinal) { ["not a token"] = "#FFFFFF" };
        var result = store.Save(new ThemeSaveRequest("ok-theme", "测试", ThemeBaseKind.Light, badToken, null, false, 0.55, 0.70));

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldContain(failure => failure.Contains("token", StringComparison.Ordinal));

        Directory.Exists(Path.Combine(scope.ThemesRoot, "ok-theme")).ShouldBeFalse("被拒绝的保存不得留下半成品目录");
    }

    [Fact]
    public void 坏清单_应跳过并记Warning_不阻断其它主题()
    {
        using var scope = new ThemeScope();
        var sink = new CollectingIssueSink();
        var store = new ThemeStore(scope.ThemesRoot, sink);

        store.Save(new ThemeSaveRequest("good", "好主题", ThemeBaseKind.Light, EmptyPalette(), null, false, 0.55, 0.70))
            .Succeeded.ShouldBeTrue();

        // ① 版本不认识
        WriteManifest(scope, "future", """{ "schemaVersion": 9, "title": "未来" }""");

        // ② 壁纸名越界
        WriteManifest(scope, "escape", """{ "schemaVersion": 1, "title": "越界", "wallpaper": "../evil.png" }""");

        // ③ 根本不是 JSON
        WriteManifest(scope, "broken", "{ not json");

        var themes = store.LoadAll();

        themes.ShouldHaveSingleItem().Id.ShouldBe("good");
        sink.Issues.Count(issue => issue.Issue.Severity == IssueSeverity.Warning).ShouldBe(3);
        sink.Issues.Select(issue => issue.Issue.Message).ShouldContain(message => message.Contains("future", StringComparison.Ordinal));
        sink.Issues.Select(issue => issue.Issue.Message).ShouldContain(message => message.Contains("壁纸文件名越界", StringComparison.Ordinal));

        // 同一份坏清单只报一次（查询面会被反复调用）
        _ = store.LoadAll();
        sink.Issues.Count.ShouldBe(3);
    }

    [Fact]
    public void 读取_id以文件夹名为准_清单里的id只是说明()
    {
        using var scope = new ThemeScope();
        var store = new ThemeStore(scope.ThemesRoot);

        WriteManifest(scope, "copied", """{ "schemaVersion": 1, "id": "origin", "title": "复制来的", "base": "dark" }""");

        var themes = store.LoadAll();
        themes.ShouldHaveSingleItem().Id.ShouldBe("copied", "复制文件夹后改名仍应可用（id = 文件夹名）");
        themes[0].Base.ShouldBe(ThemeBaseKind.Dark);
        store.Read("origin").ShouldBeNull();
    }

    [Fact]
    public void 删除_应删掉整个文件夹且重复删除不报错()
    {
        using var scope = new ThemeScope();
        var store = new ThemeStore(scope.ThemesRoot);

        store.Save(new ThemeSaveRequest("sakura", "樱色", ThemeBaseKind.Light, EmptyPalette(), [1, 2, 3], false, 0.55, 0.70))
            .Succeeded.ShouldBeTrue();

        store.Delete("sakura").Succeeded.ShouldBeTrue();
        Directory.Exists(Path.Combine(scope.ThemesRoot, "sakura")).ShouldBeFalse();
        store.Exists("sakura").ShouldBeFalse();

        // 幂等：不存在视为已删除
        store.Delete("sakura").Succeeded.ShouldBeTrue();
        store.Delete("light").Succeeded.ShouldBeFalse("保留名不是主题，不能删");
    }

    [Fact]
    public void 保存_遮罩与表面透明度越界应按边界收敛()
    {
        using var scope = new ThemeScope();
        var store = new ThemeStore(scope.ThemesRoot);

        store.Save(new ThemeSaveRequest("clamped", "收敛", ThemeBaseKind.Dark, EmptyPalette(), null, false, 5, -3))
            .Succeeded.ShouldBeTrue();

        var view = store.Read("clamped").ShouldNotBeNull();
        view.WallpaperVeilOpacity.ShouldBe(0.9);
        view.SurfaceOpacity.ShouldBe(0.0);
    }

    /// <summary>
    /// **浮点字段按不变文化解析**：清单里写的是 `0.55` / `0.70`，在 `,` 为小数分隔符的机器（de-DE）上
    /// 若按当前文化解析会被当成千位分隔符（55 / 70），再被 clamp 成 0.9 / 1.0 —— 表现为"主题遮罩突然最浓"。
    /// </summary>
    [Fact]
    public void 浮点字段_在逗号小数分隔符文化下仍按不变文化读取()
    {
        using var scope = new ThemeScope();
        var store = new ThemeStore(scope.ThemesRoot);

        WriteManifest(
            scope,
            "locale",
            """{ "schemaVersion": 1, "title": "区域", "wallpaperVeilOpacity": 0.55, "surfaceOpacity": 0.70 }""");

        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var view = store.Read("locale").ShouldNotBeNull();
            view.WallpaperVeilOpacity.ShouldBe(0.55);
            view.SurfaceOpacity.ShouldBe(0.70);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static Dictionary<string, string> EmptyPalette() => new(StringComparer.Ordinal);

    private static void WriteManifest(ThemeScope scope, string id, string json)
    {
        var directory = Path.Combine(scope.ThemesRoot, id);
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "theme.json"), json);
    }

    /// <summary>测试用临时主题根。</summary>
    private sealed class ThemeScope : IDisposable
    {
        public ThemeScope()
        {
            ThemesRoot = Path.Combine(Path.GetTempPath(), "hyw-theme-store-" + Guid.NewGuid().ToString("N"));
        }

        public string ThemesRoot { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(ThemesRoot))
                {
                    Directory.Delete(ThemesRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }
}
