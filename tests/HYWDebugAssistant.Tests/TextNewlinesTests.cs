using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>共享换行工具：文本发送器与转换器只有这一套语义。</summary>
public class TextNewlinesTests
{
    [Theory]
    [InlineData("a\r\nb\rc\nd", NewlineMode.CrLf, "a\r\nb\r\nc\r\nd")]
    [InlineData("a\r\nb\rc\nd", NewlineMode.Cr, "a\rb\rc\rd")]
    [InlineData("a\r\nb\rc\nd", NewlineMode.Lf, "a\nb\nc\nd")]
    [InlineData("a\r\nb\rc\nd", NewlineMode.None, "abcd")]
    public void 归一_三种换行和删除(string text, NewlineMode mode, string expected)
    {
        TextNewlines.Normalize(text, mode).ShouldBe(expected);
    }

    [Fact]
    public void 勾选框到换行档()
    {
        TextNewlines.FromCheckboxes(true, true).ShouldBe(NewlineMode.CrLf);
        TextNewlines.FromCheckboxes(true, false).ShouldBe(NewlineMode.Cr);
        TextNewlines.FromCheckboxes(false, true).ShouldBe(NewlineMode.Lf);
        TextNewlines.FromCheckboxes(false, false).ShouldBe(NewlineMode.None);
        TextNewlines.ChoiceOf(NewlineMode.CrLf).ShouldBe("crlf");
        TextNewlines.ParseChoice("lf").ShouldBe(NewlineMode.Lf);
        TextNewlines.ParseChoice("unknown").ShouldBe(NewlineMode.CrLf);
    }
}
