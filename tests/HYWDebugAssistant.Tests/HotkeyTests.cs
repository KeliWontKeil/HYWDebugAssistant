using Avalonia.Input;
using HYWDebugAssistant.Ui.Hotkeys;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// M1-17：**应用内快捷键**——绑定解析/格式化、冲突、动作分发、设置持久化。
/// </summary>
public class HotkeyTests
{
    [Theory]
    [InlineData("M", Key.M, false, false, false)]
    [InlineData("Ctrl+L", Key.L, true, false, false)]
    [InlineData("ctrl+shift+t", Key.T, true, true, false)]
    [InlineData("Alt+F1", Key.F1, false, false, true)]
    [InlineData("Ctrl+Alt+Shift+Q", Key.Q, true, true, true)]
    public void 绑定_应能解析修饰键与主键(string text, Key key, bool control, bool shift, bool alt)
    {
        var binding = HotkeyBinding.Parse(text);

        binding.IsEmpty.ShouldBeFalse();
        binding.Key.ShouldBe(key);
        binding.Control.ShouldBe(control);
        binding.Shift.ShouldBe(shift);
        binding.Alt.ShouldBe(alt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Nope")]
    public void 绑定_非法或空文本应为未绑定(string? text) => HotkeyBinding.Parse(text).IsEmpty.ShouldBeTrue();

    [Fact]
    public void 绑定_格式化与解析应可往返()
    {
        foreach (var text in new[] { "M", "Ctrl+L", "Ctrl+Shift+T", "Alt+F1", "Ctrl+Alt+Q" })
        {
            HotkeyBinding.Parse(text).ToString().ShouldBe(text);
        }
    }

    [Fact]
    public void 绑定_只按修饰键不算绑定()
    {
        HotkeyBinding.From(Key.LeftCtrl, KeyModifiers.Control).IsEmpty.ShouldBeTrue();
        HotkeyBinding.From(Key.LeftShift, KeyModifiers.Shift).IsEmpty.ShouldBeTrue();
        HotkeyBinding.From(Key.LeftAlt, KeyModifiers.Alt).IsEmpty.ShouldBeTrue();

        HotkeyBinding.From(Key.M, KeyModifiers.None).ToString().ShouldBe("M");
    }

    [Fact]
    public void 服务_应按绑定表分发动作()
    {
        var actions = new Recorder();
        var service = new HotkeyService(actions);
        service.Rebuild(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HotkeyCatalog.OpenIssues] = "Ctrl+J",
            [HotkeyCatalog.ToggleMarkers] = string.Empty,   // 显式不绑定
        }).ShouldBeEmpty();

        var bindings = service.EffectiveBindings;
        bindings[HotkeyCatalog.OpenIssues].ShouldBe("Ctrl+J");
        bindings.ContainsKey(HotkeyCatalog.ToggleMarkers).ShouldBeFalse("空绑定 = 不绑定");
        bindings[HotkeyCatalog.OpenCatalog].ShouldBe("M", "未配置的动作回落到内置默认");

        actions.Execute(HotkeyCatalog.OpenIssues);   // 动作面契约（按键分发在窗口上，模型层只验证动作面与绑定表）
        actions.Executed.ShouldContain(HotkeyCatalog.OpenIssues);
    }

    [Fact]
    public void 服务_同键重复应报告冲突且先到者胜()
    {
        var service = new HotkeyService(new Recorder());

        var conflicts = service.Rebuild(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HotkeyCatalog.OpenCatalog] = "Ctrl+K",
            [HotkeyCatalog.OpenIssues] = "Ctrl+K",   // 与上一个冲突
            [HotkeyCatalog.OpenSettings] = "Ctrl+K", // 同样冲突
        });

        conflicts.Count.ShouldBe(2);
        conflicts.ShouldAllBe(c => c.Contains("Ctrl+K"));
        service.EffectiveBindings[HotkeyCatalog.OpenCatalog].ShouldBe("Ctrl+K");
        service.EffectiveBindings.Values.Count(v => v == "Ctrl+K").ShouldBe(1);
    }

    [Fact]
    public void 服务_动作目录的默认绑定应各自唯一()
    {
        var defaults = HotkeyCatalog.Actions.Select(a => a.DefaultBinding).ToList();
        defaults.Distinct(StringComparer.Ordinal).Count().ShouldBe(defaults.Count, "内置默认绑定不得互相冲突");
        HotkeyCatalog.Actions.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(defaults.Count);
    }

    [Fact]
    public void 服务_应有固定与解锁端口锚点的动作且默认键可用()
    {
        // 【G5 · C5-7】动作目录必须**真的**有「固定 / 解锁端口锚点」这一项，
        // 而且**与「全部锁定/解锁（模块窗口）」是两个不同动作**（术语撞词：锚点侧一律用「固定」）。
        var action = HotkeyCatalog.Find(HotkeyCatalog.ToggleAnchorFixed);
        action.ShouldNotBeNull("G5 要求的全局「固定 / 解锁端口锚点」动作必须在目录里");
        action.Title.ShouldContain("固定");
        action.Title.Contains("锁定", StringComparison.Ordinal).ShouldBeFalse("锚点侧一律用「固定」，不用「锁定」");

        HotkeyCatalog.ToggleAnchorFixed.ShouldNotBe(HotkeyCatalog.ToggleLockAll);
        HotkeyBinding.Parse(action.DefaultBinding).IsEmpty.ShouldBeFalse("默认必须有键（否则用户不知道按什么）");

        HotkeyCatalog.Defaults[HotkeyCatalog.ToggleAnchorFixed].ShouldBe(action.DefaultBinding);
    }

    [Fact]
    public void 设置_快捷键绑定应可保存并读回()
    {
        using var harness = new SettingsHarness();

        var settings = harness.Services.GetSettings();
        settings.Hotkeys[HotkeyCatalog.OpenIssues] = "Ctrl+J";
        settings.Hotkeys[HotkeyCatalog.ToggleSnap] = string.Empty;
        settings.AppTopmost = true;

        harness.Services.UpdateSettings(settings).Succeeded.ShouldBeTrue();

        var reloaded = harness.Services.GetSettings();
        reloaded.Hotkeys[HotkeyCatalog.OpenIssues].ShouldBe("Ctrl+J");
        reloaded.Hotkeys[HotkeyCatalog.ToggleSnap].ShouldBeEmpty();
        reloaded.AppTopmost.ShouldBeTrue();
    }

    [Fact]
    public void 设置_来源根列表应可保存并读回()
    {
        using var harness = new SettingsHarness();

        var settings = harness.Services.GetSettings();
        settings.ModuleSourceDirectories = [@"C:\modules-a", @"D:\modules-b"];

        harness.Services.UpdateSettings(settings).Succeeded.ShouldBeTrue();

        harness.Services.GetSettings().ModuleSourceDirectories.ShouldBe([@"C:\modules-a", @"D:\modules-b"]);
    }

    private sealed class Recorder : IHotkeyActions
    {
        public List<string> Executed { get; } = [];

        public void Execute(string actionId) => Executed.Add(actionId);
    }

    /// <summary>设置往返测试用的最小现场（真实设置服务 + 临时目录）。</summary>
    private sealed class SettingsHarness : IDisposable
    {
        private readonly string _directory;

        public SettingsHarness()
        {
            _directory = Path.Combine(Path.GetTempPath(), "hyw-hotkey-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);

            Runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();

            var settings = new HYWDebugAssistant.Core.Settings.SettingsService(
                Runtime,
                Path.Combine(_directory, "settings.json"));
            _ = settings.Load();

            Services = new HYWDebugAssistant.Core.Services.RuntimeServices(Runtime, settings);
        }

        public HYWDebugAssistant.Core.Runtime.ModuleRuntime Runtime { get; }

        public HYWDebugAssistant.Core.Services.RuntimeServices Services { get; }

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
