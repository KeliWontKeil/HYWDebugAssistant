using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 设置页的主题草稿：**只写被编辑过的主题**、删除**走主题文件夹**（不是设置文件）、「取消」丢弃草稿。
/// </summary>
/// <remarks>
/// 用真实 <see cref="SettingsService"/>（临时目录）而不是替身：这条链的价值就在"落到磁盘上的形状"，
/// 替身会把它测成"内存里看起来对"。
/// </remarks>
public class SettingsPanelThemeDraftTests
{
    [Fact]
    public void 改颜色后应用_应写进主题文件夹而不是设置文件()
    {
        using var scope = new PanelScope();
        var panel = scope.Panel;

        var row = panel.PaletteGroups
            .SelectMany(group => group.Rows)
            .First(row => string.Equals(row.Key, "AccentPrimary", StringComparison.Ordinal));

        panel.SetPaletteColor(row, "#112233");
        panel.ApplyCommand.Execute(null);

        panel.StatusIsOk.ShouldBeTrue(panel.StatusMessage);

        // ① 主题文件夹里有这条覆盖
        var theme = scope.Services.GetThemeFiles().ShouldHaveSingleItem();
        theme.Palette["AccentPrimary"].ShouldBe("#112233");
        theme.Base.ShouldBe(ThemeBaseKind.Light);

        var manifestPath = Path.Combine(scope.Service.Themes.ThemesRoot, theme.Id, "theme.json");
        File.Exists(manifestPath).ShouldBeTrue();
        File.ReadAllText(manifestPath).ShouldContain("#112233");

        // ② 设置文件里没有主题色板（主题内容不再内联）
        File.ReadAllText(scope.SettingsPath).ShouldNotContain("#112233");
        File.ReadAllText(scope.SettingsPath).ShouldNotContain("paletteOverrides");

        // ③ 跟随系统的浅色模式指向这套主题（改内置主题 = 复制成自定义主题）
        scope.Services.GetSettings().Appearance.SystemLightScheme.ShouldBe(theme.Id);
    }

    [Fact]
    public void 删除主题_取消不删_应用才删文件夹()
    {
        using var scope = new PanelScope();
        var panel = scope.Panel;

        scope.Service.Themes.Save(new ThemeSaveRequest(
            "temp-theme",
            "临时主题",
            ThemeBaseKind.Light,
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            ClearWallpaper: false,
            0.55,
            0.70)).Succeeded.ShouldBeTrue();

        panel.Load();
        panel.SelectTheme("temp-theme");
        panel.DeleteSchemeCommand.Execute(null);
        panel.CancelCommand.Execute(null);   // 取消 = 丢弃草稿

        scope.Service.Themes.Exists("temp-theme").ShouldBeTrue("「取消」不得删除主题文件夹");

        panel.SelectTheme("temp-theme");
        panel.DeleteSchemeCommand.Execute(null);
        panel.ApplyCommand.Execute(null);

        panel.StatusIsOk.ShouldBeTrue(panel.StatusMessage);
        scope.Service.Themes.Exists("temp-theme").ShouldBeFalse("「应用」后主题文件夹必须被删除");
        scope.Services.GetThemeFiles().ShouldBeEmpty();
        scope.Services.GetSettings().Appearance.ThemeSelection.ShouldBe(ThemeIds.System);
    }

    /// <summary>测试用：临时设置文件 + 真实服务 + 已 Load 的面板。</summary>
    private sealed class PanelScope : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "hyw-settings-theme-" + Guid.NewGuid().ToString("N"));

        public PanelScope()
        {
            _ = Directory.CreateDirectory(_directory);

            Runtime = new ModuleRuntime();
            SettingsPath = Path.Combine(_directory, "settings.json");
            Service = new SettingsService(Runtime, SettingsPath);
            Service.Load();
            Services = new RuntimeServices(Runtime, Service);

            Panel = new SettingsPanelViewModel(Services, Services);
            Panel.Load();
        }

        public ModuleRuntime Runtime { get; }

        public string SettingsPath { get; }

        public SettingsService Service { get; }

        public RuntimeServices Services { get; }

        public SettingsPanelViewModel Panel { get; }

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
