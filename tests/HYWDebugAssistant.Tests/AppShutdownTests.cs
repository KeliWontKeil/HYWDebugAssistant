using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 应用收尾的**一次性**契约：多个关闭路径先后到达也只执行一遍。
/// </summary>
public class AppShutdownTests
{
    [Fact]
    public void Run_重复调用_只执行一次()
    {
        var count = 0;
        var shutdown = new AppShutdown(() => count++);

        shutdown.IsDone.ShouldBeFalse();

        shutdown.Run();
        shutdown.Run();

        count.ShouldBe(1);
        shutdown.IsDone.ShouldBeTrue();
    }
}
