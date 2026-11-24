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

}
