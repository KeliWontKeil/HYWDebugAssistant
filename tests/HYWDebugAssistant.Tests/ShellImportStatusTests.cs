using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>拖入无法识别的内容：错误应落在页面操作状态，而不是应用底部状态栏。</summary>
public class ShellImportStatusTests
{
    [Fact]
    public void 拖入未知内容_错误应进入页面状态而非底部提示()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var shell = new ShellViewModel(services, services);

        var path = Path.Combine(Path.GetTempPath(), "hyw-drop-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "not a package");

        try
        {
            shell.ImportPath(path);

            shell.Catalog.StatusMessage.ShouldNotBeNullOrWhiteSpace();
            shell.Catalog.StatusIsOk.ShouldBeFalse();
            shell.StatusNotice.ShouldBeNull("导入错误不应占用应用底部临时提示");
            shell.ActivePage.ShouldBe(ShellPages.Catalog);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
