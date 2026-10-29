using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 遮罩 / 表面透明度是**主题文件夹的字段**：改内置主题时应自动复制成自定义主题并写进主题文件夹。
/// </summary>
public class SettingsPanelThemeMaterialTests
{
    [Fact]
    public void 修改遮罩和表面透明度_应写入当前主题文件夹()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-settings-panel-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        try
        {
            using var runtime = new ModuleRuntime();
            var service = new SettingsService(runtime, Path.Combine(directory, "settings.json"));
            service.Load();

            var services = new RuntimeServices(runtime, service);
            var panel = new SettingsPanelViewModel(services, services);
            panel.Load();

            panel.WallpaperVeilOpacity = 0.2;
            panel.SurfaceOpacity = 0.0;
            panel.ApplyCommand.Execute(null);

            panel.StatusIsOk.ShouldBeTrue(panel.StatusMessage);

            // 改内置主题 ⇒ 自动复制成自定义主题，并把"跟随系统 · 浅色"指向它
            var appearance = services.GetSettings().Appearance;
            var schemeId = appearance.SystemLightScheme;
            ThemeIds.IsSystem(schemeId).ShouldBeFalse("改内置主题应复制成自定义主题");

            var theme = services.GetThemeFiles().FirstOrDefault(file => file.Id == schemeId).ShouldNotBeNull();
            theme.WallpaperVeilOpacity.ShouldBe(0.2);
            theme.SurfaceOpacity.ShouldBe(0.0);

            // 再次打开必须读回当前主题的值，而不是全局默认值
            panel.Load();
            panel.WallpaperVeilOpacity.ShouldBe(0.2);
            panel.SurfaceOpacity.ShouldBe(0.0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
