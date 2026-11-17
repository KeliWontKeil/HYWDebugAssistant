using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>显示缓冲换行哨兵的剥离 / 还原。</summary>
public class NewlineEncodingTests
{
    [Fact]
    public void Expand_应保留三种真实换行()
    {
        var text = $"a{NewlineEncoding.CrLfSentinel}\nb{NewlineEncoding.CrSentinel}\nc{NewlineEncoding.LfSentinel}\nd";

        NewlineEncoding.Expand(text).ShouldBe("a\r\nb\rc\nd");
    }

    [Fact]
    public void Expand_无哨兵时原样返回()
    {
        NewlineEncoding.Expand("a\r\nb").ShouldBe("a\r\nb");
    }
}
