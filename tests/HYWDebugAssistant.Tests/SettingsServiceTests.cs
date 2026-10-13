using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
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
    /// **外观块 = 覆盖层**（方向的例子）：默认不写（跟随主题内容）、写了就落盘、清掉就回跟随；
    /// 同时验证**旧外观扁平键读时折叠**进来、写侧只留新形状。
    /// </summary>
    /// <remarks>
    /// 一条用例走三个出口：① 折叠（旧形状 → 外观块）② 覆盖落盘（文件里只有新形状）③ 重启读回。
    /// 只断言其中一个出口会漏 —— 例如"折叠了但没写出去"或"写出去了但读不回"。
    /// </remarks>
    [Fact]
    public void 更新_方向语义色应落盘并往返_且旧外观扁平键折叠进外观块()
    {
        using var scope = new SettingsScope();

        // 预置**旧形状**：主题 / 主题色 / 背景档位 / 字体 / 方向色都在顶层
        File.WriteAllText(scope.SettingsPath, """
        {
          "schemaVersion": 1,
          "theme": "Dark",
          "accentColor": "#EA580C",
          "backgroundMode": "LightGray",
          "uiFontFamily": "Georgia",
          "dataFontFamily": "Cascadia Code",
          "directionSendColor": "#123456",
          "directionReceiveColor": ""
        }
        """);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        // ① 折叠：旧字段进了外观块（按主题分层）
        var appearance = service.Current.Appearance;
        appearance.ThemeSelection.ShouldBe(ThemeIds.Dark, "旧值 `Dark` 规范成 `dark`");
        appearance.Override(ThemeIds.Light, "AccentPrimary").ShouldBe("#EA580C", "旧主题色折叠成两套的覆盖");
        appearance.Override(ThemeIds.Dark, "AccentPrimary").ShouldBe("#EA580C");
        appearance.Override(ThemeIds.Light, "BgWorkspace").ShouldBe("#E8EAED", "旧背景档位折叠成三支底色");
        appearance.Override(ThemeIds.Light, "BgModule").ShouldBe("#F7F8FA");
        appearance.Override(ThemeIds.Light, "BgChrome").ShouldBe("#F1F2F5");
        appearance.Override(ThemeIds.Light, "DirectionSend").ShouldBe("#123456", "旧方向色折叠进覆盖层");
        appearance.Override(ThemeIds.Light, "DirectionReceive").ShouldBeNull("旧值空 = 跟随 ⇒ 不写");
        appearance.UiFontFamily.ShouldBe("Georgia", "旧顶层字体键折叠进外观块");
        appearance.DataFontFamily.ShouldBe("Cascadia Code");

        // ② 覆盖落盘：清掉方向色、留一支没动过的 ⇒ 文件里只有被改过的那几支
        var updated = service.Current.Clone();
        updated.Appearance.SetOverride(ThemeIds.Light, "DirectionSend", null);
        updated.Appearance.SetOverride(ThemeIds.Dark, "DirectionReceive", "#0891B2");
        service.Update(updated).Succeeded.ShouldBeTrue();

        var root = JsonNode.Parse(File.ReadAllText(scope.SettingsPath))!.AsObject();

        // **按结构判**而不是按文本判：`"theme"` 在新形状里也有（`appearance.theme`）⇒ 文本包含会误报
        foreach (var legacy in new[]
        {
            "theme", "paletteOverrides", "uiFontFamily", "dataFontFamily",
            "accentColor", "backgroundMode", "backgroundColor",
            "directionSendColor", "directionReceiveColor",
        })
        {
            root.ContainsKey(legacy).ShouldBeFalse($"旧键 {legacy} 不该再出现在顶层");
        }

        var block = root["appearance"]!.AsObject();
        block["theme"]!.GetValue<string>().ShouldBe(ThemeIds.Dark);
        block["fonts"]!["ui"]!.GetValue<string>().ShouldBe("Georgia");
        block["paletteOverrides"]!["light"]!["AccentPrimary"]!.GetValue<string>().ShouldBe("#EA580C");
        block["paletteOverrides"]!["dark"]!["DirectionReceive"]!.GetValue<string>().ShouldBe("#0891B2");

        // ③ 重启读回：改过的在、清掉的没了
        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Appearance.Override(ThemeIds.Dark, "DirectionReceive").ShouldBe("#0891B2", "重新加载应保留用户设置");
        reloaded.Current.Appearance.Override(ThemeIds.Light, "DirectionSend").ShouldBeNull("清掉的那一支应回到跟随");
        reloaded.Current.Appearance.Override(ThemeIds.Light, "AccentPrimary").ShouldBe("#EA580C", "折叠来的覆盖同样落盘");
    }

    /// <summary>清掉覆盖 = 回到跟随（**不做任何合法性/对比度校验**：完全尊重用户选择）。</summary>
    [Fact]
    public void 更新_方向语义色清空应回到跟随主题()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var custom = service.Current.Clone();
        custom.Appearance.SetOverride(ThemeIds.Light, "DirectionSend", "#123456");
        service.Update(custom).Succeeded.ShouldBeTrue();

        var cleared = service.Current.Clone();
        cleared.Appearance.SetOverride(ThemeIds.Light, "DirectionSend", null);
        service.Update(cleared).Succeeded.ShouldBeTrue();

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Appearance.Override(ThemeIds.Light, "DirectionSend").ShouldBeNull();

        // 清空后文件里连"这套主题的桶"都不该留下（空桶 = 死数据）
        File.ReadAllText(scope.SettingsPath).ShouldNotContain("#123456");
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
        // **外观块**（主题选择 + 按主题分层的覆盖 + 字体）：三层各给一个哨兵值
        _ when type == typeof(HYWDebugAssistant.Core.Api.AppearanceSettings) => new HYWDebugAssistant.Core.Api.AppearanceSettings
        {
            ThemeSelection = $"sentinel-{name}",
            UiFontFamily = $"sentinel-ui-{name}",
            DataFontFamily = $"sentinel-data-{name}",
            PaletteOverrides =
            {
                ["sentinel-theme"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["TextPrimary"] = "#010203" },
            },
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
                // 外观块自带两层字典 ⇒ 逐项比，而不是比引用（比引用会让"浅拷"这条缺陷漏网）
                var copiedAppearance = actual.ShouldBeAssignableTo<HYWDebugAssistant.Core.Api.AppearanceSettings>()!;
                copiedAppearance.ThemeSelection.ShouldBe(appearance.ThemeSelection, $"{name}：Clone 漏了外观块（主题选择）");
                copiedAppearance.UiFontFamily.ShouldBe(appearance.UiFontFamily, $"{name}：Clone 漏了外观块（界面字体）");
                copiedAppearance.DataFontFamily.ShouldBe(appearance.DataFontFamily, $"{name}：Clone 漏了外观块（数据字体）");

                foreach (var (themeId, map) in appearance.PaletteOverrides)
                {
                    copiedAppearance.OverridesOf(themeId).Count.ShouldBe(map.Count, $"{name}：Clone 漏了外观块的色板覆盖");
                    foreach (var (token, hex) in map)
                    {
                        copiedAppearance.Override(themeId, token).ShouldBe(hex, $"{name}：Clone 漏了外观块的色板覆盖");
                    }
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

    /// <summary>测试用临时目录（设置文件路径）。</summary>
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
