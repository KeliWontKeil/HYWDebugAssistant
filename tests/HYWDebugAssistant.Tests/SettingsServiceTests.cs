using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T4（M1-7）：应用级设置——加载容错、校验、即时生效、原子写、**未知字段片段保留**。
/// </summary>
public class SettingsServiceTests
{
    [Fact]
    public void 加载_文件不存在时应使用默认设置且不报错()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);

        service.Load().ShouldBe(SettingsLoadOutcome.DefaultsCreated);
        service.Current.InputQueueCapacity.ShouldBe(1024);
        service.Current.StopTimeoutSeconds.ShouldBe(new ApplicationSettings().StopTimeoutSeconds, "缺省只在设置模型里给一次");
        runtime.Issues.Count.ShouldBe(0, "首次运行不是异常");
    }

    [Fact]
    public void 加载_文件损坏时应使用默认设置并报Issue但不阻断()
    {
        using var scope = new SettingsScope();
        File.WriteAllText(scope.SettingsPath, "{ this is not json ");

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);

        service.Load().ShouldBe(SettingsLoadOutcome.DefaultsAfterFailure);
        service.Current.InputQueueCapacity.ShouldBe(1024);
        runtime.Issues.Count.ShouldBe(1);
        runtime.Issues.Recent(1)[0].Issue.Category.ShouldBe(Sdk.Reporting.IssueCategory.ConfigurationError);
    }

    [Fact]
    public void 更新_合法设置应即时生效并原子写盘()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var updated = service.Current.Clone();
        updated.InputQueueCapacity = 256;
        updated.StopTimeoutSeconds = 2.5;
        updated.AlignAutoNetworkEnabled = false;

        var result = service.Update(updated);

        result.Succeeded.ShouldBeTrue(result.Failures.Count > 0 ? result.Failures[0] : null);

        // 即时生效：承载参数与建网开关已更新
        runtime.Carriers.Options.QueueCapacity.ShouldBe(256);
        runtime.Carriers.Options.StopTimeout.ShouldBe(TimeSpan.FromSeconds(2.5));
        runtime.AlignAutoNetwork.ShouldBeFalse();

        // 已落盘
        File.Exists(scope.SettingsPath).ShouldBeTrue();
        var text = File.ReadAllText(scope.SettingsPath);
        text.ShouldContain("\"inputQueueCapacity\": 256");
        text.ShouldContain("\"stopTimeoutSeconds\": 2.5");
    }

    [Fact]
    public void 更新_吸附距离应落盘并往返()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        service.Current.SnapDistance.ShouldBe(4, "默认 4（ADR-0042 D6）");

        var updated = service.Current.Clone();
        updated.SnapDistance = 6;
        service.Update(updated).Succeeded.ShouldBeTrue();

        var text = File.ReadAllText(scope.SettingsPath);
        text.ShouldContain("\"snapDistance\": 6");

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.SnapDistance.ShouldBe(6, "重新加载应保留用户设置");
    }

    /// <summary>
    /// **低版本（v3 及更低）设置**：只读已知标量键，外观主题回落内置，**不做内联主题迁移**，
    /// 且**载入时不得写回**（原文件保持原样）。
    /// </summary>
    /// <remarks>
    /// 一条用例走三个出口：① 读到的值 ② 警告 ③ 文件字节未变 ——
    /// 少了 ③ 就可能"读对了但把用户的原文件悄悄改写成 v4"。
    /// </remarks>
    [Fact]
    public void 加载_旧版本设置应只读标量并回落内置且不写回()
    {
        using var scope = new SettingsScope();
        var original = """
        {
          "schemaVersion": 3,
          "inputQueueCapacity": 512,
          "appearance": {
            "theme": "sakura",
            "systemLightScheme": "sakura",
            "systemDarkScheme": "sakura",
            "schemes": [ { "id": "sakura", "title": "樱色", "base": "light" } ],
            "seededThemes": [ "sakura" ],
            "paletteOverrides": { "sakura": { "AccentPrimary": "#112233" } },
            "fonts": { "ui": "Georgia", "data": "Cascadia Code" }
          }
        }
        """;
        File.WriteAllText(scope.SettingsPath, original);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load().ShouldBe(SettingsLoadOutcome.Loaded);

        // ① 已知标量键照读
        service.Current.InputQueueCapacity.ShouldBe(512);
        service.Current.Appearance.UiFontFamily.ShouldBe("Georgia");
        service.Current.Appearance.DataFontFamily.ShouldBe("Cascadia Code");

        // 外观主题回落内置：旧自定义主题没有文件夹 ⇒ 选择回落"跟随系统"，两种模式回到内置浅深
        service.Current.Appearance.ThemeSelection.ShouldBe(ThemeIds.System);
        service.Current.Appearance.SystemLightScheme.ShouldBe(ThemeIds.Light);
        service.Current.Appearance.SystemDarkScheme.ShouldBe(ThemeIds.Dark);

        // ② 一条 Warning
        runtime.Issues.Recent(10).ShouldContain(
            entry => entry.Issue.Severity == IssueSeverity.Warning
                && entry.Issue.Message.Contains("旧版本", StringComparison.Ordinal));

        // ③ 原文件保持不变（没有把内联主题"迁移"并写回）
        File.ReadAllText(scope.SettingsPath).ShouldBe(original);
    }

    /// <summary>外观块 v4 的形状：`theme / systemLightScheme / systemDarkScheme / seeded[] / fonts`，不再有内联主题。</summary>
    [Fact]
    public void 往返_外观块应写成v4形状()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var updated = service.Current.Clone();
        updated.Appearance.ThemeSelection = "custom-abc";
        updated.Appearance.SystemLightScheme = ThemeIds.Light;
        updated.Appearance.SystemDarkScheme = ThemeIds.Dark;
        updated.Appearance.UiFontFamily = "Georgia";
        updated.Appearance.DataFontFamily = "Cascadia Code";
        updated.Appearance.Seeded = ["appearance/themes/sakura"];

        // 选择必须指向真实存在的主题（否则载入时会回落内置）
        service.Themes.Save(new ThemeSaveRequest(
            "custom-abc",
            "自定义 ABC",
            ThemeBaseKind.Light,
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            ClearWallpaper: false,
            0.55,
            0.70)).Succeeded.ShouldBeTrue();

        service.Update(updated).Succeeded.ShouldBeTrue();

        var root = JsonNode.Parse(File.ReadAllText(scope.SettingsPath))!.AsObject();
        root["schemaVersion"]!.GetValue<int>().ShouldBe(4);

        var block = root["appearance"]!.AsObject();
        block["theme"]!.GetValue<string>().ShouldBe("custom-abc");
        block["systemLightScheme"]!.GetValue<string>().ShouldBe(ThemeIds.Light);
        block["systemDarkScheme"]!.GetValue<string>().ShouldBe(ThemeIds.Dark);
        block["fonts"]!["ui"]!.GetValue<string>().ShouldBe("Georgia");
        block["fonts"]!["data"]!.GetValue<string>().ShouldBe("Cascadia Code");
        block["seeded"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["appearance/themes/sakura"]);

        block.ContainsKey("schemes").ShouldBeFalse("外观块不再内联主题方案");
        block.ContainsKey("paletteOverrides").ShouldBeFalse("外观块不再内联色板覆盖");
        block.ContainsKey("seededThemes").ShouldBeFalse("seededThemes 已折进 seeded");

        // 重启读回
        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Appearance.ThemeSelection.ShouldBe("custom-abc", "主题选择是标量，原样保留");
        reloaded.Current.Appearance.Seeded.ShouldContain("appearance/themes/sakura");
        reloaded.Current.Appearance.UiFontFamily.ShouldBe("Georgia");
    }

    [Fact]
    public void 加载_已删除的主题在设置里的选择应回落内置()
    {
        using var scope = new SettingsScope();
        File.WriteAllText(scope.SettingsPath, """
        {
          "schemaVersion": 4,
          "appearance": {
            "theme": "gone-theme",
            "systemLightScheme": "gone-theme",
            "systemDarkScheme": "gone-theme"
          }
        }
        """);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        service.Current.Appearance.ThemeSelection.ShouldBe(ThemeIds.System, "主题文件夹不存在 ⇒ 跟随系统（内置）");
        service.Current.Appearance.SystemLightScheme.ShouldBe(ThemeIds.Light);
        service.Current.Appearance.SystemDarkScheme.ShouldBe(ThemeIds.Dark);
    }

    [Fact]
    public void 更新_吸附距离超出范围应被拒绝()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var baseline = service.Current.SnapDistance;
        var invalid = service.Current.Clone();
        invalid.SnapDistance = 20;

        var result = service.Update(invalid);

        result.Succeeded.ShouldBeFalse();
        result.Failures[0].ShouldContain("吸附距离");
        service.Current.SnapDistance.ShouldBe(baseline, "被拒绝的更新不得改变当前值");
    }

    [Fact]
    public void 更新_非法值应被拒绝且不改变当前设置()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var baseline = service.Current.InputQueueCapacity;
        var invalid = service.Current.Clone();
        invalid.InputQueueCapacity = 0;

        var result = service.Update(invalid);

        result.Succeeded.ShouldBeFalse();
        result.Failures[0].ShouldContain("输入队列容量");
        service.Current.InputQueueCapacity.ShouldBe(baseline, "被拒绝的更新不得改变当前值");
    }

    /// <summary>
    /// **字体设置必须真的生效并落盘**（改字体必须与改颜色一样立即生效、可保存）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>真因</b>：`ApplicationSettings.Clone()` **漏拷** `UiFontFamily`/`DataFontFamily`，而 Clone 是
    /// **读**（`RuntimeServices.GetSettings`）与**写**（`SettingsService.Update` 的 `Current = settings.Clone()`）
    /// 的必经之路 ⇒ 落盘写默认值、读回默认值、交给 `ThemeApplier` 的也是默认值。
    /// </para>
    /// <para>
    /// <b>本用例为什么能拦住它</b>：它**同时走三个出口** —— ① 公开读出口（`GetSettings` = Clone）
    /// ② 文件出口（settings.json 的键值）③ 重启读回出口；并且多一条**反向断言**（④ 改别的设置不得把字体冲回默认，
    /// 这正是"用户改一次吸附距离，字体就悄悄回默认"的那条路径）。只断言其中一个出口都会漏。
    /// </para>
    /// </remarks>
    [Fact]
    public void 更新_界面字体与数据字体应生效并落盘往返()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var services = new RuntimeServices(runtime, service);

        // 用户改完点「应用」：界面 → 设置对象（`GetSettings` 给的是副本 ⇒ 这里同时验证了读出口带字体）
        var view = services.GetSettings();
        view.Appearance.UiFontFamily = "Georgia";
        view.Appearance.DataFontFamily = "Cascadia Code";

        services.UpdateSettings(view).Succeeded.ShouldBeTrue();

        // ① 写出口（生效值）
        services.GetSettings().Appearance.UiFontFamily.ShouldBe("Georgia", "写完再读必须还是用户选的那个");
        services.GetSettings().Appearance.DataFontFamily.ShouldBe("Cascadia Code");

        // ② 文件出口（外观块里的字体轴：`appearance.fonts.{ui,data}`）
        var text = File.ReadAllText(scope.SettingsPath);
        text.ShouldContain("\"ui\": \"Georgia\"");
        text.ShouldContain("\"data\": \"Cascadia Code\"");

        // ③ 重启读回出口
        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Appearance.UiFontFamily.ShouldBe("Georgia", "重新加载应保留用户设置");
        reloaded.Current.Appearance.DataFontFamily.ShouldBe("Cascadia Code");

        // ④ 反向断言：改一个**别的**设置（界面上最常见的动作）不得把字体冲回默认值
        var again = services.GetSettings();
        again.SnapDistance = 6;
        services.UpdateSettings(again).Succeeded.ShouldBeTrue();
        services.GetSettings().Appearance.UiFontFamily.ShouldBe("Georgia", "别的设置落盘时，字体不得被自己的副本冲回默认");
        services.GetSettings().Appearance.DataFontFamily.ShouldBe("Cascadia Code");
    }

    /// <summary>
    /// **`Clone()` 必须与全部可写属性一一对应**（"漏一个字段就静默失效"的结构性防线）。
    /// </summary>
    /// <remarks>
    /// 用户实测的字体缺陷就是"Clone 漏了两个字段"—— 这类缺陷**不会报错、不会红**，只表现为"改了没反应"。
    /// 本用例对**每一个**可写属性赋一个非默认哨兵值再 Clone，逐字段比对 ⇒ 将来任何人加字段忘了补 Clone 会当场红。
    /// 新增属性时若类型未登记，哨兵工厂会**显式抛出并带上属性名**（宁可红，不要静默放过）。
    /// </remarks>
    [Fact]
    public void Clone_必须复制全部可写属性()
    {
        var fresh = new ApplicationSettings();

        foreach (var property in typeof(ApplicationSettings)
            .GetProperties()
            .Where(p => p is { CanRead: true, CanWrite: true }))
        {
            var sentinel = property.PropertyType == typeof(bool)
                ? !(bool)property.GetValue(fresh)!
                : Sentinel(property.PropertyType, property.Name);

            property.SetValue(fresh, sentinel);

            var copied = property.GetValue(fresh.Clone());

            AssertCopied(property.Name, sentinel, copied);
        }
    }

    /// <summary>为某一类型的属性造一个**非默认**哨兵值。</summary>
    private static object Sentinel(Type type, string name) => true switch
    {
        _ when type == typeof(string) => $"sentinel-{name}",
        _ when type == typeof(int) => 987_654,
        _ when type == typeof(double) => 12.5,
        _ when type == typeof(IList<string>) => new List<string> { $"dir-{name}" },
        _ when type == typeof(IDictionary<string, string>) => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"key-{name}"] = "value",
        },
        // **外观块**（选择 / 跟随系统两套 / 已投放 / 字体）：逐项给一个哨兵值
        _ when type == typeof(HYWDebugAssistant.Core.Api.AppearanceSettings) => new HYWDebugAssistant.Core.Api.AppearanceSettings
        {
            ThemeSelection = $"sentinel-{name}",
            SystemLightScheme = $"sentinel-light-{name}",
            SystemDarkScheme = $"sentinel-dark-{name}",
            UiFontFamily = $"sentinel-ui-{name}",
            DataFontFamily = $"sentinel-data-{name}",
            Seeded = { $"appearance/themes/sentinel-{name}" },
        },
        _ => throw new NotSupportedException(
            $"新增了可写属性 {name}（{type.Name}）：请在 {nameof(Sentinel)} 里补一个哨兵值，让 Clone 完整性用例继续生效"),
    };

    /// <summary>比对"克隆出来的值"与"写进去的哨兵值"（集合按内容比，字典按键值比）。</summary>
    private static void AssertCopied(string name, object expected, object? actual)
    {
        switch (expected)
        {
            case HYWDebugAssistant.Core.Api.AppearanceSettings appearance:
                var copiedAppearance = actual.ShouldBeAssignableTo<HYWDebugAssistant.Core.Api.AppearanceSettings>()!;
                copiedAppearance.ThemeSelection.ShouldBe(appearance.ThemeSelection, $"{name}：Clone 漏了外观块（主题选择）");
                copiedAppearance.SystemLightScheme.ShouldBe(appearance.SystemLightScheme, $"{name}：Clone 漏了跟随系统浅色主题");
                copiedAppearance.SystemDarkScheme.ShouldBe(appearance.SystemDarkScheme, $"{name}：Clone 漏了跟随系统深色主题");
                copiedAppearance.UiFontFamily.ShouldBe(appearance.UiFontFamily, $"{name}：Clone 漏了外观块（界面字体）");
                copiedAppearance.DataFontFamily.ShouldBe(appearance.DataFontFamily, $"{name}：Clone 漏了外观块（数据字体）");

                copiedAppearance.Seeded.Count.ShouldBe(appearance.Seeded.Count, $"{name}：Clone 漏了外观块的已投放列表");
                for (var index = 0; index < appearance.Seeded.Count; index++)
                {
                    copiedAppearance.Seeded[index].ShouldBe(appearance.Seeded[index], $"{name}：Clone 漏了已投放记录");
                }

                ReferenceEquals(actual, expected).ShouldBeFalse($"{name}：外观块必须深拷（否则改一份会影响另一份）");
                break;

            case IDictionary<string, string> dictionary:
                var copiedDictionary = actual.ShouldBeAssignableTo<IDictionary<string, string>>()!;
                copiedDictionary.Count.ShouldBe(dictionary.Count, $"{name}：Clone 漏了这个字典字段");
                foreach (var (key, value) in dictionary)
                {
                    copiedDictionary[key].ShouldBe(value, $"{name}：Clone 漏了这个字典字段");
                }

                break;

            case IList<string> list:
                actual.ShouldBeAssignableTo<IList<string>>()!.ShouldBe(list, $"{name}：Clone 漏了这个列表字段");
                break;

            default:
                actual.ShouldBe(expected, $"{name}：Clone 漏了这个字段（用户实测「改了没反应」的那一类缺陷）");
                break;
        }
    }

    [Fact]
    public void 往返_应保留未识别字段()
    {
        using var scope = new SettingsScope();
        // 预置一个含**未知字段**的文件（模拟更高版本写下的设置）
        File.WriteAllText(scope.SettingsPath, """
        {
          "schemaVersion": 1,
          "inputQueueCapacity": 512,
          "futureOption": "由更高版本写入",
          "nested": { "a": 1, "b": [1, 2, 3] }
        }
        """);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load().ShouldBe(SettingsLoadOutcome.Loaded);
        service.Current.InputQueueCapacity.ShouldBe(512);

        // 更新一个已知字段
        var updated = service.Current.Clone();
        updated.UiRefreshRateHz = 30;
        service.Update(updated).Succeeded.ShouldBeTrue();

        // 未知字段原样保留（AC-4 同策略）
        var root = JsonNode.Parse(File.ReadAllText(scope.SettingsPath))!.AsObject();
        root["futureOption"]!.GetValue<string>().ShouldBe("由更高版本写入");
        root["nested"]!["b"]!.AsArray().Count.ShouldBe(3);
        root["uiRefreshRateHz"]!.GetValue<int>().ShouldBe(30);
        root["inputQueueCapacity"]!.GetValue<int>().ShouldBe(512, "未修改的已知字段保持原值");
    }

    [Fact]
    public void 往返_损坏文件不应被覆盖()
    {
        using var scope = new SettingsScope();
        var broken = "{ broken content";
        File.WriteAllText(scope.SettingsPath, broken);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        // 只加载、不更新 → 原文件保持不变（保护用户数据）
        File.ReadAllText(scope.SettingsPath).ShouldBe(broken);
    }

    [Fact]
    public void 往返_应用整体置顶应落盘并在重启后保留()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var updated = service.Current.Clone();
        updated.AppTopmost = true;
        service.Update(updated).Succeeded.ShouldBeTrue();

        File.ReadAllText(scope.SettingsPath).ShouldContain("\"appTopmost\": true");

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.AppTopmost.ShouldBeTrue("应用整体置顶必须落盘（旧实现只在界面生效，重启回 false）");
    }

    [Fact]
    public void 加载_未来版本应使用默认值并禁止写回()
    {
        using var scope = new SettingsScope();
        var original = """
        {
          "schemaVersion": 999,
          "inputQueueCapacity": 256,
          "appTopmost": true
        }
        """;
        File.WriteAllText(scope.SettingsPath, original);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);

        service.Load().ShouldBe(SettingsLoadOutcome.FutureVersion);
        service.Current.InputQueueCapacity.ShouldBe(1024, "未来版本不猜测语义 ⇒ 使用本版本默认值");
        service.Current.AppTopmost.ShouldBeFalse();

        var updated = service.Current.Clone();
        updated.InputQueueCapacity = 128;
        service.Update(updated).Succeeded.ShouldBeFalse("未来版本必须禁止写回，保护原文件");
        service.Save(out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrWhiteSpace();

        File.ReadAllText(scope.SettingsPath).ShouldBe(original, "原文件必须保持不变");
    }

    [Fact]
    public void 服务_应可经公开面读写设置()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var settings = new SettingsService(runtime, scope.SettingsPath);
        settings.Load();

        var services = new RuntimeServices(runtime, settings);

        var view = services.GetSettings();
        view.UiRefreshRateHz.ShouldBe(new ApplicationSettings().UiRefreshRateHz, "缺省只在设置模型里给一次");

        view.UiRefreshRateHz = 120;
        services.UpdateSettings(view).Succeeded.ShouldBeTrue();
        services.GetSettings().UiRefreshRateHz.ShouldBe(120);

        // 返回的是副本：改它不影响生效值
        var copy = services.GetSettings();
        copy.UiRefreshRateHz = 1;
        services.GetSettings().UiRefreshRateHz.ShouldBe(120);
    }

    /// <summary>
    /// **界面语言**：落盘 → 往返 → 未知值回落 `system`（与外观类设置同一容错口径）。
    /// </summary>
    /// <remarks>
    /// 三个出口缺一不可：① 文件里真的写了 `uiLanguage` ② 重新加载读得回来 ③ 未知值不炸、不写怪值。
    /// </remarks>
    [Fact]
    public void 更新_界面语言应落盘并往返且未知值回落跟随系统()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        service.Current.UiLanguage.ShouldBe(TextLanguages.System, "默认跟随系统");

        var updated = service.Current.Clone();
        updated.UiLanguage = TextLanguages.ZhHans;
        service.Update(updated).Succeeded.ShouldBeTrue("写入语言不该失败");

        File.ReadAllText(scope.SettingsPath).ShouldContain("\"uiLanguage\": \"zh-Hans\"");

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.UiLanguage.ShouldBe(TextLanguages.ZhHans, "重新加载应保留语言");

        File.WriteAllText(scope.SettingsPath, """{ "schemaVersion": 4, "uiLanguage": "klingon" }""");

        var invalid = new SettingsService(runtime, scope.SettingsPath);
        invalid.Load().ShouldBe(SettingsLoadOutcome.Loaded);
        invalid.Current.UiLanguage.ShouldBe(TextLanguages.System, "未知语言 ⇒ 跟随系统");
    }

    /// <summary>
    /// **`--emit-default-settings` 的产物必须能被设置服务正常解析**（发布打包用的出厂设置基线就是它）。
    /// </summary>
    /// <remarks>
    /// 走两个出口：① 隐藏开关真的写出了文件（且不初始化 Avalonia）② 写出的文件能作为本版本 v4 设置载入。
    /// 只断言"文件存在"会漏掉"写出来的根本不是设置文件"。
    /// </remarks>
    [Fact]
    public void 默认设置发射_产物应可被设置服务解析()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-emit-defaults-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "settings.json");

            HYWDebugAssistant.App.Program.TryEmitDefaultSettings(
                [HYWDebugAssistant.App.Program.EmitDefaultSettingsSwitch, path],
                out var exitCode).ShouldBeTrue();
            exitCode.ShouldBe(0);
            File.Exists(path).ShouldBeTrue();

            using var runtime = new ModuleRuntime();
            var service = new SettingsService(runtime, path);
            service.Load().ShouldBe(SettingsLoadOutcome.Loaded, "发射出来的必须是本版本可读的 v4 设置文件");

            var defaults = new ApplicationSettings();
            service.Current.SchemaVersion.ShouldBe(ApplicationSettings.CurrentSchemaVersion);
            service.Current.InputQueueCapacity.ShouldBe(defaults.InputQueueCapacity);
            service.Current.Appearance.ThemeSelection.ShouldBe(ThemeIds.System);
            runtime.Issues.Count.ShouldBe(0, "发射默认设置不是异常");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>测试用临时目录（设置文件路径；主题根会落在同级的 `appearance\themes`）。</summary>
    private sealed class SettingsScope : IDisposable
    {
        public SettingsScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "hyw-settings-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            SettingsPath = Path.Combine(Directory, "settings.json");
        }

        public string Directory { get; }

        public string SettingsPath { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }
}
