using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Seeding;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **出厂种子投放**（`resources\seeds\` → 程序目录）：首次投放、幂等、删了不复活、坏种子跳过、缺种子仍启动。
/// </summary>
/// <remarks>
/// 一条用例尽量走三个出口：① 返回值 / 接口 ② 磁盘上的文件 ③ 重新读回（新实例再 <c>Load</c>）——
/// 只断言其中一个会漏（例如"投放了但没记 seeded"或"记了但文件没落盘"）。
/// </remarks>
public class ThemeSeedTests
{
    [Fact]
    public void 首次启动_应投放设置文件与出厂主题()
    {
        using var temp = new SeedScope();
        temp.WriteSettingsSeed();

        var service = temp.CreateService();
        service.Load().ShouldBe(SettingsLoadOutcome.Loaded, "设置文件由单文件种子投放后即可读取");

        // ① 单文件：设置文件出现（内容就是种子那份）
        File.Exists(temp.SettingsPath).ShouldBeTrue();

        // ② 主题单元：整目录投放（清单 + 同目录壁纸），且可读
        var theme = service.Themes.Read(SeedScope.SeedThemeId).ShouldNotBeNull();
        theme.Base.ShouldBe(ThemeBaseKind.Light);
        theme.WallpaperFileName.ShouldBe("wallpaper.png");
        theme.WallpaperVeilOpacity.ShouldBe(0.55);
        theme.SurfaceOpacity.ShouldBe(0.70);
        theme.Palette["AccentPrimary"].ShouldBe("#FFC45C8A");
        File.Exists(temp.Target("settings/appearance/themes/sakura/wallpaper.png"))
            .ShouldBeTrue("壁纸与清单同目录（主题自包含）");

        // ③ seeded 记录（相对程序目录），并且真的落盘
        service.Current.Appearance.Seeded.ShouldContain("settings/appearance/themes/sakura");

        var persisted = System.Text.Json.Nodes.JsonNode
            .Parse(File.ReadAllText(temp.SettingsPath))!
            .AsObject();
        persisted["schemaVersion"]!.GetValue<int>().ShouldBe(HYWDebugAssistant.Core.Api.ApplicationSettings.CurrentSchemaVersion);
        persisted["appearance"]!["seeded"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ShouldContain("settings/appearance/themes/sakura");
    }

    [Fact]
    public void 无设置种子_应用代码默认值并投放主题()
    {
        using var temp = new SeedScope();   // 只有主题种子，没有 settings.json 种子

        var service = temp.CreateService();
        service.Load().ShouldBe(SettingsLoadOutcome.DefaultsCreated);

        service.Current.Flow.QueueCapacity.ShouldBe(
            new ApplicationSettings().Flow.QueueCapacity,
            "没有设置文件 ⇒ 代码默认值（缺省只在设置模型里给一次）");
        service.Themes.Exists(SeedScope.SeedThemeId).ShouldBeTrue("出厂主题不该因为缺设置文件而缺席");
        service.Current.Appearance.Seeded.ShouldContain("settings/appearance/themes/sakura");
        File.Exists(temp.SettingsPath).ShouldBeTrue("投放改变了设置 ⇒ 需要落盘记录 seeded");
    }

    [Fact]
    public void 投放器_只投放缺失的单元与文件()
    {
        using var temp = new SeedScope();
        temp.WriteSettingsSeed();

        var seeder = new SeedService(temp.TargetRoot, temp.SeedsRoot);

        // 单文件：目标不存在才复制；复制过就不再复制
        seeder.SeedFiles().ShouldBe(["settings/settings.json"]);
        seeder.SeedFiles().ShouldBeEmpty("文件存在即证明，不需要记录");

        // 叶子目录 = 一个投放单元，返回相对程序目录的路径
        seeder.SeedUnits([]).ShouldBe(["settings/appearance/themes/sakura"]);
        seeder.SeedUnits(["settings/appearance/themes/sakura"]).ShouldBeEmpty("已记录 ⇒ 不再投放（删了也不复活）");
        seeder.SeedUnits([]).ShouldBeEmpty("目标已存在 ⇒ 整单元跳过（不改写用户内容）");
    }

    [Fact]
    public void 重复启动_不重投也不覆盖用户改动()
    {
        using var temp = new SeedScope();
        var first = temp.CreateService();
        first.Load();

        // 用户改过这个主题：改标题（清单）+ 留一个自己的文件
        var directory = Path.Combine(first.Themes.ThemesRoot, SeedScope.SeedThemeId);
        File.WriteAllText(Path.Combine(directory, "user-note.txt"), "用户自己的文件");
        var editedTitle = "我改过的樱色";
        first.Themes.Save(new ThemeSaveRequest(
            SeedScope.SeedThemeId,
            editedTitle,
            ThemeBaseKind.Light,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AccentPrimary"] = "#010203" },
            null,
            ClearWallpaper: false,
            0.3,
            0.4)).Succeeded.ShouldBeTrue();

        // 顺带改一个设置：确认重启后生效的是设置文件（单文件种子不覆盖它）
        var updated = first.Current.Clone();
        updated.SnapDistance = 6;
        first.Update(updated).Succeeded.ShouldBeTrue();

        var reopened = temp.CreateService();
        reopened.Load();

        var theme = reopened.Themes.Read(SeedScope.SeedThemeId).ShouldNotBeNull();
        theme.Title.ShouldBe(editedTitle, "已存在的主题目录一个字都不该被种子改写");
        theme.Palette["AccentPrimary"].ShouldBe("#010203");
        File.Exists(Path.Combine(directory, "user-note.txt")).ShouldBeTrue("整单元投放不得清掉目录里的其它文件");
        reopened.Current.SnapDistance.ShouldBe(6, "单文件种子不得覆盖已存在的设置文件");

        reopened.Current.Appearance.Seeded
            .Count(entry => entry.Contains("sakura", StringComparison.Ordinal))
            .ShouldBe(1, "重复启动不得重复记 seeded");
    }

    [Fact]
    public void 删除出厂主题后_重启不复活()
    {
        using var temp = new SeedScope();
        var first = temp.CreateService();
        first.Load();
        first.Themes.Exists(SeedScope.SeedThemeId).ShouldBeTrue();

        first.Themes.Delete(SeedScope.SeedThemeId).Succeeded.ShouldBeTrue();

        var reopened = temp.CreateService();
        reopened.Load();

        reopened.Themes.Exists(SeedScope.SeedThemeId).ShouldBeFalse("seeded 已记录 ⇒ 用户删除后不复活");
        reopened.Current.Appearance.Seeded.ShouldContain("settings/appearance/themes/sakura");
    }

    [Fact]
    public void 坏主题种子_应跳过并记Warning且不记seeded()
    {
        using var temp = new SeedScope();
        temp.WriteThemeSeed(manifest: "{ not json");

        var service = temp.CreateService();
        service.Load().ShouldNotBe(SettingsLoadOutcome.DefaultsAfterFailure, "坏种子不阻断启动");

        service.Themes.Exists(SeedScope.SeedThemeId).ShouldBeFalse("坏清单的主题不进目录");
        temp.Runtime.Issues.Recent(20).ShouldContain(
            entry => entry.Issue.Severity == IssueSeverity.Warning);
        service.Current.Appearance.Seeded.ShouldNotContain(
            "settings/appearance/themes/sakura",
            "失败不记 seeded ⇒ 下次启动重试");
        Directory.Exists(temp.Target("settings/appearance/themes/sakura"))
            .ShouldBeFalse("本次刚投放的半成品要撤掉，否则'目标已存在'会让重试永远不再发生");

        // 修好种子后重启能补投 —— 这正是"不记 seeded"的用途
        temp.WriteThemeSeed();
        var retried = temp.CreateService();
        retried.Load();

        retried.Themes.Exists(SeedScope.SeedThemeId).ShouldBeTrue();
        retried.Current.Appearance.Seeded.ShouldContain("settings/appearance/themes/sakura");
    }

    [Fact]
    public void 缺种子_仍以代码默认值启动()
    {
        using var temp = new SeedScope(createSeeds: false);

        var service = temp.CreateService();
        service.Load().ShouldBe(SettingsLoadOutcome.DefaultsCreated);

        service.Current.Flow.QueueCapacity.ShouldBe(new ApplicationSettings().Flow.QueueCapacity);
        service.Themes.LoadAll().ShouldBeEmpty();
        temp.Runtime.Issues.Count.ShouldBe(0, "没有出厂种子不是异常（删掉 resources\\ 的安装照样启动）");
    }

    [Fact]
    public void 旧版本设置_不迁移也不写回_且不投放种子()
    {
        using var temp = new SeedScope();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.SettingsPath)!);

        var original = """
{
  "schemaVersion": 3,
  "appearance": {
    "theme": "sakura",
    "systemLightScheme": "sakura",
    "systemDarkScheme": "sakura",
    "seededThemes": [ "sakura" ],
    "schemes": [ { "id": "sakura", "title": "樱色", "base": "light" } ],
    "paletteOverrides": { "sakura": { "AccentPrimary": "#112233" } }
  }
}
""";
        File.WriteAllText(temp.SettingsPath, original);

        var service = temp.CreateService();
        service.Load().ShouldBe(SettingsLoadOutcome.Loaded);

        // 只读已知标量键；外观主题回落内置（自定义主题没有文件夹 ⇒ 跟随系统 / 内置浅深）
        service.Current.Appearance.ThemeSelection.ShouldBe(ThemeIds.System);
        service.Current.Appearance.SystemLightScheme.ShouldBe(ThemeIds.Light);
        service.Current.Appearance.SystemDarkScheme.ShouldBe(ThemeIds.Dark);

        // 旧文件**不写回**（保护原文件），也不投放（投了没地方记，删了会复活）
        File.ReadAllText(temp.SettingsPath).ShouldBe(original, "旧版本设置载入时不得被改写");
        service.Themes.Exists(SeedScope.SeedThemeId).ShouldBeFalse("旧版本设置不投放种子");

        temp.Runtime.Issues.Recent(10).ShouldContain(
            entry => entry.Issue.Severity == IssueSeverity.Warning);
    }

    /// <summary>出厂种子 + 程序目录的临时布局（种子根与目标根分开，互不污染）。</summary>
    private sealed class SeedScope : IDisposable
    {
        /// <summary>出厂种子主题的 id（= 种子里的文件夹名；本夹具自己声明，不再借用产品常量）。</summary>
        public const string SeedThemeId = "sakura";

        private const string SakuraManifest = """
{
  "schemaVersion": 1,
  "id": "sakura",
  "title": "樱色",
  "base": "light",
  "wallpaper": "wallpaper.png",
  "wallpaperVeilOpacity": 0.55,
  "surfaceOpacity": 0.70,
  "palette": {
    "AccentPrimary": "#FFC45C8A",
    "BgChrome": "#FFFCEFF6"
  }
}
""";

        private static readonly byte[] WallpaperBytes = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02];

        private readonly string _physicalRoot =
            Path.Combine(Path.GetTempPath(), "hyw-seed-" + Guid.NewGuid().ToString("N"));

        public SeedScope(bool createSeeds = true)
        {
            _ = Directory.CreateDirectory(SeedsRoot);
            _ = Directory.CreateDirectory(TargetRoot);
            Runtime = new ModuleRuntime();
            SettingsPath = Path.Combine(TargetRoot, "settings", "settings.json");

            if (createSeeds)
            {
                WriteThemeSeed();
            }
        }

        public ModuleRuntime Runtime { get; }

        public string SeedsRoot => Path.Combine(_physicalRoot, "resources", "seeds");

        public string TargetRoot => Path.Combine(_physicalRoot, "app");

        public string SettingsPath { get; }

        public string Target(string relative) =>
            Path.Combine(TargetRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        public SettingsService CreateService() => new(
            Runtime,
            SettingsPath,
            seedsRoot: SeedsRoot,
            themesRoot: Path.Combine(TargetRoot, "settings", "appearance", "themes"),
            seedTargetRoot: TargetRoot);

        /// <summary>写一份"出厂默认设置"种子（内容与 `--emit-default-settings` 同一份映射）。</summary>
        public void WriteSettingsSeed() =>
            WriteFile(
                Path.Combine(SeedsRoot, "settings", "settings.json"),
                SettingsService.Serialize(new ApplicationSettings()));

        /// <summary>写一个主题种子（清单坏掉时传 <paramref name="manifest"/> 模拟坏种子）。</summary>
        public void WriteThemeSeed(string? manifest = null)
        {
            var directory = Path.Combine(SeedsRoot, "settings", "appearance", "themes", SeedThemeId);
            _ = Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, "theme.json"), manifest ?? SakuraManifest);
            File.WriteAllBytes(Path.Combine(directory, "wallpaper.png"), WallpaperBytes);
        }

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                if (Directory.Exists(_physicalRoot))
                {
                    Directory.Delete(_physicalRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }

        private static void WriteFile(string path, string text)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
