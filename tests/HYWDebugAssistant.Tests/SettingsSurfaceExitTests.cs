using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.Ui.Theming;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>表面不透明度与关闭行为的设置契约。</summary>
public class SettingsSurfaceExitTests
{
    [Fact]
    public void 表面标记_只标记背景表面token()
    {
        PaletteManifest.Find("BgChrome")!.Surface.ShouldBeTrue();
        PaletteManifest.Find("SurfaceButtonFaceBrush")!.Surface.ShouldBeTrue();
        PaletteManifest.Find("BgModule")!.Surface.ShouldBeFalse();
        PaletteManifest.Find("TextPrimary")!.Surface.ShouldBeFalse();
    }

    [Fact]
    public void 新设置字段_应落盘并往返()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        _ = Directory.CreateDirectory(directory);

        try
        {
            using var runtime = new ModuleRuntime();
            var service = new SettingsService(runtime, path);
            service.Load();

            // 主题的遮罩 / 表面透明度住在主题文件夹里
            var saved = service.Themes.Save(new ThemeSaveRequest(
                "test-theme",
                "测试主题",
                ThemeBaseKind.Light,
                new Dictionary<string, string>(StringComparer.Ordinal),
                null,
                ClearWallpaper: false,
                WallpaperVeilOpacity: 0.55,
                SurfaceOpacity: 0.0));

            saved.Succeeded.ShouldBeTrue(string.Join("；", saved.Failures));

            var updated = service.Current.Clone();
            updated.WorkspaceExitBehavior = "Save";
            var result = service.UpdateStrict(updated);
            result.Succeeded.ShouldBeTrue(string.Join("；", result.Failures));

            using var secondRuntime = new ModuleRuntime();
            var reloaded = new SettingsService(secondRuntime, path);
            reloaded.Load();

            reloaded.Current.WorkspaceExitBehavior.ShouldBe("Save");

            var theme = reloaded.Themes.Read("test-theme").ShouldNotBeNull();
            theme.WallpaperVeilOpacity.ShouldBe(0.55);
            theme.SurfaceOpacity.ShouldBe(0.0);
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
