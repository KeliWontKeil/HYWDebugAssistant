using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.Hotkeys;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 设置弹窗的三键草稿语义：设置页改动在应用/确认前不进入运行时；交互页的直接开关仍即时落盘。
/// </summary>
public class SettingsApplyBehaviorTests
{
    [Fact]
    public void 设置页开关在应用前不改变运行时_应用后才生效()
    {
        using var scope = new Scope();
        var shell = scope.Shell;

        shell.Settings.ShowInfoOverlay = true;
        shell.Settings.AppTopmost = true;

        shell.ShowInfoOverlay.ShouldBeFalse("设置页是草稿，不能像交互页那样即时改运行时");
        shell.AppTopmost.ShouldBeFalse();

        shell.Settings.ApplyCommand.Execute(null);

        shell.Settings.StatusIsOk.ShouldBeTrue(shell.Settings.StatusMessage);
        shell.ShowInfoOverlay.ShouldBeTrue();
        shell.AppTopmost.ShouldBeTrue();
        scope.Services.GetSettings().ShowInfoOverlay.ShouldBeTrue();
        scope.Services.GetSettings().AppTopmost.ShouldBeTrue();
    }

    [Fact]
    public void 设置页取消不改变运行时也不写入设置()
    {
        using var scope = new Scope();
        var shell = scope.Shell;

        shell.Settings.ShowInfoOverlay = true;
        shell.Settings.AppTopmost = true;
        shell.Settings.CancelCommand.Execute(null);

        shell.ShowInfoOverlay.ShouldBeFalse();
        shell.AppTopmost.ShouldBeFalse();
        scope.Services.GetSettings().ShowInfoOverlay.ShouldBeFalse();
        scope.Services.GetSettings().AppTopmost.ShouldBeFalse();
    }

    [Fact]
    public void 交互页直接开关立即生效并落盘()
    {
        using var scope = new Scope();
        var shell = scope.Shell;

        shell.ShowInfoOverlay = true;
        shell.AppTopmost = true;

        shell.ShowInfoOverlay.ShouldBeTrue();
        shell.AppTopmost.ShouldBeTrue();
        scope.Services.GetSettings().ShowInfoOverlay.ShouldBeTrue();
        scope.Services.GetSettings().AppTopmost.ShouldBeTrue();
    }

    [Fact]
    public void 快捷键只进草稿_应用后才写入设置()
    {
        using var scope = new Scope();
        var shell = scope.Shell;

        shell.Settings.SetHotkey(HotkeyCatalog.OpenIssues, "Ctrl+J");

        var draft = scope.Services.GetSettings().Hotkeys;
        if (draft.TryGetValue(HotkeyCatalog.OpenIssues, out var original))
        {
            original.ShouldNotBe("Ctrl+J", "聚焦/按键只改草稿，应用前不得写入设置");
        }

        shell.Settings.ApplyCommand.Execute(null);

        scope.Services.GetSettings().Hotkeys[HotkeyCatalog.OpenIssues].ShouldBe("Ctrl+J");
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "hyw-settings-apply-" + Guid.NewGuid().ToString("N"));

        public Scope()
        {
            _ = Directory.CreateDirectory(_directory);

            Runtime = new ModuleRuntime();
            Service = new SettingsService(Runtime, Path.Combine(_directory, "settings.json"));
            Service.Load();
            Services = new RuntimeServices(Runtime, Service);
            Shell = new ShellViewModel(Services, Services);
            Shell.LoadSettings();
        }

        public ModuleRuntime Runtime { get; }

        public SettingsService Service { get; }

        public RuntimeServices Services { get; }

        public ShellViewModel Shell { get; }

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }
}
