using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>**拆包不得按字符产生垃圾**：1 KB 文本一次发送的分配必须远小于载荷本身。</summary>
/// <remarks>
/// 判据用 <see cref="GC.GetAllocatedBytesForCurrentThread"/>，阈值给到 4 KB：
/// 测试替身会把载荷抄一份（约 1.5 KB）；旧实现逐字符 `ToString()+GetBytes()` 约 40 KB。
/// </remarks>
[Trait("Category", "Perf")]   // 分配测量：默认不进门禁；重做拆包 / 传输路径时按需跑
public class TextSenderAllocationTests
{
    [Fact]
    public void 一次发送千字符_分配应远小于逐字符实现()
    {
        var module = new TextSenderModule();
        var context = new TestModuleContext();
        module.OnRegistered(context);

        var text = new string('x', 1000);

        for (var i = 0; i < 10; i++)
        {
            _ = module.Send(context, text, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
        {
            _ = module.Send(context, text, out _);
        }

        var perSend = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000.0;
        perSend.ShouldBeLessThan(4096, $"一次千字符发送不应产生大块分配（实测 {perSend:F0} B）");
    }
}
