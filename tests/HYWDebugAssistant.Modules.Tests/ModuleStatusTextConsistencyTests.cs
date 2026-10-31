using HYWDebugAssistant.Modules.HexSender;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 两个发送器共有的状态句必须逐字一致：同一件事不能在两个模块里说成两句话。
/// </summary>
public sealed class ModuleStatusTextConsistencyTests
{
    [Fact]
    public void 文本发送器与HEX发送器的共享状态句应一致()
    {
        AssertSame(TextSenderText.StatusIntervalInvalid);
        AssertSame(TextSenderText.StatusIntervalInvalidContinuous);
        AssertSame(TextSenderText.StatusIntervalEmptyContinuous);
        AssertSame(TextSenderText.StatusIntervalTooSmall);
        AssertSame(TextSenderText.StatusPageEmpty);
        AssertSame(TextSenderText.StatusPageEmptyContinuous);
        AssertSame(TextSenderText.StatusSplitPackets);
    }

    private static void AssertSame(string key)
    {
        var text = TextCatalog.Get(typeof(TextSenderModule).Assembly, key);
        var other = TextCatalog.Get(typeof(HexSenderModule).Assembly, key);

        other.ShouldBe(text, $"{key} 在两个发送器里必须是同一句话");
    }
}
