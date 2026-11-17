using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.DataPlane.Queues;
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
        service.Current.Flow.QueueCapacity.ShouldBe(new ApplicationSettings().Flow.QueueCapacity);
        service.Current.Flow.StopTimeoutSeconds.ShouldBe(
            new ApplicationSettings().Flow.StopTimeoutSeconds,
            "缺省只在设置模型里给一次");
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
        service.Current.Flow.QueueCapacity.ShouldBe(new ApplicationSettings().Flow.QueueCapacity);
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
        updated.Flow = updated.Flow with { QueueCapacity = 256, StopTimeoutSeconds = 2.5 };
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
        text.ShouldContain("\"queueCapacity\": 256");
        text.ShouldContain("\"stopTimeoutSeconds\": 2.5");
        text.Contains("inputQueueCapacity", StringComparison.Ordinal)
            .ShouldBeFalse("旧根键必须删掉（一份事实只有一个来源）");
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
    /// **`flow` 块（数据面）一条走完三个出口**：① 十二项即时映射到承载参数 ② 落盘后重新载入逐项一致
    /// ③ 每个值域的边界各自被拒，且拒绝时当前值不变。
    /// </summary>
    /// <remarks>
    /// 三项分开写会重复"设置 → 承载 / 设置 → 磁盘"这两条同一事实；合并后每个字段在一处被钉住。
    /// </remarks>
    [Fact]
    public void 更新_数据面应即时映射并落盘往返且越界被拒()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        service.Current.Flow.ShouldBe(new FlowSettings(), "缺省只在设置模型里给一次");

        var updated = service.Current.Clone();
        updated.Flow = new FlowSettings
        {
            QueueCapacity = 128,
            DropPolicy = "DropNewest",
            StopTimeoutSeconds = 2,
            HintDepth = 6,
            HintHoldMs = 750,
            MaxBacklogAgeMs = 3000,
            ExternalWriteTimeoutMs = 1500,
            SendBacklogBytes = 393216,
            LivenessIntervalMs = 300,
            AdapterBufferBytes = 131072,
            AccessCloseTimeoutMs = 800,
            DeviceLostMisses = 3,
            StatusBacklogEnterDepth = 4,
            StatusBacklogExitDepth = 1,
            StatusHoldMs = 180,
            StatusDropHoldMs = 420,
        };

        service.Update(updated).Succeeded.ShouldBeTrue();

        // ① 即时生效：十二项各落到承载参数的正确位置（单位换算也要对）
        var options = runtime.Carriers.Options;
        options.QueueCapacity.ShouldBe(128);
        options.DropPolicy.ShouldBe(DropPolicy.DropNewest);
        options.StopTimeout.ShouldBe(TimeSpan.FromSeconds(2));
        options.HintDepth.ShouldBe(6);
        options.HintHold.ShouldBe(TimeSpan.FromMilliseconds(750));
        options.MaxBacklogAge.ShouldBe(TimeSpan.FromMilliseconds(3000));
        options.ExternalWriteTimeout.ShouldBe(TimeSpan.FromMilliseconds(1500));
        options.SendBacklogBytes.ShouldBe(393216);
        options.LivenessInterval.ShouldBe(TimeSpan.FromMilliseconds(300));
        options.AdapterBufferBytes.ShouldBe(131072);
        options.AccessCloseTimeout.ShouldBe(TimeSpan.FromMilliseconds(800));
        options.DeviceLostMisses.ShouldBe(3);
        options.StatusBacklogEnterDepth.ShouldBe(4);
        options.StatusBacklogExitDepth.ShouldBe(1);
        options.StatusHold.ShouldBe(TimeSpan.FromMilliseconds(180));
        options.StatusDropHold.ShouldBe(TimeSpan.FromMilliseconds(420));
        options.WatchdogInterval.ShouldBe(new CarrierOptions().WatchdogInterval, "设置里没暴露的项仍用默认值");

        // 外部接入宿主拿到同一份参数（它只用容量 / 策略 / 停止超时 / 饱和判据）
        runtime.ExternalAccessHost.Options.ShouldBe(options);

        // ② 落盘往返：重新载入后十二项逐项一致
        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Flow.ShouldBe(updated.Flow, "十二项都要落盘并读得回来");

        // ③ 值域边界：每项越界都被拒，且拒绝时当前值不变
        var baseline = reloaded.Current.Flow;
        var defaults = new FlowSettings();

        (string Name, FlowSettings Flow)[] cases =
        [
            ("队列容量", defaults with { QueueCapacity = 0 }),
            ("满时策略", defaults with { DropPolicy = "DropRandom" }),
            ("饱和判定深度", defaults with { HintDepth = 0 }),
            ("饱和判定持续", defaults with { HintHoldMs = 10 }),
            ("最大滞留时间", defaults with { MaxBacklogAgeMs = -1 }),
            ("外部写超时", defaults with { ExternalWriteTimeoutMs = 10 }),
            ("发送积压", defaults with { SendBacklogBytes = 1024 }),
            ("探活节拍", defaults with { LivenessIntervalMs = 10 }),
            ("适配件暂存", defaults with { AdapterBufferBytes = 1024 }),
            ("关闭等待", defaults with { AccessCloseTimeoutMs = 10 }),
            ("丢失判定", defaults with { DeviceLostMisses = 0 }),
            ("积压显示进入", defaults with { StatusBacklogEnterDepth = 0 }),
            ("积压显示退出", defaults with { StatusBacklogExitDepth = defaults.StatusBacklogEnterDepth }),
            ("状态滞回", defaults with { StatusHoldMs = -1 }),
            ("丢弃显示保持", defaults with { StatusDropHoldMs = -1 }),
        ];

        foreach (var (name, flow) in cases)
        {
            var invalid = reloaded.Current.Clone();
            invalid.Flow = flow;

            var result = reloaded.Update(invalid);

            result.Succeeded.ShouldBeFalse($"{name} 越界必须被拒");
            result.Failures.ShouldHaveSingleItem();
            reloaded.Current.Flow.ShouldBe(baseline, $"{name} 被拒绝后当前值不得变化");
        }
    }

    [Fact]
    public void 往返_文本显示上限应落盘并立即下发()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var updated = service.Current.Clone();
        updated.Display = new DisplaySettings
        {
            TextLineCapacity = 1234,
            TextCharBudget = 2 * 1024 * 1024,
        };

        service.Update(updated).Succeeded.ShouldBeTrue();

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Display.ShouldBe(updated.Display);
    }

    /// <summary>外观块 v5 的形状：`theme / systemLightScheme / systemDarkScheme / seeded[] / fonts{latin,cjk}`，不再有内联主题。</summary>
    [Fact]
    public void 往返_外观块应写成当前形状()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var updated = service.Current.Clone();
        updated.Appearance.ThemeSelection = "custom-abc";
        updated.Appearance.SystemLightScheme = ThemeIds.Light;
        updated.Appearance.SystemDarkScheme = ThemeIds.Dark;
        updated.Appearance.UiFonts.Latin = "Georgia";
        updated.Appearance.DataFonts.Latin = "Cascadia Code";
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

        var block = root["appearance"]!.AsObject();
        block["theme"]!.GetValue<string>().ShouldBe("custom-abc");
        block["systemLightScheme"]!.GetValue<string>().ShouldBe(ThemeIds.Light);
        block["systemDarkScheme"]!.GetValue<string>().ShouldBe(ThemeIds.Dark);
        block["fonts"]!["ui"]!["latin"]!.GetValue<string>().ShouldBe("Georgia");
        block["fonts"]!["ui"]!["cjk"]!.GetValue<string>().ShouldBe(service.Current.Appearance.UiFonts.Cjk);
        block["fonts"]!["data"]!["latin"]!.GetValue<string>().ShouldBe("Cascadia Code");
        block["fonts"]!["data"]!["cjk"]!.GetValue<string>().ShouldBe(service.Current.Appearance.DataFonts.Cjk);
        block["seeded"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["appearance/themes/sakura"]);

        block.ContainsKey("schemes").ShouldBeFalse("外观块不再内联主题方案");
        block.ContainsKey("paletteOverrides").ShouldBeFalse("外观块不再内联色板覆盖");
        block.ContainsKey("seededThemes").ShouldBeFalse("seededThemes 已折进 seeded");

        // 重启读回
        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Appearance.ThemeSelection.ShouldBe("custom-abc", "主题选择是标量，原样保留");
        reloaded.Current.Appearance.Seeded.ShouldContain("appearance/themes/sakura");
        reloaded.Current.Appearance.UiFonts.Latin.ShouldBe("Georgia");
    }

    [Fact]
    public void 加载_已删除的主题在设置里的选择应回落内置()
    {
        using var scope = new SettingsScope();
        File.WriteAllText(scope.SettingsPath, """
        {
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
        result.Failures.ShouldHaveSingleItem();
        service.Current.SnapDistance.ShouldBe(baseline, "被拒绝的更新不得改变当前值");
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
        view.Appearance.UiFonts.Latin = "Georgia";
        view.Appearance.DataFonts.Latin = "Cascadia Code";

        services.UpdateSettings(view).Succeeded.ShouldBeTrue();

        // ① 写出口（生效值）
        services.GetSettings().Appearance.UiFonts.Latin.ShouldBe("Georgia", "写完再读必须还是用户选的那个");
        services.GetSettings().Appearance.DataFonts.Latin.ShouldBe("Cascadia Code");

        // ② 文件出口（外观块里的字体轴：`appearance.fonts.{ui,data}`）
        var text = File.ReadAllText(scope.SettingsPath);
        text.ShouldContain("\"latin\": \"Georgia\"");
        text.ShouldContain("\"latin\": \"Cascadia Code\"");

        // ③ 重启读回出口
        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Appearance.UiFonts.Latin.ShouldBe("Georgia", "重新加载应保留用户设置");
        reloaded.Current.Appearance.DataFonts.Latin.ShouldBe("Cascadia Code");

        // ④ 反向断言：改一个**别的**设置（界面上最常见的动作）不得把字体冲回默认值
        var again = services.GetSettings();
        again.SnapDistance = 6;
        services.UpdateSettings(again).Succeeded.ShouldBeTrue();
        services.GetSettings().Appearance.UiFonts.Latin.ShouldBe("Georgia", "别的设置落盘时，字体不得被自己的副本冲回默认");
        services.GetSettings().Appearance.DataFonts.Latin.ShouldBe("Cascadia Code");
    }

    [Fact]
    public void 往返_应保留未识别字段()
    {
        using var scope = new SettingsScope();
        // 预置一个含**未知字段**的文件（模拟更高版本写下的设置）
        File.WriteAllText(scope.SettingsPath, """
        {
          "flow": { "queueCapacity": 512 },
          "futureOption": "由更高版本写入",
          "nested": { "a": 1, "b": [1, 2, 3] }
        }
        """);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load().ShouldBe(SettingsLoadOutcome.Loaded);
        service.Current.Flow.QueueCapacity.ShouldBe(512);

        // 更新一个已知字段
        var updated = service.Current.Clone();
        updated.UiRefreshRateHz = 30;
        service.Update(updated).Succeeded.ShouldBeTrue();

        // 未知字段原样保留（AC-4 同策略）
        var root = JsonNode.Parse(File.ReadAllText(scope.SettingsPath))!.AsObject();
        const string futureOption = "由更高版本写入";
        root["futureOption"]!.GetValue<string>().ShouldBe(futureOption);
        root["nested"]!["b"]!.AsArray().Count.ShouldBe(3);
        root["uiRefreshRateHz"]!.GetValue<int>().ShouldBe(30);
        root["flow"]!["queueCapacity"]!.GetValue<int>().ShouldBe(512, "flow 块原值保留");
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

        File.WriteAllText(scope.SettingsPath, """{ "uiLanguage": "klingon" }""");

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
            service.Load().ShouldBe(SettingsLoadOutcome.Loaded, "发射出来的必须是本版本可读的设置文件");

            var defaults = new ApplicationSettings();
            service.Current.Flow.QueueCapacity.ShouldBe(defaults.Flow.QueueCapacity);
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

    [Fact]
    public void 性能监视开关_默认开启且可落盘往返()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        service.Current.ShowPerformanceMonitor.ShouldBeTrue("新增设置项默认开启");

        var updated = service.Current.Clone();
        updated.ShowPerformanceMonitor = false;
        service.Update(updated).Succeeded.ShouldBeTrue();

        var root = JsonNode.Parse(File.ReadAllText(scope.SettingsPath))!.AsObject();
        root["showPerformanceMonitor"]!.GetValue<bool>().ShouldBeFalse();

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.ShowPerformanceMonitor.ShouldBeFalse();
    }

    [Fact]
    public void 不可显示字符标记开关_默认开启且可落盘往返()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        service.Current.Display.ShowControlCharacterMarkers.ShouldBeTrue("新增设置项默认开启");

        var updated = service.Current.Clone();
        updated.Display = updated.Display with { ShowControlCharacterMarkers = false };
        service.Update(updated).Succeeded.ShouldBeTrue();

        var root = JsonNode.Parse(File.ReadAllText(scope.SettingsPath))!.AsObject();
        root["display"]!["controlCharacterMarkers"]!.GetValue<bool>().ShouldBeFalse();

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.Display.ShowControlCharacterMarkers.ShouldBeFalse();
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
