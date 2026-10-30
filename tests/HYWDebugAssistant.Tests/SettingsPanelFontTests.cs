using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>设置页的四个字体字段必须写进外观草稿并随「应用」落盘，不能回退。</summary>
public class SettingsPanelFontTests
{
    [Fact]
    public void 修改中西文字体后应用_应保存新字体而不是回退默认()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-settings-font-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        try
        {
            using var runtime = new ModuleRuntime();
            var service = new SettingsService(runtime, Path.Combine(directory, "settings.json"));
            service.Load();

            var services = new RuntimeServices(runtime, service);
            var panel = new SettingsPanelViewModel(services, services);
            panel.Load();

            panel.UiFontLatin = "Georgia";
            panel.UiFontCjk = "SimSun";
            panel.DataFontLatin = "Cascadia Code";
            panel.DataFontCjk = "SimSun";
            panel.ApplyCommand.Execute(null);

            panel.StatusIsOk.ShouldBeTrue(panel.StatusMessage);
            services.GetSettings().Appearance.UiFonts.Latin.ShouldBe("Georgia");
            services.GetSettings().Appearance.UiFonts.Cjk.ShouldBe("SimSun");
            services.GetSettings().Appearance.DataFonts.Latin.ShouldBe("Cascadia Code");
            services.GetSettings().Appearance.DataFonts.Cjk.ShouldBe("SimSun");

            // 重新打开设置页时必须仍显示刚应用的值
            panel.Load();
            panel.UiFontLatin.ShouldBe("Georgia");
            panel.UiFontCjk.ShouldBe("SimSun");
            panel.DataFontLatin.ShouldBe("Cascadia Code");
            panel.DataFontCjk.ShouldBe("SimSun");
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
