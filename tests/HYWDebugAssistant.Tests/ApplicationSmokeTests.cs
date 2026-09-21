using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 应用骨架的最小冒烟测试（模块/组合级验证将在分层落地后补充，见 ADR-0021/0025）。
/// </summary>
public class ApplicationSmokeTests
{
    [Fact]
    public void 应用程序集_应可加载并具有预期名称()
    {
        var assembly = typeof(App).Assembly;

        assembly.ShouldNotBeNull();
        assembly.GetName().Name.ShouldBe("HYWDebugAssistant");
    }

    [Fact]
    public void Avalonia核心程序集_版本应为锁定版本12_1_2()
    {
        // 依赖版本锁定回归（中央包管理 Directory.Packages.props）
        var version = typeof(Avalonia.Application).Assembly.GetName().Version;

        version.ShouldNotBeNull();
        version!.Major.ShouldBe(12);
    }

    [Fact]
    public void AppBuilder_应可成功构建配置()
    {
        var builder = Program.BuildAvaloniaApp();

        builder.ShouldNotBeNull();
    }
}
