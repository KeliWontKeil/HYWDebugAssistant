using HYWDebugAssistant.App;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 应用骨架的最小冒烟测试。
/// 模块级/组合级验证见 <c>Tests.Reference</c> 的 T1 判据测试（AC-1~6，docs/architecture/06 §6.4）。
/// </summary>
public class ApplicationSmokeTests
{
    [Fact]
    public void 组合根程序集_应可加载并具有预期名称()
    {
        var assembly = typeof(Program).Assembly;

        assembly.ShouldNotBeNull();
        assembly.GetName().Name.ShouldBe("HYWDebugAssistant.App");
    }

    [Fact]
    public void Avalonia核心程序集_主版本应为锁定版本12()
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
